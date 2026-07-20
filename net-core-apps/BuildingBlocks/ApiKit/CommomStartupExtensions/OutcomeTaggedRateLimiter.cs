using System.Threading.RateLimiting;

namespace CommomStartupExtensions;

/// <summary>
/// Preserves the finite rejection source on a failed lease so one shared ASP.NET Core
/// rejection callback can distinguish capacity load shedding from client quota enforcement.
/// </summary>
internal sealed class OutcomeTaggedRateLimiter(
    RateLimiter inner,
    string rejectionReason) : RateLimiter
{
    public override TimeSpan? IdleDuration => inner.IdleDuration;

    public override RateLimiterStatistics? GetStatistics() => inner.GetStatistics();

    protected override RateLimitLease AttemptAcquireCore(int permitCount) =>
        Tag(inner.AttemptAcquire(permitCount));

    protected override async ValueTask<RateLimitLease> AcquireAsyncCore(
        int permitCount,
        CancellationToken cancellationToken) =>
        Tag(await inner.AcquireAsync(permitCount, cancellationToken).ConfigureAwait(false));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            inner.Dispose();
        base.Dispose(disposing);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        await inner.DisposeAsync().ConfigureAwait(false);
        await base.DisposeAsyncCore().ConfigureAwait(false);
    }

    private RateLimitLease Tag(RateLimitLease lease) => lease.IsAcquired
        ? lease
        : new OutcomeTaggedLease(lease, rejectionReason);

    private sealed class OutcomeTaggedLease(
        RateLimitLease innerLease,
        string reason) : RateLimitLease
    {
        public override bool IsAcquired => innerLease.IsAcquired;

        public override IEnumerable<string> MetadataNames => innerLease.MetadataNames
            .Append(MetadataName.ReasonPhrase.Name)
            .Distinct(StringComparer.Ordinal);

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (string.Equals(
                    metadataName,
                    MetadataName.ReasonPhrase.Name,
                    StringComparison.Ordinal))
            {
                metadata = reason;
                return true;
            }

            return innerLease.TryGetMetadata(metadataName, out metadata);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                innerLease.Dispose();
            base.Dispose(disposing);
        }
    }
}
