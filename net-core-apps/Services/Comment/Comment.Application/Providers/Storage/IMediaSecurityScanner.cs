using StarterEntities.Storage;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Application.Providers.Storage
{
    public sealed record VerifiedMediaContent(
        string FileName,
        string MimeType,
        string Extension,
        long SizeBytes,
        string Sha256,
        string Scanner,
        string ScannerVersion,
        string SignatureVersion,
        System.DateTime ScannedAtUtc);

    /// <summary>
    /// Fail-closed media gate. Implementations must consume the complete stream,
    /// verify its declared length/type, run the configured malware scanner, and
    /// rewind it before returning.
    /// </summary>
    public interface IMediaSecurityScanner
    {
        Task<VerifiedMediaContent> VerifyAsync(
            IncomingMediaFile file,
            Stream content,
            CancellationToken cancellationToken);
    }
}
