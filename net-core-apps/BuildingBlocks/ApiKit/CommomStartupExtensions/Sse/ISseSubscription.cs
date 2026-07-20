using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CommomStartupExtensions.Sse
{
    /// <summary>
    /// One live SSE subscriber. Disposing it, cancelling its subscription token,
    /// or ending its reader releases the bounded queue and registry entry.
    /// </summary>
    public interface ISseSubscription : IDisposable
    {
        /// <summary>Async stream of payloads for this subscription. Completes without surfacing cancellation when cancelled or disposed.</summary>
        IAsyncEnumerable<string> Reader(CancellationToken ct);
    }
}
