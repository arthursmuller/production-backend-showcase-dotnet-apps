using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace SecretProvider.Providers.Api;

internal static class MachineCertificateLoader
{
    private const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";

    private const UnixFileMode ForbiddenPrivateKeyFileModes =
        UnixFileMode.SetUser |
        UnixFileMode.SetGroup |
        UnixFileMode.StickyBit |
        UnixFileMode.UserExecute |
        UnixFileMode.GroupWrite |
        UnixFileMode.GroupExecute |
        UnixFileMode.OtherRead |
        UnixFileMode.OtherWrite |
        UnixFileMode.OtherExecute;

    public static X509Certificate2 Load(MachineCertificateOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!string.IsNullOrWhiteSpace(options.Path))
            return LoadFromFile(options);

        if (!string.IsNullOrEmpty(options.Password))
        {
            throw new InvalidOperationException(
                $"{SecretApiOptions.SectionName}:Certificate:Password can be used only with Certificate:Path.");
        }

        var thumbprint = NormalizeThumbprint(options.Thumbprint);
        if (string.IsNullOrEmpty(thumbprint) && string.IsNullOrWhiteSpace(options.SubjectName))
        {
            throw new InvalidOperationException(
                $"{SecretApiOptions.SectionName}:Certificate must specify Path, Thumbprint, or SubjectName.");
        }

        using var store = new X509Store(options.StoreName, options.StoreLocation);
        try
        {
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException(
                $"Could not open certificate store {options.StoreLocation}/{options.StoreName}.",
                exception);
        }

        var now = DateTimeOffset.UtcNow;
        var candidates = store.Certificates
            .Where(certificate => Matches(certificate, thumbprint, options.SubjectName))
            .Where(certificate => certificate.NotBefore.ToUniversalTime() <= now.UtcDateTime
                && now.UtcDateTime < certificate.NotAfter.ToUniversalTime())
            .Where(certificate => certificate.HasPrivateKey)
            .Where(certificate => HasRequiredEku(certificate, options.RequireClientAuthenticationEku))
            .OrderByDescending(certificate => certificate.NotAfter)
            .ToArray();

        if (candidates.Length == 0)
        {
            throw new InvalidOperationException(
                $"No valid client certificate with a private key was found in " +
                $"{options.StoreLocation}/{options.StoreName}. " +
                "Verify the configured thumbprint/subject, validity period, private-key ACL, and client-authentication EKU.");
        }

        var selected = candidates[0];
        foreach (var candidate in candidates.Skip(1))
            candidate.Dispose();

        return selected;
    }

    private static X509Certificate2 LoadFromFile(MachineCertificateOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.Thumbprint)
            || !string.IsNullOrWhiteSpace(options.SubjectName))
        {
            throw new InvalidOperationException(
                $"{SecretApiOptions.SectionName}:Certificate:Path is mutually exclusive with Thumbprint and SubjectName.");
        }

        if (!System.IO.Path.IsPathFullyQualified(options.Path!))
        {
            throw new InvalidOperationException(
                $"{SecretApiOptions.SectionName}:Certificate:Path must be an absolute path.");
        }

        if (!File.Exists(options.Path))
        {
            throw new InvalidOperationException(
                "The configured secret-provider client certificate file does not exist or is not accessible.");
        }

        ValidateUnixFilePermissions(options.Path);

        X509Certificate2 certificate;
        try
        {
            certificate = X509CertificateLoader.LoadPkcs12FromFile(
                options.Path,
                options.Password,
                X509KeyStorageFlags.EphemeralKeySet);
        }
        catch (Exception exception) when (exception is CryptographicException
            or IOException
            or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "The configured secret-provider client certificate could not be loaded. " +
                "Verify PKCS#12 content, password, mount ownership, and read permissions.",
                exception);
        }

        var now = DateTimeOffset.UtcNow.UtcDateTime;
        if (!certificate.HasPrivateKey
            || certificate.NotBefore.ToUniversalTime() > now
            || now >= certificate.NotAfter.ToUniversalTime()
            || !HasRequiredEku(certificate, options.RequireClientAuthenticationEku))
        {
            certificate.Dispose();
            throw new InvalidOperationException(
                "The configured secret-provider client certificate must be currently valid, " +
                "contain an accessible private key, and permit client authentication.");
        }

        return certificate;
    }

    internal static void ValidateUnixFilePermissions(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!OperatingSystem.IsLinux()
            && !OperatingSystem.IsMacOS()
            && !OperatingSystem.IsFreeBSD())
        {
            return;
        }

        UnixFileMode mode;
        try
        {
            // Kubernetes projected Secrets expose each key through an atomic symlink. Inspect
            // the final target rather than the symlink's intentionally broad 0777 metadata.
            var resolvedTarget = File.ResolveLinkTarget(path, returnFinalTarget: true);
            mode = File.GetUnixFileMode(resolvedTarget?.FullName ?? path);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "The secret-provider client certificate file permissions could not be inspected.",
                exception);
        }

        if (!IsSecureUnixFileMode(mode))
        {
            throw new InvalidOperationException(
                "The secret-provider client certificate file must be readable by its owner or " +
                "workload group and must not be executable, group-writable, or accessible by others.");
        }
    }

    internal static bool IsSecureUnixFileMode(UnixFileMode mode) =>
        (mode & (UnixFileMode.UserRead | UnixFileMode.GroupRead)) != 0
        && (mode & ForbiddenPrivateKeyFileModes) == 0;

    private static bool Matches(
        X509Certificate2 certificate,
        string? normalizedThumbprint,
        string? subjectName)
    {
        if (!string.IsNullOrEmpty(normalizedThumbprint)
            && !string.Equals(
                NormalizeThumbprint(certificate.Thumbprint),
                normalizedThumbprint,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(subjectName))
            return true;

        return string.Equals(certificate.Subject, subjectName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(
                certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false),
                subjectName,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasRequiredEku(X509Certificate2 certificate, bool requireClientAuthentication)
    {
        if (!requireClientAuthentication)
            return true;

        var ekuExtensions = certificate.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .ToArray();

        // An absent EKU extension means the certificate is valid for all usages.
        return ekuExtensions.Length == 0
            || ekuExtensions.Any(extension => extension.EnhancedKeyUsages
                .Cast<Oid>()
                .Any(oid => oid.Value == ClientAuthenticationOid));
    }

    private static string? NormalizeThumbprint(string? thumbprint) =>
        string.IsNullOrWhiteSpace(thumbprint)
            ? null
            : string.Concat(thumbprint.Where(character => !char.IsWhiteSpace(character)));
}
