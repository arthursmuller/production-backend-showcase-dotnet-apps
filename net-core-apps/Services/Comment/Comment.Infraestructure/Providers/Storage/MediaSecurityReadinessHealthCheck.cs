using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using InspacyApiHost;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Infra.Providers.Storage
{
    public sealed class MediaSecurityReadinessHealthCheck(
        MediaSecurityOptions options,
        ClamAvResiliencePolicy resilience,
        IConfiguration configuration) : IHealthCheck
    {
        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            var role = RuntimeIdentity.Resolve(configuration);
            if (!RunsMediaWorkflow(role))
            {
                return HealthCheckResult.Healthy(
                    "Media scanning is not owned by this runtime role.");
            }
            if (!options.RequireMalwareScan)
            {
                return HealthCheckResult.Healthy(
                    "Media malware scanning is disabled for this development runtime.");
            }

            try
            {
                await resilience.ProbeAsync(cancellationToken);
                return HealthCheckResult.Healthy(
                    "Media scanning dependency is available.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Readiness is externally visible. Do not attach endpoint, socket, engine,
                // or exception data to the result.
                return HealthCheckResult.Unhealthy(
                    "Media scanning dependency is unavailable.");
            }
        }

        internal static bool RunsMediaWorkflow(string role) =>
            RunsMediaWorkflow(RuntimeIdentity.Parse(role));

        internal static bool RunsMediaWorkflow(ApiRuntimeRole role) =>
            role.ExposesBusinessRoutes() || role.RunsJobs();
    }
}
