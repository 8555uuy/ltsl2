using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Itsl2.Core.Models;

namespace Itsl2.Core.Services;

public sealed class YggdrasilAuthService
{
    private readonly HttpClient _httpClient;

    public YggdrasilAuthService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("itsl2/0.1 (+https://github.com/8555uuy/ltsl2)");
    }

    public async Task<ThirdPartyAccount> AuthenticateAsync(
        string serverUrl,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var baseUrl = NormalizeServerUrl(serverUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        var request = new YggdrasilLoginRequest(
            new YggdrasilAgent("Minecraft", 1),
            username.Trim(),
            password,
            Guid.NewGuid().ToString("N"),
            true);

        using var response = await _httpClient.PostAsJsonAsync(
            $"{baseUrl}/authserver/authenticate", request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw CreateAuthenticationException(response.StatusCode, body);

        var result = JsonSerializer.Deserialize<YggdrasilLoginResponse>(body)
                     ?? throw new InvalidOperationException("皮肤站返回了空响应");
        var profile = result.SelectedProfile ?? result.AvailableProfiles?.FirstOrDefault()
                      ?? throw new InvalidOperationException("该账户没有可用角色");
        if (string.IsNullOrWhiteSpace(result.AccessToken) || string.IsNullOrWhiteSpace(profile.Id))
            throw new InvalidOperationException("皮肤站响应缺少必要的登录信息");

        return new ThirdPartyAccount(baseUrl, username.Trim(), profile.Name, profile.Id, result.AccessToken);
    }

    private static string NormalizeServerUrl(string serverUrl)
    {
        if (!Uri.TryCreate(serverUrl.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http")
            || string.IsNullOrWhiteSpace(uri.Host))
            throw new ArgumentException("皮肤站地址必须是有效的 HTTP 或 HTTPS 地址", nameof(serverUrl));

        var isLocalHttp = uri.Scheme == Uri.UriSchemeHttp
                  && (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                      || IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address));
        if (uri.Scheme == Uri.UriSchemeHttp && !isLocalHttp)
            throw new ArgumentException("非本机皮肤站必须使用 HTTPS", nameof(serverUrl));

        return uri.ToString().TrimEnd('/');
    }

    private static Exception CreateAuthenticationException(HttpStatusCode statusCode, string body)
    {
        try
        {
            var error = JsonSerializer.Deserialize<YggdrasilError>(body);
            if (!string.IsNullOrWhiteSpace(error?.ErrorMessage))
                return new InvalidOperationException($"皮肤站登录失败：{error.ErrorMessage}");
            if (!string.IsNullOrWhiteSpace(error?.Error))
                return new InvalidOperationException($"皮肤站登录失败：{error.Error}");
        }
        catch (JsonException)
        {
            // Some servers return an HTML error page; use the HTTP status below.
        }

        return new HttpRequestException($"皮肤站登录失败，HTTP {(int)statusCode} ({statusCode})");
    }

    private sealed record YggdrasilLoginRequest(
        [property: JsonPropertyName("agent")] YggdrasilAgent Agent,
        [property: JsonPropertyName("username")] string Username,
        [property: JsonPropertyName("password")] string Password,
        [property: JsonPropertyName("clientToken")] string ClientToken,
        [property: JsonPropertyName("requestUser")] bool RequestUser);

    private sealed record YggdrasilAgent(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("version")] int Version);

    private sealed record YggdrasilLoginResponse(
        [property: JsonPropertyName("accessToken")] string? AccessToken,
        [property: JsonPropertyName("selectedProfile")] YggdrasilProfile? SelectedProfile,
        [property: JsonPropertyName("availableProfiles")] YggdrasilProfile[]? AvailableProfiles);

    private sealed record YggdrasilProfile(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("name")] string Name);

    private sealed record YggdrasilError(
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("errorMessage")] string? ErrorMessage);
}