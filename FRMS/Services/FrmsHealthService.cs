using System.Net.Http;

namespace FRMS.PRN212.WpfConnection.Services;

public sealed class FrmsHealthService(HttpClient client) : IFrmsHealthService
{
    public async Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        using var response = await client.GetAsync("health", cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        // A healthy FRMS health endpoint responds with 200, usually with "Healthy".
        return new HealthCheckResult(response.IsSuccessStatusCode,
            (int)response.StatusCode, body);
    }
}
