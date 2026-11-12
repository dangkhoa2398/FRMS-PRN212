using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace FRMS.PRN212.WpfConnection.Services;

public sealed class CustomerApiService(HttpClient http)
{
    private string? _token;

    public async Task<(bool Success, string Error)> LoginAsync(string phoneNumber, string password)
    {
        using var response = await http.PostAsJsonAsync("api/v1/auth/customer/login",
            new { phoneNumber, password });
        if (!response.IsSuccessStatusCode)
            return (false, $"Login failed (HTTP {(int)response.StatusCode}). Check your credentials or account status.");
        using var payload = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        if (!payload.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("accessToken", out var access)
            || string.IsNullOrWhiteSpace(access.GetString())
            || !data.TryGetProperty("user", out var user)
            || !user.TryGetProperty("role", out var role)
            || role.GetString() != "CUSTOMER")
            return (false, "Invalid token response or account is not a Customer.");
        _token = access.GetString();
        return (true, "");
    }

    public async Task<(bool Success, string Error, string Role)> LoginEmployeeAsync(string email, string password)
    {
        using var response = await http.PostAsJsonAsync("api/v1/auth/employee/login", new { email, password });
        if (!response.IsSuccessStatusCode)
            return (false, $"Employee login failed (HTTP {(int)response.StatusCode}).", "");
        using var payload = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        if (!payload.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("accessToken", out var access)
            || !data.TryGetProperty("user", out var user)
            || !user.TryGetProperty("role", out var role))
            return (false, "Unexpected employee authentication response.", "");
        string? roleName = role.GetString();
        string? token = access.GetString();
        if (string.IsNullOrWhiteSpace(token) ||
            roleName is not ("FACILITY_STAFF" or "FACILITY_MANAGER"))
            return (false, "Only Facility Staff and Facility Manager are supported in this desktop application.", "");
        _token = token;
        return (true, "", roleName);
    }

    public async Task<JsonDocument> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (!string.IsNullOrEmpty(_token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    public async Task CreateAccessVisitAsync(Guid contractId, DateTime visitDate, CancellationToken cancellationToken = default)
    {
        if (contractId == Guid.Empty) throw new ArgumentException("A contract is required.", nameof(contractId));
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/v1/contracts/{contractId:D}/access-visits")
        {
            Content = JsonContent.Create(new { visitDate = visitDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) })
        };
        if (!string.IsNullOrWhiteSpace(_token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        using var response = await http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public void Logout() => _token = null;
}
