using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Infra.Providers.Storage
{
    public enum MediaScanDependencyFailure
    {
        Saturated,
        CircuitOpen,
        Timeout,
        Unavailable,
    }

    public sealed class MediaScanDependencyUnavailableException : IOException
    {
        public MediaScanDependencyUnavailableException(
            MediaScanDependencyFailure reason,
            Exception? innerException = null)
            : base("The media scanning dependency is unavailable.", innerException)
        {
            Reason = reason;
        }

        public MediaScanDependencyFailure Reason { get; }
    }

    /// <summary>
    /// Per-process admission and dependency-failure authority for ClamAV. Admission is
    /// deliberately zero-wait: request threads are never accumulated in an in-memory queue.
    /// Breaker transitions are generation-fenced so completions from calls admitted before
    /// an open/half-open transition cannot accidentally close a newer breaker generation.
    /// </summary>
    public sealed class ClamAvResiliencePolicy
    {
        private readonly MediaSecurityOptions options;
        private readonly IClamAvClient client;
        private readonly TimeProvider timeProvider;
        private readonly SemaphoreSlim concurrency;
        private readonly SemaphoreSlim readinessConcurrency = new(1, 1);
        private readonly object breakerLock = new();
        private CircuitState state;
        private int consecutiveFailures;
        private long generation;
        private DateTimeOffset openedAtUtc;

        public ClamAvResiliencePolicy(
            MediaSecurityOptions options,
            IClamAvClient client,
            TimeProvider? timeProvider = null)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(client);
            this.options = options;
            this.client = client;
            this.timeProvider = timeProvider ?? TimeProvider.System;
            concurrency = new SemaphoreSlim(
                options.MaximumConcurrentScansPerReplica,
                options.MaximumConcurrentScansPerReplica);
        }

        public Task<MalwareScanResult> ScanAsync(
            Stream content,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(content);
            return ExecuteAsync(
                token => client.ScanAsync(content, token),
                concurrency,
                cancellationToken);
        }

        public Task ProbeAsync(CancellationToken cancellationToken) =>
            ExecuteAsync(async token =>
            {
                await client.ProbeAsync(token);
                return true;
            }, readinessConcurrency, cancellationToken);

        private async Task<TResult> ExecuteAsync<TResult>(
            Func<CancellationToken, Task<TResult>> action,
            SemaphoreSlim admission,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!admission.Wait(0))
            {
                throw new MediaScanDependencyUnavailableException(
                    MediaScanDependencyFailure.Saturated);
            }

            BreakerLease lease = default;
            var enteredBreaker = false;
            try
            {
                lease = EnterBreaker();
                enteredBreaker = true;
                var result = await action(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                RecordSuccess(lease);
                return result;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                if (enteredBreaker)
                    RecordCallerCancellation(lease);
                throw;
            }
            catch (MediaScanDependencyUnavailableException)
            {
                throw;
            }
            catch (OperationCanceledException error)
            {
                if (enteredBreaker)
                    RecordFailure(lease);
                throw new MediaScanDependencyUnavailableException(
                    MediaScanDependencyFailure.Timeout,
                    error);
            }
            catch (Exception error)
            {
                if (enteredBreaker)
                    RecordFailure(lease);
                throw new MediaScanDependencyUnavailableException(
                    MediaScanDependencyFailure.Unavailable,
                    error);
            }
            finally
            {
                admission.Release();
            }
        }

        private BreakerLease EnterBreaker()
        {
            lock (breakerLock)
            {
                if (state == CircuitState.Closed)
                    return new(generation, IsHalfOpen: false, openedAtUtc);

                var now = timeProvider.GetUtcNow();
                if (state == CircuitState.Open
                    && now - openedAtUtc >= TimeSpan.FromSeconds(
                        options.CircuitBreakerOpenSeconds))
                {
                    state = CircuitState.HalfOpen;
                    generation++;
                    return new(generation, IsHalfOpen: true, openedAtUtc);
                }

                throw new MediaScanDependencyUnavailableException(
                    MediaScanDependencyFailure.CircuitOpen);
            }
        }

        private void RecordSuccess(BreakerLease lease)
        {
            lock (breakerLock)
            {
                if (lease.Generation != generation)
                    return;
                if (lease.IsHalfOpen && state != CircuitState.HalfOpen)
                    return;
                if (!lease.IsHalfOpen && state != CircuitState.Closed)
                    return;

                consecutiveFailures = 0;
                if (state == CircuitState.HalfOpen)
                {
                    state = CircuitState.Closed;
                    generation++;
                }
            }
        }

        private void RecordFailure(BreakerLease lease)
        {
            lock (breakerLock)
            {
                if (lease.Generation != generation)
                    return;

                if (lease.IsHalfOpen && state == CircuitState.HalfOpen)
                {
                    OpenCircuit(timeProvider.GetUtcNow());
                    return;
                }
                if (state != CircuitState.Closed)
                    return;

                consecutiveFailures++;
                if (consecutiveFailures >= options.CircuitBreakerFailureThreshold)
                    OpenCircuit(timeProvider.GetUtcNow());
            }
        }

        private void RecordCallerCancellation(BreakerLease lease)
        {
            lock (breakerLock)
            {
                if (!lease.IsHalfOpen
                    || lease.Generation != generation
                    || state != CircuitState.HalfOpen)
                    return;

                // A caller disconnect is not dependency evidence. Return the single probe
                // permit without extending the existing open interval, allowing another
                // controlled half-open attempt immediately.
                state = CircuitState.Open;
                openedAtUtc = lease.OpenedAtUtc;
                generation++;
            }
        }

        private void OpenCircuit(DateTimeOffset now)
        {
            state = CircuitState.Open;
            openedAtUtc = now;
            consecutiveFailures = options.CircuitBreakerFailureThreshold;
            generation++;
        }

        private enum CircuitState
        {
            Closed,
            Open,
            HalfOpen,
        }

        private readonly record struct BreakerLease(
            long Generation,
            bool IsHalfOpen,
            DateTimeOffset OpenedAtUtc);
    }
}
