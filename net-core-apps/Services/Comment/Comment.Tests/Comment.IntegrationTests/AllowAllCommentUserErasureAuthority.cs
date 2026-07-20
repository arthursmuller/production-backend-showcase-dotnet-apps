using Comment.Application.Features.Privacy;

namespace Comment.UnitTests;

internal sealed class AllowAllCommentUserErasureAuthority : ICommentUserErasureAuthority
{
    public static AllowAllCommentUserErasureAuthority Instance { get; } = new();

    public Task<ICommentUserMutationScope> AcquireMutationAsync(
        IReadOnlyCollection<int> userIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<ICommentUserMutationScope>(new AllowAllCommentUserMutationScope());

    public Task<bool> IsErasedAsync(int userId, CancellationToken cancellationToken) =>
        Task.FromResult(false);

    public Task<IReadOnlySet<int>> GetErasedAsync(
        IReadOnlyCollection<int> userIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());

    private sealed class AllowAllCommentUserMutationScope : ICommentUserMutationScope
    {
        public bool Allowed => true;

        public Task CommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
