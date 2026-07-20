using Comment.Application.Providers.Storage;
using StorageProvider;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace Comment.Infra.Providers.Storage
{
    public sealed class Storage(
        IStorageProviderService storage,
        StorageConfiguration configuration) : ICommentMediaObjectStore
    {
        public async Task<CommentMediaObjectWriteResult> PutQuarantineAsync(
            int ownerUserId,
            string objectName,
            string contentType,
            Stream content,
            CancellationToken cancellationToken)
        {
            var result = await storage.PutObject(
                configuration.CommentQuarantine,
                objectName,
                content,
                ownerUserId,
                contentType,
                Path.GetExtension(objectName),
                cancellationToken);
            return new(result.Url, result.ObjectVersion);
        }

        public Task<bool> QuarantineExistsAsync(
            int ownerUserId,
            string objectName,
            CancellationToken cancellationToken) =>
            storage.ObjectExists(
                configuration.CommentQuarantine,
                objectName,
                ownerUserId,
                Path.GetExtension(objectName),
                cancellationToken);

        public async Task<Stream> OpenQuarantineReadAsync(
            int ownerUserId,
            string objectName,
            CancellationToken cancellationToken) =>
            await OpenReadAsync(
                configuration.CommentQuarantine,
                "quarantine",
                ownerUserId,
                objectName,
                cancellationToken);

        public async Task<Stream> OpenFinalReadAsync(
            int ownerUserId,
            string objectName,
            CancellationToken cancellationToken) =>
            await OpenReadAsync(
                configuration.Comments,
                "final",
                ownerUserId,
                objectName,
                cancellationToken);

        public async Task<CommentMediaObjectWriteResult> PromoteAsync(
            int ownerUserId,
            string quarantineObjectName,
            string finalObjectName,
            string contentType,
            long sizeBytes,
            CancellationToken cancellationToken)
        {
            var result = await storage.CopyObject(
                configuration.CommentQuarantine,
                quarantineObjectName,
                configuration.Comments,
                finalObjectName,
                ownerUserId,
                contentType,
                Path.GetExtension(quarantineObjectName),
                Path.GetExtension(finalObjectName),
                sizeBytes,
                cancellationToken);
            // The provider URL may be a permanent public CDN URL. Never pass it
            // into the Comment aggregate; the API serves this private object only
            // after applying the current comment audience policy.
            return new(finalObjectName, result.ObjectVersion);
        }

        public Task DeleteQuarantineAsync(
            int ownerUserId,
            string objectName,
            CancellationToken cancellationToken) =>
            storage.DeleteObject(
                configuration.CommentQuarantine,
                objectName,
                ownerUserId,
                Path.GetExtension(objectName),
                cancellationToken);

        public Task DeleteFinalAsync(
            int ownerUserId,
            string objectName,
            CancellationToken cancellationToken) =>
            storage.DeleteObject(
                configuration.Comments,
                objectName,
                ownerUserId,
                Path.GetExtension(objectName),
                cancellationToken);

        private async Task<Stream> OpenReadAsync(
            string fileNamespace,
            string purpose,
            int ownerUserId,
            string objectName,
            CancellationToken cancellationToken)
        {
            var temporary = new FileStream(
                Path.Combine(
                    Path.GetTempPath(),
                    $"comment-{purpose}-{Guid.NewGuid():N}.tmp"),
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);
            try
            {
                await storage.DownloadObject(
                    fileNamespace,
                    objectName,
                    ownerUserId,
                    Path.GetExtension(objectName),
                    temporary,
                    cancellationToken);
                temporary.Position = 0;
                return temporary;
            }
            catch
            {
                await temporary.DisposeAsync();
                throw;
            }
        }
    }
}
