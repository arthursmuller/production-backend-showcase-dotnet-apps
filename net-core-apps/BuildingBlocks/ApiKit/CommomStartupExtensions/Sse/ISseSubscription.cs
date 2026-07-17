using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CommomStartupExtensions.Sse
{
    /// <summary>One live SSE subscriber. Dispose to release queue/refs.</summary>
    public interface ISseSubscription : IDisposable
    {
        /// <summary>Async stream of payloads for this subscription. Completes when cancelled or disposed.</summary>
        IAsyncEnumerable<string> Reader(CancellationToken ct);
    }
}
