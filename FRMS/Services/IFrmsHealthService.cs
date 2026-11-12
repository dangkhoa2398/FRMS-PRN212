namespace FRMS.PRN212.WpfConnection.Services;

public interface IFrmsHealthService
{
    Task<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = default);
}

public sealed record HealthCheckResult(bool IsHealthy, int StatusCode, string Body);
