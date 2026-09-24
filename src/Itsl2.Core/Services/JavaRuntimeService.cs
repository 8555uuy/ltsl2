using System.Diagnostics;

namespace Itsl2.Core.Services;

public sealed record JavaRuntime(string ExecutablePath, Version Version, string Vendor);

public sealed class JavaRuntimeService
{
    public async Task<JavaRuntime?> DetectAsync(string? preferredPath = null, CancellationToken cancellationToken = default)
    {
        var candidates = new List<string>();
        if (!string.IsNullOrWhiteSpace(preferredPath)) candidates.Add(preferredPath);

        var javaOnPath = OperatingSystem.IsWindows() ? "java.exe" : "java";
        candidates.Add(javaOnPath);

        foreach (var candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var runtime = await TryReadRuntimeAsync(candidate, cancellationToken);
            if (runtime is not null) return runtime;
        }

        return null;
    }

    private static async Task<JavaRuntime?> TryReadRuntimeAsync(string executablePath, CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = executablePath,
                    Arguments = "-version",
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var outputTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = string.Concat(await outputTask, await standardOutputTask);
            var version = ParseVersion(output);
            return process.ExitCode == 0 && version is not null
                ? new JavaRuntime(executablePath, version, ParseVendor(output))
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Version? ParseVersion(string output)
    {
        var marker = "version \"";
        var start = output.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;

        start += marker.Length;
        var end = output.IndexOf('"', start);
        if (end < 0) return null;

        var value = output[start..end].Split('-', '+')[0];
        if (value.StartsWith("1.", StringComparison.Ordinal)) value = value[2..];
        return Version.TryParse(value, out var version) ? version : null;
    }

    private static string ParseVendor(string output)
    {
        var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return firstLine?.Trim() ?? "未知发行版";
    }
}