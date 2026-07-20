using System;
using System.IO;
using System.Linq;

namespace StorageProvider;

internal static class StorageObjectKey
{
    private const int MaximumNamespaceLength = 64;
    private const int MaximumFileNameLength = 128;
    private const int MaximumExtensionLength = 10;

    public static string Build(
        string fileNamespace,
        int? userId,
        string fileName,
        string? extension)
    {
        var normalizedNamespace = fileNamespace?.Trim();
        if (!IsSafeSegment(normalizedNamespace, MaximumNamespaceLength, requireLowerCase: true))
            throw new ArgumentException("The storage namespace is invalid.", nameof(fileNamespace));
        if (userId.HasValue && userId.Value <= 0)
            throw new ArgumentOutOfRangeException(nameof(userId), "The storage user identifier must be positive.");

        var normalizedName = fileName?.Trim();
        if (!IsSafeSegment(normalizedName, MaximumFileNameLength, requireLowerCase: false)
            || !string.Equals(Path.GetFileName(normalizedName), normalizedName, StringComparison.Ordinal))
        {
            throw new ArgumentException("The storage object name is invalid.", nameof(fileName));
        }

        var suppliedExtension = NormalizeExtension(extension);
        var nameExtension = NormalizeExtension(Path.GetExtension(normalizedName));
        if (!string.IsNullOrEmpty(suppliedExtension)
            && !string.IsNullOrEmpty(nameExtension)
            && !suppliedExtension.Equals(nameExtension, StringComparison.Ordinal))
        {
            throw new ArgumentException("The storage object extension does not match its name.", nameof(extension));
        }

        var finalName = string.IsNullOrEmpty(nameExtension) && !string.IsNullOrEmpty(suppliedExtension)
            ? normalizedName + suppliedExtension
            : normalizedName;
        return userId.HasValue
            ? $"{normalizedNamespace}/{userId.Value}/{finalName}"
            : $"{normalizedNamespace}/{finalName}";
    }

    public static long GetValidatedLength(Stream content, long maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanRead || !content.CanSeek)
            throw new ArgumentException("Storage uploads require a readable, seekable stream.", nameof(content));
        if (content.Position != 0)
            content.Position = 0;

        var length = content.Length;
        if (length <= 0 || length > maximumBytes)
            throw new ArgumentOutOfRangeException(
                nameof(content),
                $"Storage upload length must be between 1 and {maximumBytes} bytes.");
        return length;
    }

    public static string ValidateMimeType(string? mimeType)
    {
        var normalized = mimeType?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Length > 100
            || normalized.Any(character => char.IsControl(character) || char.IsWhiteSpace(character))
            || !normalized.Contains('/', StringComparison.Ordinal))
        {
            throw new ArgumentException("The storage content type is invalid.", nameof(mimeType));
        }
        return normalized;
    }

    private static string NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return string.Empty;
        var normalized = extension.Trim();
        if (normalized[0] != '.')
            normalized = "." + normalized;
        if (normalized.Length is < 2 or > MaximumExtensionLength + 1
            || normalized[1..].Any(character =>
                !char.IsAsciiLetterOrDigit(character)
                || char.IsAsciiLetterUpper(character)))
        {
            throw new ArgumentException("The storage object extension is invalid.", nameof(extension));
        }
        return normalized;
    }

    private static bool IsSafeSegment(
        string? value,
        int maximumLength,
        bool requireLowerCase) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Length <= maximumLength
        && value.All(character =>
            char.IsAsciiLetterOrDigit(character)
            || character is '-' or '_' or '.')
        && (!requireLowerCase || value.All(character => !char.IsAsciiLetterUpper(character)))
        && value is not "." and not "..";
}
