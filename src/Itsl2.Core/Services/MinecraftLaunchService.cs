using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Itsl2.Core.Models;

namespace Itsl2.Core.Services;

public sealed record LaunchRequest(GameInstance Instance, JavaRuntime Runtime, string MainClass, string Classpath);

public sealed class MinecraftLaunchService
{
    public ProcessStartInfo BuildStartInfo(LaunchRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MainClass);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Classpath);

        return new ProcessStartInfo
        {
            FileName = request.Runtime.ExecutablePath,
            WorkingDirectory = request.Instance.GameDirectory,
            UseShellExecute = false,
            Arguments = $"-cp \"{request.Classpath}\" {request.MainClass}"
        };
    }

    public ProcessStartInfo BuildStartInfo(
        PreparedMinecraft prepared,
        JavaRuntime runtime,
        ThirdPartyAccount? account = null,
        string? authlibInjectorPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prepared.MainClass);
        ArgumentException.ThrowIfNullOrWhiteSpace(prepared.Classpath);
        if (!Directory.Exists(prepared.GameDirectory)) throw new DirectoryNotFoundException(prepared.GameDirectory);

        var playerName = account?.ProfileName ?? "itsl2_player";
        var playerUuid = account?.ProfileId ?? CreateOfflineUuid(playerName);
        var accessToken = account?.AccessToken ?? "0";
        var arguments = new List<string>();
        foreach (var argument in prepared.JvmArguments)
            AddArgument(arguments, argument, prepared, account, playerName, playerUuid, accessToken);

        arguments.Add($"-Djava.library.path={prepared.NativesDirectory}");
        arguments.Add("-Dminecraft.launcher.brand=itsl2");
        arguments.Add("-Dminecraft.launcher.version=0.1");
        if (account is not null)
        {
            if (string.IsNullOrWhiteSpace(authlibInjectorPath) || !File.Exists(authlibInjectorPath))
                throw new FileNotFoundException("未找到 authlib-injector，请先准备皮肤站认证组件", authlibInjectorPath);
            arguments.Add($"-javaagent:{authlibInjectorPath}={account.ServerUrl}");
            arguments.Add("-Dauthlibinjector.side=client");
        }

        arguments.Add("-cp");
        arguments.Add(prepared.Classpath);
        arguments.Add(prepared.MainClass);
        foreach (var argument in prepared.GameArguments)
            AddArgument(arguments, argument, prepared, account, playerName, playerUuid, accessToken);

        var startInfo = new ProcessStartInfo
        {
            FileName = runtime.ExecutablePath,
            WorkingDirectory = prepared.GameDirectory,
            UseShellExecute = false,
            CreateNoWindow = false
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    public Process Launch(PreparedMinecraft prepared, JavaRuntime runtime, ThirdPartyAccount? account = null, string? authlibInjectorPath = null)
    {
        var process = Process.Start(BuildStartInfo(prepared, runtime, account, authlibInjectorPath));
        return process ?? throw new InvalidOperationException("无法启动 Java 进程");
    }

    private static void AddArgument(
        ICollection<string> output,
        JsonElement argument,
        PreparedMinecraft prepared,
        ThirdPartyAccount? account,
        string playerName,
        string playerUuid,
        string accessToken)
    {
        if (argument.ValueKind == JsonValueKind.String)
        {
            output.Add(ReplacePlaceholders(argument.GetString() ?? "", prepared, account, playerName, playerUuid, accessToken));
            return;
        }

        if (argument.ValueKind != JsonValueKind.Object || !IsArgumentAllowed(argument)) return;
        if (!argument.TryGetProperty("value", out var value)) return;
        if (value.ValueKind == JsonValueKind.String)
            output.Add(ReplacePlaceholders(value.GetString() ?? "", prepared, account, playerName, playerUuid, accessToken));
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray())
                if (item.ValueKind == JsonValueKind.String)
                    output.Add(ReplacePlaceholders(item.GetString() ?? "", prepared, account, playerName, playerUuid, accessToken));
    }

    private static bool IsArgumentAllowed(JsonElement argument)
    {
        if (!argument.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Array) return true;
        var allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            if (!rule.TryGetProperty("action", out var action)) continue;
            if (rule.TryGetProperty("os", out var os) && os.TryGetProperty("name", out var name)
                && !IsCurrentOs(name.GetString())) continue;
            allowed = action.GetString()?.Equals("allow", StringComparison.OrdinalIgnoreCase) == true;
        }
        return allowed;
    }

    private static bool IsCurrentOs(string? name) => name switch
    {
        "windows" => OperatingSystem.IsWindows(),
        "linux" => OperatingSystem.IsLinux(),
        "osx" => OperatingSystem.IsMacOS(),
        _ => false
    };

    private static string ReplacePlaceholders(string value, PreparedMinecraft prepared, ThirdPartyAccount? account,
        string playerName, string playerUuid, string accessToken) => value
        .Replace("${natives_directory}", prepared.NativesDirectory, StringComparison.Ordinal)
        .Replace("${library_directory}", Path.Combine(prepared.GameDirectory, "libraries"), StringComparison.Ordinal)
        .Replace("${classpath}", prepared.Classpath, StringComparison.Ordinal)
        .Replace("${assets_root}", prepared.AssetsDirectory, StringComparison.Ordinal)
        .Replace("${assets_index_name}", prepared.AssetsIndex, StringComparison.Ordinal)
        .Replace("${version_name}", prepared.VersionId, StringComparison.Ordinal)
        .Replace("${version_type}", "release", StringComparison.Ordinal)
        .Replace("${auth_player_name}", playerName, StringComparison.Ordinal)
        .Replace("${auth_uuid}", playerUuid.Replace("-", "", StringComparison.Ordinal), StringComparison.Ordinal)
        .Replace("${auth_access_token}", accessToken, StringComparison.Ordinal)
        .Replace("${auth_session}", accessToken, StringComparison.Ordinal)
        .Replace("${user_type}", account is null ? "mojang" : "msa", StringComparison.Ordinal)
        .Replace("${user_properties}", "{}", StringComparison.Ordinal);

    private static string CreateOfflineUuid(string playerName)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes("OfflinePlayer:" + playerName));
        return new Guid(hash).ToString();
    }
}