using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace FRMS.Tests;

internal sealed record TestRequest(HttpMethod Method, string Path, string Query, string? Authorization, string? Body);

internal sealed class TestTransport(Func<TestRequest, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<TestRequest> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var entry = new TestRequest(
            request.Method, request.RequestUri!.AbsolutePath, request.RequestUri.Query,
            request.Headers.Authorization?.ToString(),
            request.Content is null ? null : await request.Content.ReadAsStringAsync(ct));
        Requests.Add(entry);
        return respond(entry);
    }

    public static HttpClient CreateClient(HttpMessageHandler handler) => new(handler)
    {
        BaseAddress = new Uri("http://localhost:5164/")
    };

    public static HttpResponseMessage Json(string content = "{}", HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    public static string Auth(string role, string token = "jwt") =>
        JsonSerializer.Serialize(new { data = new { accessToken = token, user = new { role } } });
}
