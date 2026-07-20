namespace BuildingBlocks.Security;

/// <summary>
/// Converts binary values to and from unpadded base64url representations.
/// </summary>
public static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> value) =>
        Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    /// <summary>
    /// Decodes base64url while retaining compatibility with standard base64 input.
    /// </summary>
    /// <exception cref="FormatException">The value is not valid base64.</exception>
    public static byte[] Decode(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight((normalized.Length + 3) / 4 * 4, '=');
        return Convert.FromBase64String(normalized);
    }

    /// <summary>
    /// Strictly decodes an unpadded base64url value within the supplied input bound.
    /// </summary>
    public static bool TryDecode(
        string? encoded,
        out byte[] value,
        int maximumEncodedLength)
    {
        if (maximumEncodedLength < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumEncodedLength));

        value = Array.Empty<byte>();
        if (encoded is null
            || encoded.Length > maximumEncodedLength
            || encoded.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            return false;
        }

        var normalized = encoded.Replace('-', '+').Replace('_', '/');
        normalized += (normalized.Length % 4) switch
        {
            0 => string.Empty,
            2 => "==",
            3 => "=",
            _ => "invalid",
        };

        try
        {
            value = Convert.FromBase64String(normalized);
            if (string.Equals(Encode(value), encoded, StringComparison.Ordinal))
                return true;

            value = Array.Empty<byte>();
            return false;
        }
        catch (FormatException)
        {
            value = Array.Empty<byte>();
            return false;
        }
    }
}
