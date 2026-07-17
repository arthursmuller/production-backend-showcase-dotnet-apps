using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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
    private readonly ILogger<MinioStorageProvider> _logger;

    public MinioStorageProvider(MinioStorageOptions opts, ILogger<MinioStorageProvider> logger)
    {
        _opts = opts;
        _logger = logger;

        _client = new MinioClient()
            .WithEndpoint(opts.Endpoint)
            .WithCredentials(opts.AccessKey, opts.SecretKey)
            .WithSSL(opts.UseSSL)
            .Build();
    }

    public async Task<DownloadDto?> Get(string endpoint)
    {
        var objectName = ResolveObjectName(endpoint);
        if (string.IsNullOrEmpty(objectName)) return null;

        var output = new MemoryStream();

        var stat = await _client.StatObjectAsync(new StatObjectArgs()
            .WithBucket(_opts.BucketName)
            .WithObject(objectName));

        await _client.GetObjectAsync(new GetObjectArgs()
            .WithBucket(_opts.BucketName)
            .WithObject(objectName)
            .WithCallbackStream(s => s.CopyTo(output)));

        var segments = objectName.Split('/');
        var fileName = segments[^1];
        var ns = segments.Length > 0 ? segments[0] : string.Empty;
        var extension = Path.GetExtension(fileName).TrimStart('.');

        return new DownloadDto(ns, fileName, stat.ContentType, extension, output.ToArray());
    }

    public async Task<string> UploadStream(
        string fileNamespace,
        string fileName,
        Stream file,
        int? userId,
        string? fileMimeType,
        string? fileExtension)
    {
        await EnsureBucketAsync();

        var objectName = BuildObjectName(fileNamespace, userId, fileName, fileExtension);
        var stream = file ?? Stream.Null;

        if (!stream.CanSeek)
        {
            var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            buffer.Position = 0;
            stream = buffer;
        }

        await _client.PutObjectAsync(new PutObjectArgs()
            .WithBucket(_opts.BucketName)
            .WithObject(objectName)
            .WithStreamData(stream)
            .WithObjectSize(stream.Length)
            .WithContentType(fileMimeType ?? "application/octet-stream"));

        _logger.LogInformation("MinIO stream upload: {Object}", objectName);
        return BuildPublicUrl(objectName);
    }

    public async Task<bool> Delete(string endpoint)
    {
        try
        {
            var objectName = ResolveObjectName(endpoint);
            if (string.IsNullOrEmpty(objectName)) return false;

            await _client.RemoveObjectAsync(new RemoveObjectArgs()
                .WithBucket(_opts.BucketName)
                .WithObject(objectName));

            _logger.LogInformation("MinIO delete: {Object}", objectName);
            return true;
        }
        catch (MinioException ex)
        {
            _logger.LogError(ex, "MinIO delete failed – endpoint:{Endpoint}", endpoint);
            return false;
        }
    }

    private async Task EnsureBucketAsync()
    {
        var exists = await _client.BucketExistsAsync(
            new BucketExistsArgs().WithBucket(_opts.BucketName));

        if (!exists)
            await _client.MakeBucketAsync(
                new MakeBucketArgs().WithBucket(_opts.BucketName));

        await _client.SetPolicyAsync(new SetPolicyArgs()
            .WithBucket(_opts.BucketName)
            .WithPolicy(BuildPublicReadPolicy(_opts.BucketName)));
    }

    private static string BuildObjectName(
        string ns,
        int? userId,
        string fileName,
        string? extension)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
        var ext = !string.IsNullOrWhiteSpace(extension)
            ? extension!.TrimStart('.')
            : Path.GetExtension(fileName ?? string.Empty).TrimStart('.');

        var name = string.IsNullOrWhiteSpace(ext) ? baseName : $"{baseName}.{ext}";

        return string.Join("/", new[] { ns, userId?.ToString(), name }
            .Where(s => !string.IsNullOrWhiteSpace(s))!);
    }

    private string ResolveObjectName(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return endpoint;

        var prefix = $"{_opts.PublicBaseUrl?.TrimEnd('/')}/{_opts.BucketName}/";
        return endpoint.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? endpoint[prefix.Length..]
            : endpoint;
    }

    private string BuildPublicUrl(string objectName) =>
        $"{_opts.PublicBaseUrl?.TrimEnd('/')}/{_opts.BucketName}/{objectName}";

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
