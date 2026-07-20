using System.Security.Cryptography;

namespace BuildingBlocks.Security;

/// <summary>
/// Produces and verifies HMAC-SHA256 authentication tags using company key bounds.
/// </summary>
public static class HmacSha256
{
    public const int TagSizeInBytes = 32;

    public static bool IsValidKey(ReadOnlySpan<byte> key) =>
        key.Length >= Base64KeyMaterial.MinimumKeySizeInBytes;

    public static byte[] Protect(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> payload)
    {
        ValidateKey(key);
        return HMACSHA256.HashData(key, payload);
    }

    public static bool Verify(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> payload,
        ReadOnlySpan<byte> suppliedTag)
    {
        var expectedTag = Protect(key, payload);
        return CryptographicOperations.FixedTimeEquals(suppliedTag, expectedTag);
    }

    private static void ValidateKey(ReadOnlySpan<byte> key)
    {
        if (!IsValidKey(key))
        {
            throw new ArgumentException(
                $"HMAC-SHA256 keys must be at least {Base64KeyMaterial.MinimumKeySizeInBytes} bytes.",
                nameof(key));
        }
    }
}
