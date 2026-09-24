using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Itsl2.Core.Models;

namespace Itsl2.Core.Services;

public sealed class MinecraftInstallService : IDisposable
{
    private const string VersionManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";
    private const string AuthlibInjectorUrl = "https://github.com/yushijinhun/authlib-injector/releases/download/v1.2.8/authlib-injector-1.2.8.jar";
    private readonly HttpClient _httpClient;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    public MinecraftInstallService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("itsl2/0.1 (+https://github.com/8555uuy/ltsl2)");
    }

    public async Task<PreparedMinecraft> PrepareAsync(
        string versionId,
        string gameDirectory,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        versionId = versionId.Trim();
        if (string.IsNullOrWhiteSpace(versionId)) throw new ArgumentException("Minecraft 版本不能为空", nameof(versionId));
        if (string.IsNullOrWhiteSpace(gameDirectory)) throw new ArgumentException("游戏目录不能为空", nameof(gameDirectory));

        Directory.CreateDirectory(gameDirectory);
        progress?.Report("正在获取 Minecraft 版本清单...");
        var manifest = await _httpClient.GetFromJsonAsync<VersionManifest>(VersionManifestUrl, _jsonOptions, cancellationToken)
                       ?? throw new InvalidOperationException("无法读取 Minecraft 版本清单");
        var version = manifest.Versions.FirstOrDefault(item => item.Id.Equals(versionId, StringComparison.OrdinalIgnoreCase))
                      ?? throw new InvalidOperationException($"未找到 Minecraft 版本 {versionId}");
        var metadata = await _httpClient.GetFromJsonAsync<VersionMetadata>(version.Url, _jsonOptions, cancellationToken)
                       ?? throw new InvalidOperationException("无法读取版本元数据");

        var versionDirectory = Path.Combine(gameDirectory, "versions", version.Id);
        var librariesDirectory = Path.Combine(gameDirectory, "libraries");
        var assetsDirectory = Path.Combine(gameDirectory, "assets");
        var nativesDirectory = Path.Combine(versionDirectory, "natives");
        Directory.CreateDirectory(versionDirectory);
        Directory.CreateDirectory(librariesDirectory);
        Directory.CreateDirectory(nativesDirectory);

        var metadataPath = Path.Combine(versionDirectory, version.Id + ".json");
        await File.WriteAllTextAsync(metadataPath, JsonSerializer.Serialize(metadata, _jsonOptions), cancellationToken);
        var clientJarPath = Path.Combine(versionDirectory, version.Id + ".jar");
        await DownloadCheckedAsync(metadata.Downloads.Client, clientJarPath, progress, "正在下载 Minecraft 客户端...", cancellationToken);

        var libraryPaths = new List<string>();
        foreach (var library in metadata.Libraries.Where(IsAllowed))
        {
            if (library.Downloads?.Artifact is not null)
            {
                var artifactPath = Path.Combine(gameDirectory, "libraries", library.Downloads.Artifact.Path.Replace('/', Path.DirectorySeparatorChar));
                await DownloadCheckedAsync(library.Downloads.Artifact, artifactPath, progress, $"正在下载库 {library.Name}...", cancellationToken);
                libraryPaths.Add(artifactPath);
            }

            var nativeClassifier = GetNativeClassifier(library);
            if (nativeClassifier is not null && library.Downloads?.Classifiers?.TryGetValue(nativeClassifier, out var native) == true)
            {
                var nativePath = Path.Combine(gameDirectory, "libraries", native.Path.Replace('/', Path.DirectorySeparatorChar));
                await DownloadCheckedAsync(native, nativePath, progress, $"正在下载原生库 {library.Name}...", cancellationToken);
                ExtractNativeJar(nativePath, nativesDirectory);
            }
        }

        var assetsIndex = metadata.AssetIndex ?? throw new InvalidOperationException("版本缺少资源索引");
        var indexesDirectory = Path.Combine(assetsDirectory, "indexes");
        var objectsDirectory = Path.Combine(assetsDirectory, "objects");
        var assetsIndexPath = Path.Combine(indexesDirectory, assetsIndex.Id + ".json");
        await DownloadCheckedAsync(
            new DownloadFile(assetsIndex.Url, assetsIndex.Id + ".json", assetsIndex.Sha1, assetsIndex.Size),
            assetsIndexPath, progress, "正在下载资源索引...", cancellationToken);
        await using var assetIndexStream = File.OpenRead(assetsIndexPath);
        var assetData = await JsonSerializer.DeserializeAsync<AssetIndexData>(assetIndexStream, _jsonOptions, cancellationToken)
                        ?? throw new InvalidOperationException("资源索引格式无效");
        foreach (var asset in assetData.Objects.Values)
        {
            var objectPath = Path.Combine(objectsDirectory, asset.Hash[..2], asset.Hash);
            await DownloadCheckedAsync(
                new DownloadFile($"https://resources.download.minecraft.net/{asset.Hash[..2]}/{asset.Hash}", asset.Hash, null, asset.Size),
                objectPath, progress, "正在下载游戏资源...", cancellationToken);
        }

        return new PreparedMinecraft(
            version.Id,
            gameDirectory,
            metadata.MainClass,
            string.Join(Path.PathSeparator, new[] { clientJarPath }.Concat(libraryPaths)),
            assetsDirectory,
            assetsIndex.Id,
            nativesDirectory,
            metadata.Arguments?.Jvm ?? [],
            metadata.Arguments?.Game ?? ParseLegacyArguments(metadata.MinecraftArguments),
            clientJarPath);
    }

    public async Task<string> EnsureAuthlibInjectorAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "itsl2", "runtime");
        var path = Path.Combine(directory, "authlib-injector.jar");
        await DownloadCheckedAsync(new DownloadFile(AuthlibInjectorUrl, "authlib-injector.jar", null), path, progress, "正在准备皮肤站认证组件...", cancellationToken);
        return path;
    }

    private async Task DownloadCheckedAsync(DownloadFile file, string path, IProgress<string>? progress, string message, CancellationToken cancellationToken)
    {
        if (await IsExistingFileValidAsync(file, path, cancellationToken)) return;

        progress?.Report(message);
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporaryPath = path + ".tmp";
        using var response = await _httpClient.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = File.Create(temporaryPath))
        {
            await input.CopyToAsync(output, cancellationToken);
        }

        if (file.Size is not null && new FileInfo(temporaryPath).Length != file.Size)
        {
            File.Delete(temporaryPath);
            throw new InvalidDataException($"下载文件大小校验失败：{file.Url}");
        }
        if (!string.IsNullOrWhiteSpace(file.Sha1))
        {
            await using var stream = File.OpenRead(temporaryPath);
            var actualSha1 = Convert.ToHexString(await SHA1.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
            if (!actualSha1.Equals(file.Sha1, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(temporaryPath);
                throw new InvalidDataException($"下载文件 SHA-1 校验失败：{file.Url}");
            }
        }

        File.Move(temporaryPath, path, true);
    }

    private static async Task<bool> IsExistingFileValidAsync(DownloadFile file, string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path)) return false;
        if (file.Size is not null && new FileInfo(path).Length != file.Size) return false;
        if (string.IsNullOrWhiteSpace(file.Sha1)) return true;

        await using var stream = File.OpenRead(path);
        var actualSha1 = Convert.ToHexString(await SHA1.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        return actualSha1.Equals(file.Sha1, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAllowed(Library library)
    {
        if (library.Rules is null || library.Rules.Length == 0) return true;
        var allowed = false;
        foreach (var rule in library.Rules)
        {
            if (!MatchesCurrentOs(rule.Os)) continue;
            allowed = rule.Action.Equals("allow", StringComparison.OrdinalIgnoreCase);
        }
        return allowed;
    }

    private static bool MatchesCurrentOs(OsPlatform? os)
    {
        if (os?.Name is null) return true;
        return os.Name switch
        {
            "windows" => OperatingSystem.IsWindows(),
            "linux" => OperatingSystem.IsLinux(),
            "osx" => OperatingSystem.IsMacOS(),
            _ => false
        };
    }

    private static string? GetNativeClassifier(Library library)
    {
        if (library.Natives is null) return null;
        if (OperatingSystem.IsWindows() && library.Natives.TryGetValue("windows", out var windows)) return windows.Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32");
        if (OperatingSystem.IsLinux() && library.Natives.TryGetValue("linux", out var linux)) return linux.Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32");
        if (OperatingSystem.IsMacOS() && library.Natives.TryGetValue("osx", out var osx)) return osx.Replace("${arch}", Environment.Is64BitOperatingSystem ? "64" : "32");
        return null;
    }

    private static void ExtractNativeJar(string path, string destination)
    {
        using var archive = System.IO.Compression.ZipFile.OpenRead(path);
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name) || entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase)) continue;
            var outputPath = Path.GetFullPath(Path.Combine(destination, entry.FullName));
            var root = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
            if (!outputPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("原生库包含非法路径");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            entry.ExtractToFile(outputPath, true);
        }
    }

    private static JsonElement[] ParseLegacyArguments(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments)) return [];
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(
            arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(value => value.Trim('"'))));
        return document.RootElement.EnumerateArray().Select(item => item.Clone()).ToArray();
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed record VersionManifest([property: JsonPropertyName("versions")] VersionEntry[] Versions);
    private sealed record VersionEntry([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("url")] string Url);
    private sealed record VersionMetadata(
        [property: JsonPropertyName("mainClass")] string MainClass,
        [property: JsonPropertyName("downloads")] VersionDownloads Downloads,
        [property: JsonPropertyName("libraries")] Library[] Libraries,
        [property: JsonPropertyName("assetIndex")] AssetIndexFile? AssetIndex,
        [property: JsonPropertyName("arguments")] MinecraftArguments? Arguments,
        [property: JsonPropertyName("minecraftArguments")] string? MinecraftArguments);
    private sealed record MinecraftArguments(
        [property: JsonPropertyName("game")] JsonElement[] Game,
        [property: JsonPropertyName("jvm")] JsonElement[] Jvm);
    private sealed record VersionDownloads([property: JsonPropertyName("client")] DownloadFile Client);
    private sealed record Library(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("downloads")] LibraryDownloads? Downloads,
        [property: JsonPropertyName("rules")] OsRule[]? Rules,
        [property: JsonPropertyName("natives")] Dictionary<string, string>? Natives);
    private sealed record LibraryDownloads(
        [property: JsonPropertyName("artifact")] DownloadFile? Artifact,
        [property: JsonPropertyName("classifiers")] Dictionary<string, DownloadFile>? Classifiers);
    private sealed record OsRule([property: JsonPropertyName("action")] string Action, [property: JsonPropertyName("os")] OsPlatform? Os);
    private sealed record OsPlatform([property: JsonPropertyName("name")] string? Name);
    private sealed record AssetIndexFile(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("sha1")] string? Sha1,
        [property: JsonPropertyName("size")] long? Size);
    private sealed record DownloadFile(
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("path")] string Path,
        [property: JsonPropertyName("sha1")] string? Sha1 = null,
        [property: JsonPropertyName("size")] long? Size = null);
    private sealed record AssetIndexData([property: JsonPropertyName("objects")] Dictionary<string, AssetObject> Objects);
    private sealed record AssetObject([property: JsonPropertyName("hash")] string Hash, [property: JsonPropertyName("size")] long Size);
}