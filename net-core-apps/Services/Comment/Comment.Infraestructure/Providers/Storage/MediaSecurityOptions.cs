using System;
using System.Net;

namespace Comment.Infra.Providers.Storage
{
    public sealed class MediaSecurityOptions
    {
        public const string SectionName = "Storage:MediaSecurity";

        public bool RequireMalwareScan { get; set; } = true;
        public string ClamAvHost { get; set; } = string.Empty;
        public int ClamAvPort { get; set; } = 3310;
        public int ConnectTimeoutSeconds { get; set; } = 2;
        public int ScanTimeoutSeconds { get; set; } = 30;
        public int ChunkBytes { get; set; } = 16 * 1024;
        public int MaximumConcurrentScansPerReplica { get; set; } = 4;
        public int CircuitBreakerFailureThreshold { get; set; } = 3;
        public int CircuitBreakerOpenSeconds { get; set; } = 30;
        public int ReadinessProbeTimeoutSeconds { get; set; } = 2;

        public bool IsValid(string environment)
        {
            var isDevelopment = string.Equals(
                environment,
                "Development",
                StringComparison.OrdinalIgnoreCase);
            if (!RequireMalwareScan && !isDevelopment)
                return false;
            if (RequireMalwareScan)
            {
                if (string.IsNullOrWhiteSpace(ClamAvHost)
                    || ClamAvHost.Length > 253
                    || Uri.CheckHostName(ClamAvHost) == UriHostNameType.Unknown)
                    return false;
                if (!isDevelopment
                    && (ClamAvHost.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                        || (IPAddress.TryParse(ClamAvHost, out var address)
                            && IPAddress.IsLoopback(address))))
                    return false;
            }
            return ClamAvPort is > 0 and <= 65535
                && ConnectTimeoutSeconds is >= 1 and <= 10
                && ScanTimeoutSeconds is >= 5 and <= 120
                && ChunkBytes is >= 4096 and <= 64 * 1024
                && MaximumConcurrentScansPerReplica is >= 1 and <= 64
                && CircuitBreakerFailureThreshold is >= 1 and <= 20
                && CircuitBreakerOpenSeconds is >= 1 and <= 300
                && ReadinessProbeTimeoutSeconds is >= 1 and <= 10;
        }
    }
}
