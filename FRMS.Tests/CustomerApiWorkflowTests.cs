using System.Net;
using System.Net.Http;
using System.Text.Json;
using FRMS.PRN212.WpfConnection.Services;
using Xunit;

namespace FRMS.Tests;

public sealed class CustomerApiWorkflowTests
{
    private static CustomerApiService NewService(TestTransport transport, out HttpClient client)
    {
        client = TestTransport.CreateClient(transport);
        return new CustomerApiService(client);
    }

    [Theory]
    [InlineData("CUSTOMER", true)]
    [InlineData("FACILITY_MANAGER", false)]
    [InlineData("FACILITY_STAFF", false)]
    [InlineData("SYSTEM_ADMINISTRATOR", false)]
    [InlineData("", false)]
    public async Task CustomerLogin_RequiresCustomerRole(string role, bool accepted)
    {
        var transport = new TestTransport(_ => TestTransport.Json(TestTransport.Auth(role)));
        var service = NewService(transport, out var client);
        using var ownedClient = client;
        var result = await service.LoginAsync("0900000000", "password");
        Assert.Equal(accepted, result.Success);
        Assert.Equal("/api/v1/auth/customer/login", transport.Requests[0].Path);
        using var body = JsonDocument.Parse(transport.Requests[0].Body!);
        Assert.Equal("0900000000", body.RootElement.GetProperty("phoneNumber").GetString());
        Assert.Equal("password", body.RootElement.GetProperty("password").GetString());
    }

    [Theory]
    [InlineData("FACILITY_MANAGER", true)]
    [InlineData("FACILITY_STAFF", true)]
    [InlineData("CUSTOMER", false)]
    [InlineData("BUSINESS_OPERATIONS_MANAGER", false)]
    [InlineData("SYSTEM_ADMINISTRATOR", false)]
    public async Task EmployeeLogin_AcceptsOnlyDesktopRoles(string role, bool accepted)
    {
        var transport = new TestTransport(_ => TestTransport.Json(TestTransport.Auth(role)));
        var service = NewService(transport, out var client);
        using var ownedClient = client;
        var result = await service.LoginEmployeeAsync("staff@site.test", "secret");
        Assert.Equal(accepted, result.Success);
        Assert.Equal(accepted ? role : "", result.Role);
        using var body = JsonDocument.Parse(transport.Requests[0].Body!);
        Assert.Equal("staff@site.test", body.RootElement.GetProperty("email").GetString());
        Assert.Equal("secret", body.RootElement.GetProperty("password").GetString());
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(429)]
    [InlineData(500)]
    public async Task CustomerLogin_HttpFailures_AreReportedWithoutThrowing(int status)
    {
        var transport = new TestTransport(_ => TestTransport.Json("{}", (HttpStatusCode)status));
        var service = NewService(transport, out var client);
        using var ownedClient = client;
        var result = await service.LoginAsync("phone", "pw");
        Assert.False(result.Success);
        Assert.Contains(status.ToString(), result.Error);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(503)]
    public async Task EmployeeLogin_HttpFailures_AreReportedWithoutThrowing(int status)
    {
        var transport = new TestTransport(_ => TestTransport.Json("{}", (HttpStatusCode)status));
        var service = NewService(transport, out var client);
        using var ownedClient = client;
        var result = await service.LoginEmployeeAsync("email", "pw");
        Assert.False(result.Success);
        Assert.Equal("", result.Role);
        Assert.Contains(status.ToString(), result.Error);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{\"data\":[]}")]
    [InlineData("{\"data\":{\"accessToken\":\"token\",\"user\":{\"role\":42}}}")]
    [InlineData("{\"data\":{\"accessToken\":null,\"user\":{\"role\":\"CUSTOMER\"}}}")]
    [InlineData("{\"data\":{\"accessToken\":\"  \",\"user\":{\"role\":\"CUSTOMER\"}}}")]
    public async Task CustomerLogin_MalformedEnvelope_IsRejected(string payload)
    {
        var transport = new TestTransport(_ => TestTransport.Json(payload));
        var service = NewService(transport, out var client);
        using var ownedClient = client;
        Assert.False((await service.LoginAsync("phone", "pw")).Success);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("{\"data\":{\"accessToken\":\"token\",\"user\":{}}}")]
    public async Task EmployeeLogin_InvalidPayload_IsRejected(string payload)
    {
        var transport = new TestTransport(_ => TestTransport.Json(payload));
        var service = NewService(transport, out var client);
        using var ownedClient = client;
        var result = await service.LoginEmployeeAsync("staff", "pw");
        Assert.False(result.Success);
    }

    [Fact]
    public async Task SwitchingEmployeeToCustomer_ReplacesBearerToken()
    {
        var transport = new TestTransport(r => r.Path.Contains("employee/login")
            ? TestTransport.Json(TestTransport.Auth("FACILITY_STAFF", "staff-token"))
            : r.Path.Contains("customer/login")
                ? TestTransport.Json(TestTransport.Auth("CUSTOMER", "customer-token"))
                : TestTransport.Json());
        var service = NewService(transport, out var client);
        using var ownedClient = client;
        Assert.True((await service.LoginEmployeeAsync("email", "pw")).Success);
        using (var initial = await service.GetAsync("api/v1/contracts"))
            Assert.Equal("Bearer staff-token", transport.Requests[^1].Authorization);
        Assert.True((await service.LoginAsync("phone", "pw")).Success);
        using (var next = await service.GetAsync("api/v1/contracts"))
            Assert.Equal("Bearer customer-token", transport.Requests[^1].Authorization);
        Assert.Null(transport.Requests[2].Authorization);
    }

