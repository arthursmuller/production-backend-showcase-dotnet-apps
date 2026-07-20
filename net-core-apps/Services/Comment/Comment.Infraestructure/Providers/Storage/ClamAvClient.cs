using System.Buffers;
using System.Buffers.Binary;
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Comment.Infra.Providers.Storage
{
    public enum MalwareScanVerdict
    {
        Clean,
        Infected,
    }

    public sealed record MalwareScanResult(
        MalwareScanVerdict Verdict,
        string Scanner,
        string EngineVersion,
        string SignatureVersion);

    public interface IClamAvClient
    {
        Task<MalwareScanResult> ScanAsync(
            Stream content,
            CancellationToken cancellationToken);

        Task ProbeAsync(CancellationToken cancellationToken);
    }

    public sealed class ClamAvClient(MediaSecurityOptions options) : IClamAvClient
    {
        private static readonly byte[] InStreamCommand = Encoding.ASCII.GetBytes("zINSTREAM\0");
        private static readonly byte[] VersionCommand = Encoding.ASCII.GetBytes("zVERSION\0");
        private const int MaximumResponseBytes = 4096;

        public async Task<MalwareScanResult> ScanAsync(
            Stream content,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(content);
            if (!content.CanSeek)
                throw new InvalidOperationException("The media scan stream must be seekable.");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.ScanTimeoutSeconds));

            var (engineVersion, signatureVersion) = await ReadVersionAsync(timeout.Token);
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
            connectTimeout.CancelAfter(TimeSpan.FromSeconds(options.ConnectTimeoutSeconds));
            using var client = new TcpClient();
            await client.ConnectAsync(options.ClamAvHost, options.ClamAvPort, connectTimeout.Token);
            await using var network = client.GetStream();
            await network.WriteAsync(InStreamCommand, timeout.Token);

            var buffer = ArrayPool<byte>.Shared.Rent(options.ChunkBytes);
            var prefix = new byte[sizeof(uint)];
            try
            {
                content.Position = 0;
                while (true)
                {
                    var read = await content.ReadAsync(
                        buffer.AsMemory(0, options.ChunkBytes),
                        timeout.Token);
                    if (read == 0)
                        break;
                    BinaryPrimitives.WriteUInt32BigEndian(prefix, checked((uint)read));
                    await network.WriteAsync(prefix, timeout.Token);
                    await network.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                }

                Array.Clear(prefix);
                await network.WriteAsync(prefix, timeout.Token);
                await network.FlushAsync(timeout.Token);

                var response = await ReadResponseAsync(network, timeout.Token);
                if (response.EndsWith(" OK", StringComparison.Ordinal))
                    return new(
                        MalwareScanVerdict.Clean,
                        "ClamAV",
                        engineVersion,
                        signatureVersion);
                if (response.Contains(" FOUND", StringComparison.Ordinal))
                    return new(
                        MalwareScanVerdict.Infected,
                        "ClamAV",
                        engineVersion,
                        signatureVersion);
                throw new InvalidOperationException("The malware scanner returned an invalid response.");
            }
            finally
            {
                content.Position = 0;
                ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
            }
        }

        public async Task ProbeAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(options.ReadinessProbeTimeoutSeconds));
            _ = await ReadVersionAsync(timeout.Token);
        }

        private async Task<(string EngineVersion, string SignatureVersion)> ReadVersionAsync(
            CancellationToken cancellationToken)
        {
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            connectTimeout.CancelAfter(TimeSpan.FromSeconds(options.ConnectTimeoutSeconds));
            using var client = new TcpClient();
            await client.ConnectAsync(options.ClamAvHost, options.ClamAvPort, connectTimeout.Token);
            await using var network = client.GetStream();
            await network.WriteAsync(VersionCommand, cancellationToken);
            await network.FlushAsync(cancellationToken);
            var response = await ReadResponseAsync(network, cancellationToken);
            if (!response.StartsWith("ClamAV ", StringComparison.Ordinal))
                throw new InvalidOperationException("The malware scanner version response was invalid.");
            var versionToken = response.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(value => value.Contains('/', StringComparison.Ordinal));
            var parts = versionToken?.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts is not { Length: >= 2 }
                || parts[0].Length > 128
                || parts[1].Length > 128)
                throw new InvalidOperationException("The malware scanner version response was invalid.");
            return (parts[0], parts[1]);
        }

        private static async Task<string> ReadResponseAsync(
            NetworkStream network,
            CancellationToken cancellationToken)
        {
            var response = new byte[MaximumResponseBytes];
            var length = 0;
            while (length < response.Length)
            {
                var read = await network.ReadAsync(
                    response.AsMemory(length, 1),
                    cancellationToken);
                if (read == 0 || response[length] is 0 or (byte)'\n')
                    break;
                length += read;
            }

            if (length == response.Length)
                throw new InvalidOperationException("The malware scanner response was too large.");
            return Encoding.ASCII.GetString(response, 0, length).Trim();
        }
    }
}
