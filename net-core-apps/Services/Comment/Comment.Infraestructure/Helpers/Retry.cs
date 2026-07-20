using System;
using Microsoft.EntityFrameworkCore;
using Polly;
using Polly.Retry;

namespace Comment.Infra.Helpers
{
    public static class Retry
    {
        public static readonly AsyncRetryPolicy DbConcurrency = Policy
            .Handle<DbUpdateConcurrencyException>()
            .WaitAndRetryAsync(3, retryAttempt =>
                TimeSpan.FromMilliseconds(
                    (25 * Math.Pow(2, retryAttempt - 1)) + Random.Shared.Next(0, 25)));
    }
}