    [Fact]
    public async Task FailedEmployeeLogin_RevokesPreviousCustomerToken()
    {
        var transport = new TestTransport(r => r.Path.Contains("customer/login")
            ? TestTransport.Json(TestTransport.Auth("CUSTOMER", "old-token"))
            : r.Path.Contains("employee/login")
                ? TestTransport.Json("{}", HttpStatusCode.Unauthorized)
                : TestTransport.Json());
        var service = NewService(transport, out var client);
        using var ownedClient = client;
        Assert.True((await service.LoginAsync("phone", "pw")).Success);
        Assert.False((await service.LoginEmployeeAsync("email", "pw")).Success);
        using var data = await service.GetAsync("api/v1/invoices");
        Assert.Null(transport.Requests[^1].Authorization);
    }

    [Theory]
    [InlineData("api/v1/reservations", HttpStatusCode.OK)]
    [InlineData("api/v1/contracts", HttpStatusCode.OK)]
    [InlineData("api/v1/visits", HttpStatusCode.OK)]
    [InlineData("api/v1/support-tickets", HttpStatusCode.OK)]
    public async Task CustomerRead_UsesGetAndParsesEnvelope(string path, HttpStatusCode status)
    {
        var transport = new TestTransport(_ => TestTransport.Json("{\"data\":{\"items\":[]}}", status));
        var service = NewService(transport, out var client);
        using var ownedClient = client;
        using var json = await service.GetAsync(path);
        Assert.Equal(HttpMethod.Get, transport.Requests[0].Method);
        Assert.Equal("/" + path, transport.Requests[0].Path);
        Assert.Equal(JsonValueKind.Array, json.RootElement.GetProperty("data").GetProperty("items").ValueKind);
    }

    [Fact]
    public async Task Get_WhenUnauthorized_PreservesStatusCode()
    {
        var transport = new TestTransport(_ => TestTransport.Json("{}", HttpStatusCode.Unauthorized));
        var service = NewService(transport, out var client);
        using var ownedClient = client;
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => service.GetAsync("api/v1/contracts"));
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Fact]
    public async Task Get_InvalidJson_ThrowsJsonException()
    {
        var transport = new TestTransport(_ => TestTransport.Json("not-json"));
        var service = NewService(transport, out var client);
        using var ownedClient = client;
        await Assert.ThrowsAnyAsync<JsonException>(() => service.GetAsync("api/v1/reservations"));
    }

    [Theory]
    [InlineData("api/v1/reservations", "POST")]
    [InlineData("api/v1/support-tickets", "POST")]
    [InlineData("api/v1/visits/id/check-in", "POST")]
    [InlineData("api/v1/visits/id/check-out", "POST")]
    [InlineData("api/v1/inspections/id/complete", "POST")]
    [InlineData("api/v1/storage-units/id/status", "POST")]
    [InlineData("api/v1/reservations/id/handover-unit-assignment", "PUT")]
    [InlineData("api/v1/visits/id", "DELETE")]
    public async Task SendJson_UsesProvidedVerbRouteAndBody(string path, string verb)
    {
        var transport = new TestTransport(_ => TestTransport.Json("{\"data\":{}}"));
        var service = NewService(transport, out var client);
        using var ownedClient = client;
        using var response = await service.SendJsonAsync(new HttpMethod(verb), path, new { ticket = "demo" });
        Assert.Equal(path, transport.Requests[0].Path.TrimStart('/'));
        Assert.Equal(verb, transport.Requests[0].Method.Method);
        using var body = JsonDocument.Parse(transport.Requests[0].Body!);
        Assert.Equal("demo", body.RootElement.GetProperty("ticket").GetString());
        Assert.Equal("application/json", response.RootElement.ValueKind == JsonValueKind.Object ? "application/json" : "invalid");
    }

    [Fact]
    public async Task SendJson_NullBody_DoesNotSendContent()
    {
        var transport = new TestTransport(_ => TestTransport.Json());
        var service = NewService(transport, out var client);
        using var ownedClient = client;
        using var doc = await service.SendJsonAsync(HttpMethod.Post, "api/v1/visits/id/check-in");
        Assert.Null(transport.Requests[0].Body);
    }

    [Fact]
    public async Task SendJson_ServerValidationError_ThrowsWithCodeAndStatus()
    {
        var transport = new TestTransport(_ => TestTransport.Json(
            "{\"error\":{\"code\":\"VALIDATION_ERROR\"}}", HttpStatusCode.BadRequest));
        var service = NewService(transport, out var client);
        using var ownedClient = client;
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => service.SendJsonAsync(
            HttpMethod.Post, "api/v1/reservations", new { startMonth = "invalid" }));
        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Contains("VALIDATION_ERROR", ex.Message);
    }

    [Fact]
    public async Task CreateAccessVisit_BackendConflict_IsNotSwallowed()
    {
        var transport = new TestTransport(_ => TestTransport.Json("{}", HttpStatusCode.Conflict));
        var service = NewService(transport, out var client);
        using var ownedClient = client;
        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            service.CreateAccessVisitAsync(Guid.NewGuid(), DateTime.Today.AddDays(1)));
        Assert.Equal(HttpStatusCode.Conflict, ex.StatusCode);
    }

    [Fact]
    public async Task CancelledCall_PropagatesCancellation()
    {
        var transport = new TestTransport(_ => TestTransport.Json());
        var service = NewService(transport, out var client);
        using var ownedClient = client;
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.GetAsync("api/v1/contracts", cancel.Token));
        Assert.Empty(transport.Requests);
    }
}
