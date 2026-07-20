using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Minio;
using Minio.DataModel.Args;
using Minio.Exceptions;

namespace StorageProvider.Providers.Minio;

/// <summary>
/// MinIO-backed implementation – used for local development.
/// </summary>
public class MinioStorageProvider : IStorageProvider
{
    private readonly IMinioClient _client;
    private readonly MinioStorageOptions _opts;

    public MinioStorageProvider(IMinioClient client, MinioStorageOptions opts)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _opts = opts ?? throw new ArgumentNullException(nameof(opts));
    }

    public async Task<StorageObjectWriteResult> PutObject(
        string fileNamespace,
        string fileName,
        Stream file,
        int? userId,
        string? fileMimeType,
        string? fileExtension,
        CancellationToken cancellationToken)
    {
        var objectName = StorageObjectKey.Build(fileNamespace, userId, fileName, fileExtension);
        var length = StorageObjectKey.GetValidatedLength(file, _opts.MaximumUploadBytes);
        var mimeType = StorageObjectKey.ValidateMimeType(fileMimeType);
        await EnsureBucketAsync(cancellationToken);
        var response = await _client.PutObjectAsync(new PutObjectArgs()
            .WithBucket(_opts.Bucket)
            .WithObject(objectName)
            .WithStreamData(file)
            .WithObjectSize(length)
            .WithContentType(mimeType), cancellationToken);
        return new(BuildPublicUrl(objectName), response.Etag ?? "not-reported");
    }

    public async Task<bool> ObjectExists(
        string fileNamespace,
        string fileName,
        int? userId,
        string? fileExtension,
        CancellationToken cancellationToken)
    {
        var objectName = StorageObjectKey.Build(fileNamespace, userId, fileName, fileExtension);
        await EnsureBucketAsync(cancellationToken);
        try
        {
            await _client.StatObjectAsync(new StatObjectArgs()
                .WithBucket(_opts.Bucket)
                .WithObject(objectName), cancellationToken);
            return true;
        }
        catch (ObjectNotFoundException)
        {
            return false;
        }
    }

    public async Task DownloadObject(
        string fileNamespace,
        string fileName,
        int? userId,
        string? fileExtension,
        Stream destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
            throw new ArgumentException("The storage download destination must be writable.", nameof(destination));
        var objectName = StorageObjectKey.Build(fileNamespace, userId, fileName, fileExtension);
        await EnsureBucketAsync(cancellationToken);
        await _client.GetObjectAsync(new GetObjectArgs()
            .WithBucket(_opts.Bucket)
            .WithObject(objectName)
            .WithCallbackStream((stream, token) =>
                stream.CopyToAsync(destination, token)),
            cancellationToken);
        if (destination.CanSeek)
            destination.Position = 0;
    }

    public async Task<StorageObjectWriteResult> CopyObject(
        string sourceNamespace,
        string sourceFileName,
        string destinationNamespace,
        string destinationFileName,
        int? userId,
        string fileMimeType,
        string? sourceExtension,
        string? destinationExtension,
        long contentLength,
        CancellationToken cancellationToken)
    {
        await using var temporary = CreateTemporaryStream();
        await DownloadObject(
            sourceNamespace,
            sourceFileName,
            userId,
            sourceExtension,
            temporary,
            cancellationToken);
        if (temporary.Length != contentLength)
            throw new InvalidDataException("The source object length changed before promotion.");
        return await PutObject(
            destinationNamespace,
            destinationFileName,
            temporary,
            userId,
            fileMimeType,
            destinationExtension,
            cancellationToken);
    }

    public async Task DeleteObject(
        string fileNamespace,
        string fileName,
        int? userId,
        string? fileExtension,
        CancellationToken cancellationToken)
    {
        var objectName = StorageObjectKey.Build(fileNamespace, userId, fileName, fileExtension);
        await EnsureBucketAsync(cancellationToken);
        await _client.RemoveObjectAsync(new RemoveObjectArgs()
            .WithBucket(_opts.Bucket)
            .WithObject(objectName), cancellationToken);
    }

    private async Task EnsureBucketAsync(CancellationToken cancellationToken)
    {
        var exists = await _client.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(_opts.Bucket),
            cancellationToken);

        if (!exists && !_opts.CreateBucket)
            throw new InvalidOperationException(
                "The configured MinIO bucket does not exist and runtime creation is disabled.");
        if (!exists)
            await _client.MakeBucketAsync(
                new MakeBucketArgs().WithBucket(_opts.Bucket),
                cancellationToken);

        if (_opts.AllowPublicRead)
        {
            await _client.SetPolicyAsync(new SetPolicyArgs()
                .WithBucket(_opts.Bucket)
                .WithPolicy(BuildPublicReadPolicy(_opts.Bucket)), cancellationToken);
        }
    }

    private string BuildPublicUrl(string objectName) =>
        $"{_opts.PublicUrl?.TrimEnd('/')}/{_opts.Bucket}/{objectName}";

    private static FileStream CreateTemporaryStream() => new(
        Path.Combine(Path.GetTempPath(), $"inspacy-storage-{Guid.NewGuid():N}.tmp"),
        FileMode.CreateNew,
        FileAccess.ReadWrite,
        FileShare.None,
        64 * 1024,
        FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);

    private static string BuildPublicReadPolicy(string bucket) =>
        $@"{{
          ""Version"": ""2012-10-17"",
          ""Statement"": [
            {{
              ""Effect"":    ""Allow"",
              ""Principal"": {{ ""AWS"": [""*""] }},
              ""Action"":    [""s3:GetObject""],
              ""Resource"":  [""arn:aws:s3:::{bucket}/*""]
            }}
          ]
        }}";
}
