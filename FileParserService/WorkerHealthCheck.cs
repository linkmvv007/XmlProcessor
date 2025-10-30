using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace FileParserService;


public class WorkerHealthCheck : IHealthCheck
{
    private readonly Worker _service;
    private readonly ILogger<WorkerHealthCheck> _logger;

    public WorkerHealthCheck(ILogger<WorkerHealthCheck> logger, Worker service)
    {
        _logger = logger;
        _service = service;
    }
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (!_service.IsHealthy)
        {
            return Task.FromResult(
                HealthCheckResult.Unhealthy("Worker host hasn't run successfully in 10+ minutes")
                );
        }

        return Task.FromResult(
            HealthCheckResult.Healthy("Worker host running normally")
            );
    }
}

/// <summary>
/// Extension methods for registering BackgroundService health checks.
/// Integrates with ASP.NET Core health check middleware for monitoring dashboards.
/// </summary>
public static class HealthCheckExtensions
{
    /// <summary>
    /// Adds a health check specifically for the WorkerHealthCheck.
    /// Provides detailed health status with metrics.
    /// </summary>
    public static IServiceCollection AddWorkerHealthCheck(
        this IServiceCollection services,
        string name = "background-service",
        HealthStatus? failureStatus = null,
        IEnumerable<string>? tags = null,
        TimeSpan? timeout = null)
    {
        return services.AddHealthChecks()
            .AddCheck<WorkerHealthCheck>(
                name,
                failureStatus,
                tags ?? new[] { "background-service", "ready" },
                timeout)
            .Services;
    }
}