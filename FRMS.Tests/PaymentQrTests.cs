using System.Net;
using System.Net.Http;
using System.Text.Json;
using FRMS.PRN212.WpfConnection.Services;
using FRMS.PRN212.WpfConnection.ViewModels;
using Xunit;

namespace FRMS.Tests;

public sealed class PaymentQrTests
{
    private static readonly Guid InvoiceId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid PaymentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string Checkout = "https://pay.payos.vn/web/test-session";
    private const string Payload = "0002010102126304BE36";

    [Fact]
    public async Task UncertainStart_ReusesKeyAndUsesBackendStatus()
    {
        var keys = new List<Guid>();
        using var http = TestTransport.CreateClient(new PaymentHandler(request =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/auth/customer/login"))
                return TestTransport.Json(TestTransport.Auth("CUSTOMER"));
            Assert.Equal("Bearer jwt", request.Headers.Authorization?.ToString());
            if (request.Method == HttpMethod.Post)
            {
                Assert.Equal($"/api/v1/invoices/{InvoiceId:D}/payments/payos", request.RequestUri.AbsolutePath);
                Assert.Null(request.Content);
                keys.Add(Guid.Parse(Assert.Single(request.Headers.GetValues("Idempotency-Key"))));
                if (keys.Count == 1) throw new HttpRequestException("Connection interrupted");
                return Session("PENDING", Payload, DateTimeOffset.UtcNow.AddMinutes(5));
            }
            Assert.Equal($"/api/v1/payments/{PaymentId:D}", request.RequestUri.AbsolutePath);
            return Session("SUCCESS");
        }));
        var api = new CustomerApiService(http);
        Assert.True((await api.LoginAsync("0900000000", "fixture")).Success);
        var payment = new PaymentQrViewModel(api, InvoiceId);

        await payment.StartAsync(default);
        Assert.True(payment.CanStart);
        await payment.StartAsync(default);
        Assert.NotEqual(Guid.Empty, keys[0]);
        Assert.Equal(keys[0], keys[1]);
        Assert.Equal(Payload, payment.QrCode);
        Assert.Equal(Checkout, payment.CheckoutUri!.AbsoluteUri);
        Assert.False(payment.Succeeded);

        await payment.CheckAsync(default);
        Assert.True(payment.Succeeded);
        Assert.Null(payment.QrCode);
        Assert.False(payment.CanPoll);
    }

    [Fact]
    public async Task OldBackend_CheckoutWorksWithoutQrOrExpiry()
    {
        using var http = TestTransport.CreateClient(new PaymentHandler(_ => Session("PENDING")));
        var payment = new PaymentQrViewModel(new CustomerApiService(http), InvoiceId);
        await payment.StartAsync(default);

        Assert.Null(payment.QrCode);
        Assert.Null(payment.ExpiresAt);
        Assert.Equal(Checkout, payment.CheckoutUri!.AbsoluteUri);
        Assert.True(payment.CanPoll);
    }

    [Fact]
    public async Task ExpiredQr_IsHiddenWhileStatusRemainsCheckable()
    {
        using var http = TestTransport.CreateClient(new PaymentHandler(_ =>
            Session("PENDING", Payload, DateTimeOffset.UtcNow.AddSeconds(-1))));
        var payment = new PaymentQrViewModel(new CustomerApiService(http), InvoiceId);
        await payment.StartAsync(default);

        Assert.True(payment.IsExpired);
        Assert.True(payment.CanCheck);
        Assert.False(payment.CanPoll);
        Assert.False(payment.Succeeded);
        Assert.Null(payment.QrCode);
        Assert.Null(payment.CheckoutUri);
    }

    [Fact]
    public async Task RejectedStatusQuery_StopsPollingAndHidesCheckout()
    {
        using var http = TestTransport.CreateClient(new PaymentHandler(request => request.Method == HttpMethod.Post
            ? Session("PENDING", Payload)
            : TestTransport.Json(status: HttpStatusCode.Unauthorized)));
        var payment = new PaymentQrViewModel(new CustomerApiService(http), InvoiceId);
        await payment.StartAsync(default);
        await payment.CheckAsync(default);

        Assert.False(payment.CanPoll);
        Assert.False(payment.Succeeded);
        Assert.Null(payment.CheckoutUri);
        Assert.Contains("Sign in", payment.Message);
    }

    private static HttpResponseMessage Session(string status, string? qrCode = null, DateTimeOffset? expiresAt = null) =>
        TestTransport.Json(JsonSerializer.Serialize(new
        {
            data = new { paymentId = PaymentId, invoiceId = InvoiceId, amount = 150000,
                paymentMethod = "PAYOS", status, paymentUrl = Checkout, qrCode, expiresAt }
        }, new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull }));

    private sealed class PaymentHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(respond(request));
        }
    }
}
