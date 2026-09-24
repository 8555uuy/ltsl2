using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Itsl2.Core.Models;

namespace Itsl2.Core.Services;

public sealed class ModrinthService : IDisposable
{
    private readonly HttpClient _httpClient;

    public ModrinthService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.BaseAddress = new Uri("https://api.modrinth.com/v2/");
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("itsl2/0.1 (+https://github.com/8555uuy/ltsl2)");
    }

    public async Task<IReadOnlyList<ModSearchResult>> SearchAsync(
        string query,
        string gameVersion,
        string? loader = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        var facets = new List<string[]> { new[] { "project_type:mod" }, new[] { $"versions:{gameVersion}" } };
        if (!string.IsNullOrWhiteSpace(loader)) facets.Add(new[] { $"categories:{loader}" });

        var facetsJson = Uri.EscapeDataString(System.Text.Json.JsonSerializer.Serialize(facets));
        var url = $"search?query={Uri.EscapeDataString(query.Trim())}&facets={facetsJson}&limit=20&index=relevance";
        var response = await _httpClient.GetFromJsonAsync<ModrinthSearchResponse>(url, cancellationToken);
        return response?.Hits.Select(hit => new ModSearchResult(
            hit.ProjectId,
            hit.Title,
            hit.Slug,
            hit.Description,
            hit.IconUrl ?? string.Empty,
            hit.Downloads,
            hit.LatestVersion)).ToArray() ?? [];
    }

    public async Task<string> DownloadLatestAsync(
        ModSearchResult mod,
        string gameDirectory,
        string gameVersion,
        string? loader = null,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory)) throw new ArgumentException("游戏目录不能为空", nameof(gameDirectory));

        var versions = await _httpClient.GetFromJsonAsync<List<ModrinthVersion>>(
            $"project/{Uri.EscapeDataString(mod.ProjectId)}/version", cancellationToken) ?? [];
        var version = versions.FirstOrDefault(item =>
            item.GameVersions.Contains(gameVersion, StringComparer.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(loader) || item.Loaders.Contains(loader, StringComparer.OrdinalIgnoreCase)));
        var file = version?.Files.FirstOrDefault(item => item.Primary && item.Filename.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                   ?? version?.Files.FirstOrDefault(item => item.Filename.EndsWith(".jar", StringComparison.OrdinalIgnoreCase));
        if (file is null) throw new InvalidOperationException("没有找到匹配当前 Minecraft 版本的模组文件");

        var modsDirectory = Path.Combine(gameDirectory, "mods");
        Directory.CreateDirectory(modsDirectory);
        var safeFileName = Path.GetFileName(file.Filename);
        var destination = Path.Combine(modsDirectory, safeFileName);

        using var response = await _httpClient.GetAsync(file.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var totalBytes = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = File.Create(destination + ".tmp");
        var buffer = new byte[64 * 1024];
        long receivedBytes = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            receivedBytes += read;
            if (totalBytes is > 0) progress?.Report((double)receivedBytes / totalBytes.Value);
        }

        File.Move(destination + ".tmp", destination, true);
        return destination;
    }

    public void Dispose() => _httpClient.Dispose();

    private sealed record ModrinthSearchResponse([property: JsonPropertyName("hits")] ModrinthHit[] Hits);

    private sealed record ModrinthHit(
        [property: JsonPropertyName("project_id")] string ProjectId,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("slug")] string Slug,
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("icon_url")] string? IconUrl,
        [property: JsonPropertyName("downloads")] long Downloads,
        [property: JsonPropertyName("latest_version")] string? LatestVersion);

    private sealed record ModrinthVersion(
        [property: JsonPropertyName("game_versions")] string[] GameVersions,
        [property: JsonPropertyName("loaders")] string[] Loaders,
        [property: JsonPropertyName("files")] ModrinthFile[] Files);

    private sealed record ModrinthFile(
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("filename")] string Filename,
        [property: JsonPropertyName("primary")] bool Primary);
}