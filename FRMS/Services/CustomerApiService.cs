using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FRMS.PRN212.WpfConnection.Services;

/// <summary>
/// HTTP boundary for the desktop client. Authentication state is session-scoped;
/// request-level bearer headers avoid changing shared HttpClient defaults.
/// </summary>
public sealed class CustomerApiService(HttpClient http)
{
    private string? _token;

    public async Task<(bool Success, string Error)> LoginAsync(
        string phoneNumber, string password, CancellationToken cancellationToken = default)
    {
        // An unsuccessful sign-in must never retain the previous account's token.
        Logout();
        using var response = await http.PostAsJsonAsync(
            "api/v1/auth/customer/login", new { phoneNumber, password }, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return (false, $"Login failed (HTTP {(int)response.StatusCode}). Check your credentials or account status.");

        var credentials = await ReadCredentialsAsync(response, cancellationToken);
        if (credentials is null || credentials.Value.Role != "CUSTOMER")
            return (false, "Invalid token response or account is not a Customer.");

        _token = credentials.Value.Token;
        return (true, "");
    }

    public async Task<(bool Success, string Error, string Role)> LoginEmployeeAsync(
        string email, string password, CancellationToken cancellationToken = default)
    {
        Logout();
        using var response = await http.PostAsJsonAsync(
            "api/v1/auth/employee/login", new { email, password }, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return (false, $"Employee login failed (HTTP {(int)response.StatusCode}).", "");

        var credentials = await ReadCredentialsAsync(response, cancellationToken);
        if (credentials is null)
            return (false, "Unexpected employee authentication response.", "");

        if (credentials.Value.Role is not ("FACILITY_STAFF" or "FACILITY_MANAGER"))
            return (false, "Only Facility Staff and Facility Manager are supported in this desktop application.", "");

        _token = credentials.Value.Token;
        return (true, "", credentials.Value.Role);
    }

    public async Task<JsonDocument> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(HttpMethod.Get, path);
        using var response = await http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    public async Task CreateAccessVisitAsync(
        Guid contractId, DateTime visitDate, CancellationToken cancellationToken = default)
    {
        if (contractId == Guid.Empty)
            throw new ArgumentException("A contract is required.", nameof(contractId));

        var body = new
        {
            visitDate = visitDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)
        };
        using var request = CreateRequest(
            HttpMethod.Post, $"api/v1/contracts/{contractId:D}/access-visits", body);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<JsonDocument> SendJsonAsync(
        HttpMethod method, string path, object? body = null, CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(method, path, body);
        using var response = await http.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException(
                $"API request failed (HTTP {(int)response.StatusCode}): {responseText}",
                null, response.StatusCode);

        // POST/DELETE endpoints may legitimately return 204 No Content.
        return JsonDocument.Parse(string.IsNullOrWhiteSpace(responseText) ? "{}" : responseText);
    }

    public void Logout() => _token = null;

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        if (!string.IsNullOrWhiteSpace(_token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        return request;
    }

    private static async Task<(string Token, string Role)?> ReadCredentialsAsync(
        HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var payload = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!TryGetObject(payload.RootElement, "data", out var data)
                || !TryGetString(data, "accessToken", out var token)
                || !TryGetObject(data, "user", out var user)
                || !TryGetString(user, "role", out var role))
                return null;
            return (token, role);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryGetObject(JsonElement element, string name, out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out value)
            && value.ValueKind == JsonValueKind.Object;
    }

    private static bool TryGetString(JsonElement element, string name, out string value)
    {
        value = "";
        if (element.ValueKind != JsonValueKind.Object
            || !element.TryGetProperty(name, out var property)
            || property.ValueKind != JsonValueKind.String)
            return false;
        value = property.GetString() ?? "";
        return !string.IsNullOrWhiteSpace(value);
    }
}
