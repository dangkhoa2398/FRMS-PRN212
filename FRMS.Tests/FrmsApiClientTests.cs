using System.Net;
using System.Net.Http;
using System.Text.Json;
using Frms.Wpf.Integration;
using Xunit;

namespace FRMS.Tests;

public sealed class FrmsApiClientTests
{
    [Theory]
    [InlineData("")]
    [InlineData("localhost:5164")]
    [InlineData("ftp://example.test")]
    [InlineData("file:///tmp/api")]
    public void InvalidApiUrl_IsRejected(string url)
    {
        Assert.Throws<ArgumentException>(() => new FrmsApiClient(url));
    }

    [Fact]
    public void HttpClientWithoutBaseAddress_IsRejected()
    {
        using var http = new HttpClient(new TestTransport(_ => TestTransport.Json()));
        Assert.Throws<ArgumentException>(() => new FrmsApiClient(http));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public async Task Health_MapsStatusCorrectly(HttpStatusCode code, bool healthy)
    {
        var transport = new TestTransport(_ => TestTransport.Json("{}", code));
        using var http = TestTransport.CreateClient(transport);
        using var api = new FrmsApiClient(http);
        Assert.Equal(healthy, await api.CheckHealthAsync());
        Assert.Equal("/health", transport.Requests.Single().Path);
    }

    [Theory]
    [InlineData(1, 20)]
    [InlineData(2, 100)]
    public async Task Facilities_UsesPagination(int page, int size)
    {
        var transport = new TestTransport(_ => TestTransport.Json("{\"data\":[]}"));
        using var http = TestTransport.CreateClient(transport);
        using var api = new FrmsApiClient(http);
        using var response = await api.GetFacilitiesAsync(page, size);
        Assert.Equal($"/api/v1/facilities?page={page}&pageSize={size}",
            transport.Requests.Single().Path + transport.Requests.Single().Query);
        Assert.Equal(JsonValueKind.Array, response.RootElement.GetProperty("data").ValueKind);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    [InlineData(1, 0)]
    [InlineData(1, -3)]
    public async Task Facilities_InvalidPagination_RejectsWithoutHttp(int page, int size)
    {
        var transport = new TestTransport(_ => TestTransport.Json());
        using var http = TestTransport.CreateClient(transport);
        using var api = new FrmsApiClient(http);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => api.GetFacilitiesAsync(page, size));
        Assert.Empty(transport.Requests);
    }

    [Fact]
    public async Task BearerToken_CanBeAppliedReplacedAndRemoved()
    {
        var transport = new TestTransport(_ => TestTransport.Json());
        using var http = TestTransport.CreateClient(transport);
        using var api = new FrmsApiClient(http);
        api.SetBearerToken("first");
        using (var json = await api.GetJsonAsync("api/v1/contracts")) { }
        api.SetBearerToken("second");
        using (var json = await api.GetJsonAsync("api/v1/contracts")) { }
        api.SetBearerToken(null);
        using (var json = await api.GetJsonAsync("api/v1/contracts")) { }
        Assert.Equal(new[] { "Bearer first", "Bearer second", null },
            transport.Requests.Select(x => x.Authorization).ToArray());
    }

    [Fact]
    public async Task GetJson_ThrowsOnBadStatusAndBadPayload()
    {
        var count = 0;
        var transport = new TestTransport(_ => ++count == 1
            ? TestTransport.Json("{}", HttpStatusCode.Forbidden)
            : TestTransport.Json("broken"));
        using var http = TestTransport.CreateClient(transport);
        using var api = new FrmsApiClient(http);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => api.GetJsonAsync("api/v1/contracts"));
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        await Assert.ThrowsAnyAsync<JsonException>(() => api.GetJsonAsync("api/v1/contracts"));
    }

    [Fact]
    public async Task InjectedHttpClient_RemainsOwnedByCallerAfterApiDispose()
    {
        var transport = new TestTransport(_ => TestTransport.Json());
        using var http = TestTransport.CreateClient(transport);
        using (var api = new FrmsApiClient(http))
        {
            using var doc = await api.GetJsonAsync("health");
        }
        using var response = await http.GetAsync("health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task CancelledRequest_PropagatesCancellation()
    {
        var transport = new TestTransport(_ => TestTransport.Json());
        using var http = TestTransport.CreateClient(transport);
        using var api = new FrmsApiClient(http);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => api.GetJsonAsync("health", cts.Token));
        Assert.Empty(transport.Requests);
    }
}
