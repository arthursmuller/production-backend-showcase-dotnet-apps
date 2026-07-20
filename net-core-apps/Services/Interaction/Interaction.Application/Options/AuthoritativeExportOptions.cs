using BuildingBlocks.Security;

namespace Interaction.Application.Options;

public sealed class AuthoritativeExportOptions
{
    public const string SectionName = "AuthoritativeExport";
    public const int MaximumPageSize = 500;
    public string CursorSigningKey { get; set; } = string.Empty;
    public int CursorLifetimeMinutes { get; set; } = 360;

    public static bool IsValid(AuthoritativeExportOptions options)
    {
        if (options.CursorLifetimeMinutes is < 60 or > 1440)
            return false;
        return Base64KeyMaterial.TryDecode(
            options.CursorSigningKey,
            out _,
            rejectSurroundingWhitespace: false);
    }
}
