using Comment.Domain.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Application.Features.Privacy;

public static class CommentUserMutationExtensions
{
    public static async Task<ICommentUserMutationScope> RequireActiveUsersAsync(
        this ICommentUserErasureAuthority authority,
        IEnumerable<int> userIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authority);
        var normalized = (userIds ?? Array.Empty<int>())
            .Where(userId => userId > 0)
            .Distinct()
            .ToArray();
        if (normalized.Length == 0)
            throw new CommentException("At least one active user is required.");

        var scope = await authority.AcquireMutationAsync(
            normalized,
            cancellationToken);
        if (scope.Allowed)
            return scope;

        await scope.DisposeAsync();
        throw new CommentException(
            "The requested Comment mutation is not available.",
            403);
    }
}
