using System.Net;
using System.Net.Http;
using FRMS.PRN212.WpfConnection.Services;
using Xunit;

namespace FRMS.Tests;

public sealed class FrmsHealthServiceTests
{
    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.NoContent, true)]
    [InlineData(HttpStatusCode.BadGateway, false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    public async Task Health_ReturnsBodyStatusAndHealthyFlag(HttpStatusCode code, bool expected)
    {
        var transport = new TestTransport(_ => TestTransport.Json("Healthy", code));
        using var client = TestTransport.CreateClient(transport);
        var service = new FrmsHealthService(client);
        var result = await service.CheckAsync();
        Assert.Equal(expected, result.IsHealthy);
        Assert.Equal((int)code, result.StatusCode);
        Assert.Equal("Healthy", result.Body);
        Assert.Equal("/health", transport.Requests[0].Path);
    }

    [Fact]
    public async Task Health_PropagatesTransportErrors()
    {
        var transport = new TestTransport(_ => throw new HttpRequestException("offline"));
        using var client = TestTransport.CreateClient(transport);
        await Assert.ThrowsAsync<HttpRequestException>(() => new FrmsHealthService(client).CheckAsync());
    }

    [Fact]
    public async Task Health_PropagatesCancellation()
    {
        var transport = new TestTransport(_ => TestTransport.Json());
        using var client = TestTransport.CreateClient(transport);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new FrmsHealthService(client).CheckAsync(cts.Token));
    }
}
