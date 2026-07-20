using System.Text.RegularExpressions;
using SecretProvider.Providers.Api;

namespace SecretProvider.Configuration;

internal static class SecretPlaceholder
{
    private const string ReservedMarker = "{{secret";

    private static readonly Regex Pattern = new(
        @"\{\{secret:(?<alias>[A-Za-z0-9][A-Za-z0-9._-]*)\}\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));

    public static bool ContainsReservedMarker(string? value) =>
        value?.Contains(ReservedMarker, StringComparison.OrdinalIgnoreCase) == true;

    public static IReadOnlyCollection<SecretReference> GetReferences(
        string value,
        SecretApiOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(options);

        var matches = ValidateAndGetMatches(value);
        return matches
            .Select(match => ToReference(match, options))
            .Distinct()
            .ToArray();
    }

    public static string Resolve(
        string value,
        SecretApiOptions options,
        IReadOnlyDictionary<SecretReference, string> resolvedSecrets)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(resolvedSecrets);

        _ = ValidateAndGetMatches(value);
        return Pattern.Replace(value, match =>
        {
            var reference = ToReference(match, options);
            if (!resolvedSecrets.TryGetValue(reference, out var secret)
                || string.IsNullOrWhiteSpace(secret))
            {
                throw new InvalidOperationException(
                    $"Secret provider returned no usable value for reference '{reference}'.");
            }

            return secret;
        });
    }

    public static void Validate(string value, SecretApiOptions options) =>
        _ = GetReferences(value, options);

    private static Match[] ValidateAndGetMatches(string value)
    {
        var matches = Pattern.Matches(value).Cast<Match>().ToArray();

        if (!ContainsReservedMarker(value))
            return matches;

        var unmatched = Pattern.Replace(value, string.Empty);
        if (ContainsReservedMarker(unmatched) || matches.Length == 0)
        {
            throw new InvalidOperationException(
                "Configuration contains a malformed secret placeholder. " +
                "Use '{{secret:Alias}}'.");
        }

        return matches;
    }

    private static SecretReference ToReference(Match match, SecretApiOptions options)
    {
        return options.ResolveAlias(match.Groups["alias"].Value);
    }
}
