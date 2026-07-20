namespace BuildingBlocks.Security;

/// <summary>
/// Validates and decodes bounded base64-encoded symmetric key material.
/// </summary>
public static class Base64KeyMaterial
{
    public const int MinimumKeySizeInBytes = 32;
    public const int MaximumEncodedKeyLength = 256;

    public static bool TryDecode(
        string? encoded,
        out byte[] key,
        bool rejectSurroundingWhitespace = true)
    {
        key = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(encoded)
            || encoded.Length > MaximumEncodedKeyLength
            || (rejectSurroundingWhitespace
                && !string.Equals(encoded, encoded.Trim(), StringComparison.Ordinal)))
        {
            return false;
        }

        try
        {
            key = Convert.FromBase64String(encoded);
            if (key.Length >= MinimumKeySizeInBytes)
                return true;
        }
        catch (FormatException)
        {
            // Invalid configuration is reported by the calling options validator.
        }

        key = Array.Empty<byte>();
        return false;
    }
}
