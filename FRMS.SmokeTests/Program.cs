using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

// Optional live, read-only smoke checks. No credentials are persisted,
// no mutations are sent to business endpoints, and no response bodies with
// personal data or tokens are printed.
var rawBaseUrl = Environment.GetEnvironmentVariable("PRN212_API_BASE_URL") ?? "http://localhost:5164/";
if (!Uri.TryCreate(rawBaseUrl, UriKind.Absolute, out var address) ||
    address.Scheme is not ("http" or "https"))
{
    Console.Error.WriteLine("PRN212_API_BASE_URL must be an absolute HTTP(S) URL.");
    return 2;
}
var baseUrl = new Uri(address.AbsoluteUri.TrimEnd('/') + "/");
using var http = new HttpClient { BaseAddress = baseUrl, Timeout = TimeSpan.FromSeconds(20) };
var checks = new LiveChecks(http);
return await checks.RunAsync();

internal sealed class LiveChecks(HttpClient http)
{
    private int _passed;
    private int _failed;
    private int _skipped;

    public async Task<int> RunAsync()
    {
        await CheckAsync("API health and database readiness", async () =>
        {
            using var response = await http.GetAsync("health");
            if (!response.IsSuccessStatusCode)
                throw new SmokeFailure($"health returned HTTP {(int)response.StatusCode}");
        });
        await GetCheckAsync("Public facility catalog", "api/v1/facilities?page=1&pageSize=20", token: null);
        await CheckAsync("Unauthenticated account access is blocked", async () =>
        {
            using var response = await http.GetAsync("api/v1/auth/me");
            if (response.StatusCode != HttpStatusCode.Unauthorized)
                throw new SmokeFailure($"expected HTTP 401, received {(int)response.StatusCode}");
        });

        await RunRoleAsync("Customer", "PRN212_CUSTOMER_PHONE", "PRN212_CUSTOMER_PASSWORD",
            "api/v1/auth/customer/login", "CUSTOMER", "phoneNumber", async token =>
            {
                await GetCheckAsync("Customer /auth/me", "api/v1/auth/me", token);
                await GetCheckAsync("Customer reservations", "api/v1/reservations?page=1&pageSize=20", token);
                await GetCheckAsync("Customer contracts", "api/v1/contracts?page=1&pageSize=20", token);
                await GetCheckAsync("Customer invoices", "api/v1/invoices?page=1&pageSize=20", token);
                await GetCheckAsync("Customer support tickets", "api/v1/support-tickets?page=1&pageSize=20", token);
                await CheckAsync("Customer cannot read staff work items", async () =>
                {
                    using var request = NewGet("api/v1/staff/work-items?page=1&pageSize=20", token);
                    using var response = await http.SendAsync(request);
                    if (response.StatusCode != HttpStatusCode.Forbidden)
                        throw new SmokeFailure($"expected HTTP 403, received {(int)response.StatusCode}");
                });
            });

        await RunRoleAsync("Staff", "PRN212_STAFF_EMAIL", "PRN212_STAFF_PASSWORD",
            "api/v1/auth/employee/login", "FACILITY_STAFF", "email", async token =>
            {
                await GetCheckAsync("Staff /auth/me", "api/v1/auth/me", token);
                await GetCheckAsync("Staff work items and history",
                    "api/v1/staff/work-items?allDates=true&includeHistory=true&page=1&pageSize=20", token);
                await GetCheckAsync("Staff support tickets", "api/v1/support-tickets?page=1&pageSize=20", token);
            });

        await RunRoleAsync("Manager", "PRN212_MANAGER_EMAIL", "PRN212_MANAGER_PASSWORD",
            "api/v1/auth/employee/login", "FACILITY_MANAGER", "email", async token =>
            {
                await GetCheckAsync("Manager reservations", "api/v1/reservations?page=1&pageSize=20", token);
                await GetCheckAsync("Manager visits", "api/v1/visits?page=1&pageSize=20", token);
                await GetCheckAsync("Manager support tickets", "api/v1/support-tickets?page=1&pageSize=20", token);
                await CheckAsync("Manager assigned facility storage", async () =>
                {
                    using var doc = await GetDocumentAsync("api/v1/auth/me", token);
                    var facilityId = ReadString(doc.RootElement, "data", "profile", "facilityId");
                    if (!Guid.TryParse(facilityId, out var parsedId))
                        throw new SmokeFailure("manager /auth/me has no valid profile.facilityId");
                    using var items = await GetDocumentAsync(
                        $"api/v1/facilities/{parsedId:D}/storage-units?page=1&pageSize=20", token);
                    RequireEnvelope(items);
                });
            });

        Console.WriteLine($"SUMMARY PASS={_passed} FAIL={_failed} SKIP={_skipped}");
        return _failed == 0 ? 0 : 1;
    }

