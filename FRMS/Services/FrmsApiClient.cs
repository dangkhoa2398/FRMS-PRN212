using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Frms.Wpf.Integration;

/// <summary>
/// Connects the WPF application to the existing FRMS ASP.NET Core REST API.
/// This class never connects to PostgreSQL directly.
/// </summary>
public sealed class FrmsApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public FrmsApiClient(string baseUrl = "http://localhost:5164/")
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("A valid HTTP(S) API base URL is required.", nameof(baseUrl));
        }

        _http = new HttpClient { BaseAddress = uri, Timeout = TimeSpan.FromSeconds(20) };
        _ownsClient = true;
    }

    public FrmsApiClient(HttpClient httpClient)
    {
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        if (_http.BaseAddress is null)
            throw new ArgumentException("HttpClient.BaseAddress must be configured.", nameof(httpClient));
        _ownsClient = false;
    }

    public void SetBearerToken(string? token)
    {
        _http.DefaultRequestHeaders.Authorization = string.IsNullOrWhiteSpace(token)
            ? null
            : new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<bool> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync("health", cancellationToken).ConfigureAwait(false);
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Public endpoint. The response contains the backend's data/pagination envelope.
    /// </summary>
    public Task<JsonDocument> GetFacilitiesAsync(
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize < 1) throw new ArgumentOutOfRangeException(nameof(page));
        return GetJsonAsync($"api/v1/facilities?page={page}&pageSize={pageSize}", cancellationToken);
    }

    public async Task<JsonDocument> GetJsonAsync(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(relativePath, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }
}
