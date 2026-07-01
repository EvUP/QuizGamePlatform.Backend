using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace testBdControllers.DataAccess.HealthChecks
{
    public class DatabaseHealthCheck(ApplicationDbContext dbContext) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);

            return canConnect
                ? HealthCheckResult.Healthy("База данных доступна")
                : HealthCheckResult.Unhealthy("База данных недоступна");
        }
    }
}