    private async Task RunRoleAsync(string label, string idEnv, string passwordEnv,
        string route, string expectedRole, string identifierName, Func<string, Task> checks)
    {
        var identifier = Environment.GetEnvironmentVariable(idEnv);
        var password = Environment.GetEnvironmentVariable(passwordEnv);
        if (string.IsNullOrWhiteSpace(identifier) && string.IsNullOrWhiteSpace(password))
        {
            _skipped++;
            Console.WriteLine($"SKIP {label} role smoke checks: {idEnv} and {passwordEnv} not configured");
            return;
        }
        if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(password))
        {
            _failed++;
            Console.WriteLine($"FAIL {label} credentials incomplete: configure both {idEnv} and {passwordEnv}");
            return;
        }

        string? token = null;
        await CheckAsync($"{label} authentication and role", async () =>
        {
            var loginBody = identifierName == "phoneNumber"
                ? new Dictionary<string, string> { ["phoneNumber"] = identifier, ["password"] = password }
                : new Dictionary<string, string> { ["email"] = identifier, ["password"] = password };
            using var response = await http.PostAsJsonAsync(route, loginBody);
            if (!response.IsSuccessStatusCode)
                throw new SmokeFailure($"login returned HTTP {(int)response.StatusCode}");
            using var doc = await ReadDocumentAsync(response);
            var actualRole = ReadString(doc.RootElement, "data", "user", "role");
            token = ReadString(doc.RootElement, "data", "accessToken");
            if (actualRole != expectedRole || string.IsNullOrWhiteSpace(token))
                throw new SmokeFailure("login returned unexpected role or empty access token");
        });
        if (token is not null)
            await checks(token);
        else
        {
            _skipped++;
            Console.WriteLine($"SKIP {label} authenticated reads: login did not succeed");
        }
    }

    private async Task GetCheckAsync(string label, string path, string? token) =>
        await CheckAsync(label, async () =>
        {
            using var json = await GetDocumentAsync(path, token);
            RequireEnvelope(json);
        });

    private async Task<JsonDocument> GetDocumentAsync(string path, string? token)
    {
        using var request = NewGet(path, token);
        using var response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
            throw new SmokeFailure($"GET returned HTTP {(int)response.StatusCode}");
        return await ReadDocumentAsync(response);
    }

    private static HttpRequestMessage NewGet(string path, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (!string.IsNullOrWhiteSpace(token))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task<JsonDocument> ReadDocumentAsync(HttpResponseMessage response)
    {
        try
        {
            await using var stream = await response.Content.ReadAsStreamAsync();
            return await JsonDocument.ParseAsync(stream);
        }
        catch (JsonException)
        {
            throw new SmokeFailure("response was not valid JSON");
        }
    }

    private static void RequireEnvelope(JsonDocument doc)
    {
        if (doc.RootElement.ValueKind != JsonValueKind.Object ||
            !doc.RootElement.TryGetProperty("data", out _))
            throw new SmokeFailure("expected JSON object with a data property");
    }

    private static string ReadString(JsonElement root, params string[] path)
    {
        var current = root;
        foreach (var key in path)
        {
            if (current.ValueKind != JsonValueKind.Object ||
                !current.TryGetProperty(key, out current))
                return "";
        }
        return current.ValueKind == JsonValueKind.String ? current.GetString() ?? "" : "";
    }

    private async Task CheckAsync(string label, Func<Task> check)
    {
        try
        {
            await check();
            _passed++;
            Console.WriteLine($"PASS {label}");
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or
            JsonException or SmokeFailure or InvalidOperationException)
        {
            _failed++;
            // Deliberately omit response bodies, credentials, access tokens and PII.
            Console.WriteLine($"FAIL {label}: {(e is SmokeFailure ? e.Message : e.GetType().Name)}");
        }
    }
}

internal sealed class SmokeFailure(string message) : Exception(message);
