using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace FRMS.Wpf.Services;

public sealed class FrmsApiClient
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    public async Task<(int StatusCode, string Body)> GetAsync(
        string baseUrl, string path, string? accessToken = null)
    {
        if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("API URL phải bắt đầu bằng http:// hoặc https://.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(uri, path.TrimStart('/')));
        if (!string.IsNullOrWhiteSpace(accessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await Http.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();

        try
        {
            using var json = JsonDocument.Parse(raw);
            raw = JsonSerializer.Serialize(json.RootElement,
                new JsonSerializerOptions { WriteIndented = true });
        }
        catch (JsonException)
        {
            // Health endpoints can return plain text.
        }

        return ((int)response.StatusCode, raw);
    }
}
