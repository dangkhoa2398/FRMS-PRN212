using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FRMS.PRN212.WpfConnection.Services;
using Xunit;

namespace FRMS.Tests;

public sealed class CustomerApiServiceTests
{
    [Fact]
    public async Task CustomerLogin_AcceptsCustomerToken_AndAddsBearerToNextRequest()
    {
        var handler = new RecordingHandler(request => request.Path.Contains("/login", StringComparison.Ordinal)
            ? JsonResponse("""{"data":{"accessToken":"customer-jwt","user":{"role":"CUSTOMER"}}}""")
            : JsonResponse("""{"data":[{"facilityId":"f1"}]}"""));
        using var client = CreateClient(handler);
        var service = new CustomerApiService(client);

        var login = await service.LoginAsync("0900000000", "password");
        using var response = await service.GetAsync("api/v1/facilities");

        Assert.True(login.Success);
        Assert.Equal("", login.Error);
        Assert.Equal("Bearer customer-jwt", handler.Requests[1].Authorization);
        Assert.Equal("/api/v1/facilities", handler.Requests[1].Path);
        Assert.Equal(JsonValueKind.Array, response.RootElement.GetProperty("data").ValueKind);
        Assert.Null(handler.Requests[0].Authorization);
    }

    [Theory]
    [InlineData("FACILITY_STAFF")]
    [InlineData("FACILITY_MANAGER")]
    public async Task EmployeeLogin_AcceptsSupportedRoles(string role)
    {
        var handler = new RecordingHandler(_ => JsonResponse(
            $$"""{"data":{"accessToken":"employee-jwt","user":{"role":"{{role}}"}}}"""));
        using var client = CreateClient(handler);
        var service = new CustomerApiService(client);

        var login = await service.LoginEmployeeAsync("staff@example.com", "password");
        using var _ = await service.GetAsync("api/v1/visits");

        Assert.True(login.Success);
        Assert.Equal(role, login.Role);
        Assert.Equal("Bearer employee-jwt", handler.Requests[1].Authorization);
        Assert.Equal("/api/v1/auth/employee/login", handler.Requests[0].Path);
    }

    [Fact]
    public async Task EmployeeLogin_RejectsCustomerRole_WithoutSavingToken()
    {
        var handler = new RecordingHandler(request => request.Path.Contains("/login", StringComparison.Ordinal)
            ? JsonResponse("""{"data":{"accessToken":"wrong-role","user":{"role":"CUSTOMER"}}}""")
            : JsonResponse("{}"));
        using var client = CreateClient(handler);
        var service = new CustomerApiService(client);

        var login = await service.LoginEmployeeAsync("test@example.com", "password");
        using var _ = await service.GetAsync("api/v1/visits");

        Assert.False(login.Success);
        Assert.Equal("", login.Role);
        Assert.Null(handler.Requests[1].Authorization);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{}")]
    [InlineData("{\"data\":{\"accessToken\":123,\"user\":{\"role\":\"CUSTOMER\"}}}")]
    public async Task CustomerLogin_RejectsInvalidAuthenticationPayload(string payload)
    {
        var handler = new RecordingHandler(_ => JsonResponse(payload));
        using var client = CreateClient(handler);

        var result = await new CustomerApiService(client).LoginAsync("0900000000", "password");

        Assert.False(result.Success);
        Assert.Contains("Invalid token response", result.Error);
    }

    [Fact]
    public async Task FailedLogin_ClearsPreviouslyAuthenticatedSession()
    {
        var loginCount = 0;
        var handler = new RecordingHandler(request => request.Path.Contains("/login", StringComparison.Ordinal)
            ? ++loginCount == 1
                ? JsonResponse("""{"data":{"accessToken":"old-jwt","user":{"role":"CUSTOMER"}}}""")
                : new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : JsonResponse("{}"));
        using var client = CreateClient(handler);
        var service = new CustomerApiService(client);

        Assert.True((await service.LoginAsync("0900000000", "correct")).Success);
        Assert.False((await service.LoginAsync("0900000000", "incorrect")).Success);
        using var _ = await service.GetAsync("api/v1/reservations");

        Assert.Null(handler.Requests[2].Authorization);
    }

    [Fact]
    public async Task Logout_RemovesBearerHeader()
    {
        var handler = new RecordingHandler(request => request.Path.Contains("/login", StringComparison.Ordinal)
            ? JsonResponse("""{"data":{"accessToken":"jwt","user":{"role":"CUSTOMER"}}}""")
            : JsonResponse("{}"));
        using var client = CreateClient(handler);
        var service = new CustomerApiService(client);

        Assert.True((await service.LoginAsync("0900000000", "password")).Success);
        service.Logout();
        using var _ = await service.GetAsync("api/v1/contracts");

        Assert.Null(handler.Requests[1].Authorization);
    }

    [Fact]
    public async Task CreateAccessVisit_SendsExpectedRouteAndDateOnlyJson()
    {
        var handler = new RecordingHandler(_ => JsonResponse("{}", HttpStatusCode.Created));
        using var client = CreateClient(handler);
        var service = new CustomerApiService(client);
        var contractId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

        await service.CreateAccessVisitAsync(contractId, new DateTime(2026, 11, 30, 23, 0, 0));

        Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal($"/api/v1/contracts/{contractId:D}/access-visits", handler.Requests[0].Path);
        using var payload = JsonDocument.Parse(handler.Requests[0].Body!);
        Assert.Equal("2026-11-30", payload.RootElement.GetProperty("visitDate").GetString());
    }

    [Fact]
    public async Task CreateAccessVisit_RejectsEmptyContractWithoutHttpCall()
    {
        var handler = new RecordingHandler(_ => JsonResponse("{}"));
        using var client = CreateClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new CustomerApiService(client).CreateAccessVisitAsync(Guid.Empty, DateTime.Today));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task SendJson_NoContent_ReturnsEmptyJsonObject()
    {
        var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        using var client = CreateClient(handler);

        using var response = await new CustomerApiService(client).SendJsonAsync(HttpMethod.Delete, "api/v1/visits/1");

        Assert.Equal(JsonValueKind.Object, response.RootElement.ValueKind);
        Assert.Empty(response.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task SendJson_Conflict_PreservesHttpStatus()
    {
        var handler = new RecordingHandler(_ => JsonResponse("""{"error":{"code":"VISIT_INVALID_STATUS"}}""",
            HttpStatusCode.Conflict));
        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            new CustomerApiService(client).SendJsonAsync(HttpMethod.Post, "api/v1/visits/1/cancel"));

        Assert.Equal(HttpStatusCode.Conflict, exception.StatusCode);
        Assert.Contains("VISIT_INVALID_STATUS", exception.Message);
    }

    private static HttpClient CreateClient(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri("http://localhost:5164/")
    };

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed record CapturedRequest(HttpMethod Method, string Path, string? Authorization, string? Body);

    private sealed class RecordingHandler(Func<CapturedRequest, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            var captured = new CapturedRequest(request.Method, request.RequestUri!.AbsolutePath,
                request.Headers.Authorization?.ToString(), body);
            Requests.Add(captured);
            return respond(captured);
        }
    }
}
