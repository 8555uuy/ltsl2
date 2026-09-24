using System.Diagnostics;
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
}