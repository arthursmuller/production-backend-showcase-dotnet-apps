using Feed.Domain.Exceptions;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Feed.Application.Features.Privacy;

public static class FeedUserMutationExtensions
{
    public static async Task<IFeedUserMutationScope> RequireActiveUsersAsync(
        this IFeedUserErasureAuthority authority,
        IEnumerable<int> userIds,
        CancellationToken cancellationToken)
    {
        var normalized = (userIds ?? []).Where(value => value > 0).Distinct().ToArray();
        if (normalized.Length == 0)
            return NoopScope.Instance;

        var scope = await authority.AcquireMutationAsync(normalized, cancellationToken);
        if (scope.Allowed)
            return scope;

        await scope.CommitAsync(cancellationToken);
        await scope.DisposeAsync();
        throw new FeedException("The Feed mutation subject is no longer available.", 410);
    }

    private sealed class NoopScope : IFeedUserMutationScope
    {
        public static NoopScope Instance { get; } = new();
        public bool Allowed => true;
        public IReadOnlySet<int> ErasedUserIds { get; } = new HashSet<int>();
        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
