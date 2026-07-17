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
            .WaitAndRetryAsync(50, retryAttempt =>
                TimeSpan.FromMilliseconds(TimeSpan.FromSeconds(2).TotalMilliseconds * Math.Pow(2, retryAttempt - 1)));
    }
}
