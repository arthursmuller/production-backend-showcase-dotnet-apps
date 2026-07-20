using System.Globalization;
using System.Text;

namespace Moderation.Application.Features.Shared;

public static class ModerationCursor
{
    public static string Encode(DateTimeOffset receivedAtUtc, Guid id)
    {
        var value = $"{receivedAtUtc.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture)}:{id:D}";
        return Convert.ToBase64String(Encoding.ASCII.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static bool TryDecode(
        string? cursor,
        out DateTimeOffset? receivedAtUtc,
        out Guid? id)
    {
        receivedAtUtc = null;
        id = null;
        if (string.IsNullOrWhiteSpace(cursor))
            return true;
        if (cursor.Length > 128)
            return false;

        try
        {
            var normalized = cursor.Replace('-', '+').Replace('_', '/');
            normalized = normalized.PadRight((normalized.Length + 3) / 4 * 4, '=');
            var decoded = Encoding.ASCII.GetString(Convert.FromBase64String(normalized));
            var separator = decoded.IndexOf(':');
            if (separator <= 0
                || !long.TryParse(decoded.AsSpan(0, separator), CultureInfo.InvariantCulture, out var ticks)
                || !Guid.TryParseExact(decoded[(separator + 1)..], "D", out var parsedId))
            {
                return false;
            }

            receivedAtUtc = new DateTimeOffset(ticks, TimeSpan.Zero);
            id = parsedId;
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
