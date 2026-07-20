using BuildingBlocks.Security;

namespace Moderation.Application.Options;

public sealed class ModerationDecisionExportOptions
{
    public const string SectionName = "DecisionExport";
    public const int MaximumPageSize = 500;
    public string CursorSigningKey { get; set; } = string.Empty;
    public int CursorLifetimeMinutes { get; set; } = 360;

    public static bool IsValid(ModerationDecisionExportOptions options)
    {
        if (options.CursorLifetimeMinutes is < 60 or > 1440)
            return false;
        return Base64KeyMaterial.TryDecode(
            options.CursorSigningKey,
            out _,
            rejectSurroundingWhitespace: false);
    }
}
