using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace InspacyApiHost;

/// <summary>
/// Process-local, monotonic drain state. Once shutdown begins the process remains unready for
/// the rest of its lifetime; a new secret/configuration snapshot requires a new process.
/// </summary>
internal sealed class RuntimeDrainState : IHealthCheck
{
    private int _draining;

    public bool IsDraining => Volatile.Read(ref _draining) != 0;

    public void BeginDrain() => Interlocked.Exchange(ref _draining, 1);

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(IsDraining
            ? HealthCheckResult.Unhealthy("The runtime is draining and is not accepting new work.")
            : HealthCheckResult.Healthy("The runtime is accepting work."));
    }
}

/// <summary>
/// Connects the generic-host stopping signal to readiness before application hosted services
/// are asked to stop. BackgroundService and MassTransit then receive their normal cancellation
/// and drain signals from the host lifecycle.
/// </summary>
internal sealed class RuntimeDrainLifecycleService(
    IHostApplicationLifetime lifetime,
    RuntimeDrainState state) : IHostedService, IDisposable
{
    private IDisposable? _stoppingRegistration;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _stoppingRegistration = lifetime.ApplicationStopping.Register(
            static value =>
            {
                if (value is RuntimeDrainState drainState)
                    drainState.BeginDrain();
            },
            state);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        state.BeginDrain();
        return Task.CompletedTask;
    }

    public void Dispose() => _stoppingRegistration?.Dispose();
}
