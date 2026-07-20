using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Architecture.Tests;

public sealed partial class CredentialConfigurationTests
{
    [Fact]
    public void Committed_runtime_configuration_has_no_plaintext_credentials_or_secret_defaults()
    {
        var violations = new List<string>();
        foreach (var path in JsonConfigurationFiles())
            InspectJson(path, violations);
        foreach (var path in ComposeFiles())
            InspectCompose(path, violations);
        InspectEnvironmentExample(violations);
        InspectDevelopmentOverrides(violations);

        Assert.True(
            violations.Count == 0,
            "Committed runtime configuration contains plaintext credentials or reusable " +
            "secret defaults:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void Service_container_contexts_exclude_local_credentials_and_git_metadata()
    {
        var dockerfiles = Path.Combine(RepositoryRoot, "DevOps", "Dockerfiles");
        var ignoreFiles = Directory
            .EnumerateFiles(dockerfiles, "*.Dockerfile.dockerignore", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(5, ignoreFiles.Length);

        string[] required =
        [
            "**/.git",
            "**/.env",
            "**/.env.*",
            "**/appsettings.Development.json",
            "**/launchSettings.json",
            "**/*.pfx",
            "**/*.p12",
            "**/*.pem",
            "**/*.key",
            "**/*secrets*.json",
            "**/*secrets*.yaml",
            "**/*secrets*.yml",
        ];

        foreach (var path in ignoreFiles)
        {
            var lines = File.ReadAllLines(path)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith('#'))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var pattern in required)
                Assert.Contains(pattern, lines);
            Assert.DoesNotContain(lines, line => line.StartsWith("!.git", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static void InspectJson(string path, ICollection<string> violations)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(File.ReadAllText(path));
        }
        catch (JsonException)
        {
            violations.Add($"{Relative(path)}: invalid JSON");
            return;
        }

        using (document)
            InspectJsonElement(document.RootElement, [], path, violations);
    }

    private static void InspectJsonElement(
        JsonElement element,
        IReadOnlyList<string> parentPath,
        string file,
        ICollection<string> violations)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return;

        foreach (var property in element.EnumerateObject())
        {
            var propertyPath = parentPath.Append(property.Name).ToArray();
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                InspectJsonElement(property.Value, propertyPath, file, violations);
                continue;
            }
            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                var index = 0;
                foreach (var item in property.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.Object)
                    {
                        InspectJsonElement(
                            item,
                            propertyPath.Append(index.ToString()).ToArray(),
                            file,
                            violations);
                    }
                    index++;
                }
                continue;
            }
            if (property.Value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                continue;
            if (IsSecretReferenceMetadata(propertyPath))
                continue;

            var value = property.Value.ValueKind == JsonValueKind.Null
                ? null
                : property.Value.GetString();
            if (IsSensitivePath(propertyPath) && !IsAllowedJsonSecretValue(file, value))
            {
                violations.Add(
                    $"{Relative(file)}:{string.Join(':', propertyPath)}: plaintext credential value");
                continue;
            }
            if (!string.IsNullOrWhiteSpace(value)
                && !SecretAlias().IsMatch(value)
                && ContainsInlineCredential(value))
            {
                violations.Add(
                    $"{Relative(file)}:{string.Join(':', propertyPath)}: embedded credential material");
            }
        }
    }

    private static void InspectCompose(string path, ICollection<string> violations)
    {
        var source = File.ReadAllText(path);
        foreach (Match interpolation in UnescapedInterpolation().Matches(source))
        {
            var variable = interpolation.Groups["name"].Value;
            if (IsSensitiveName(variable)
                && !string.Equals(interpolation.Groups["operator"].Value, ":?", StringComparison.Ordinal))
            {
                violations.Add(
                    $"{Relative(path)}: secret variable {variable} is not required with :?");
            }
        }

        var lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            var match = ComposeSetting().Match(line);
            if (!match.Success || !IsSensitiveName(match.Groups["key"].Value))
                continue;

            var key = match.Groups["key"].Value;
            var value = TrimYamlScalar(match.Groups["value"].Value);
            if (string.IsNullOrWhiteSpace(value) || SecretAlias().IsMatch(value))
                continue;

            if (!IsConnectionName(key))
            {
                if (!RequiredInterpolationOnly().IsMatch(value))
                {
                    violations.Add(
                        $"{Relative(path)}:{lineNumber}: secret setting {key} must use one required environment variable");
                }
                continue;
            }

            var interpolations = UnescapedInterpolation().Matches(value).Cast<Match>().ToArray();
            if (interpolations.Length == 0
                || interpolations.Any(item => !string.Equals(
                    item.Groups["operator"].Value,
                    ":?",
                    StringComparison.Ordinal)))
            {
                violations.Add(
                    $"{Relative(path)}:{lineNumber}: connection setting {key} must use required environment secrets");
                continue;
            }

            var withoutRequiredValues = UnescapedInterpolation().Replace(value, string.Empty);
            if (ContainsInlineCredential(withoutRequiredValues))
            {
                violations.Add(
                    $"{Relative(path)}:{lineNumber}: connection setting {key} embeds credential material");
            }
        }
    }

    private static void InspectEnvironmentExample(ICollection<string> violations)
    {
        var path = Path.Combine(RepositoryRoot, "DevOps", ".env.example");
        if (!File.Exists(path))
        {
            violations.Add("DevOps/.env.example: missing blank local-secret template");
            return;
        }

        var lineNumber = 0;
        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            var match = EnvironmentSetting().Match(line);
            if (!match.Success || !IsSensitiveName(match.Groups["key"].Value))
                continue;
            if (!string.IsNullOrWhiteSpace(match.Groups["value"].Value))
            {
                violations.Add(
                    $"DevOps/.env.example:{lineNumber}: secret examples must remain blank");
            }
        }
    }

    private static void InspectDevelopmentOverrides(ICollection<string> violations)
    {
        var services = Path.Combine(RepositoryRoot, "Services");
        foreach (var baselinePath in Directory.EnumerateFiles(
            services,
            "appsettings.json",
            SearchOption.AllDirectories).Where(path =>
                !HasSegment(path, "Tests")
                && !HasSegment(path, "bin")
                && !HasSegment(path, "obj")))
        {
            var developmentPath = Path.Combine(
                Path.GetDirectoryName(baselinePath)!,
                "appsettings.Development.json");
            if (!File.Exists(developmentPath))
                continue;

            using var baseline = JsonDocument.Parse(File.ReadAllText(baselinePath));
            using var development = JsonDocument.Parse(File.ReadAllText(developmentPath));
            var aliases = new List<string[]>();
            CollectSensitiveAliases(baseline.RootElement, [], aliases);
            foreach (var aliasPath in aliases)
            {
                if (!TryGetPath(development.RootElement, aliasPath, out var overrideValue)
                    || overrideValue.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)
                    || (overrideValue.ValueKind == JsonValueKind.String
                        && !string.IsNullOrWhiteSpace(overrideValue.GetString())))
                {
                    violations.Add(
                        $"{Relative(developmentPath)}:{string.Join(':', aliasPath)}: " +
                        "development must provide a blank external-override boundary");
                }
            }
        }
    }

    private static void CollectSensitiveAliases(
        JsonElement element,
        IReadOnlyList<string> parentPath,
        ICollection<string[]> aliases)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return;
        foreach (var property in element.EnumerateObject())
        {
            var propertyPath = parentPath.Append(property.Name).ToArray();
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                CollectSensitiveAliases(property.Value, propertyPath, aliases);
            }
            else if (property.Value.ValueKind == JsonValueKind.String
                && !IsSecretReferenceMetadata(propertyPath)
                && IsSensitivePath(propertyPath)
                && SecretAlias().IsMatch(property.Value.GetString()!))
            {
                aliases.Add(propertyPath);
            }
        }
    }

    private static bool TryGetPath(
        JsonElement root,
        IReadOnlyList<string> path,
        out JsonElement value)
    {
        value = root;
        foreach (var segment in path)
        {
            if (value.ValueKind != JsonValueKind.Object
                || !value.TryGetProperty(segment, out value))
                return false;
        }
        return true;
    }

    private static bool IsAllowedJsonSecretValue(string file, string? value)
    {
        if (Path.GetFileName(file).Equals(
            "appsettings.Development.json",
            StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrWhiteSpace(value);
        return string.IsNullOrWhiteSpace(value) || SecretAlias().IsMatch(value);
    }

    private static bool IsSecretReferenceMetadata(IReadOnlyList<string> path)
    {
        for (var index = 0; index + 1 < path.Count; index++)
        {
            if (path[index].Equals("SecretProvider", StringComparison.OrdinalIgnoreCase)
                && path[index + 1].Equals("References", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool IsSensitivePath(IReadOnlyList<string> path) =>
        path.Any(segment => Normalize(segment).Equals("signingkeys", StringComparison.Ordinal))
        || IsSensitiveName(path[^1]);

    private static bool IsSensitiveName(string name)
    {
        var normalized = Normalize(name);
        return normalized.Contains("password", StringComparison.Ordinal)
            || normalized.EndsWith("pass", StringComparison.Ordinal)
            || normalized.Contains("secret", StringComparison.Ordinal)
            || normalized.Contains("credential", StringComparison.Ordinal)
            || normalized.Contains("signingkey", StringComparison.Ordinal)
            || normalized.Contains("hmackey", StringComparison.Ordinal)
            || normalized.Contains("hashkey", StringComparison.Ordinal)
            || normalized.Contains("accesskey", StringComparison.Ordinal)
            || normalized.Contains("privatekey", StringComparison.Ordinal)
            || normalized.Contains("encryptionkey", StringComparison.Ordinal)
            || normalized.Contains("apikey", StringComparison.Ordinal)
            || normalized.Contains("connectionstring", StringComparison.Ordinal)
            || normalized.EndsWith("connection", StringComparison.Ordinal)
            || normalized == "key";
    }

    private static bool IsConnectionName(string name)
    {
        var normalized = Normalize(name);
        return normalized.Contains("connectionstring", StringComparison.Ordinal)
            || normalized.EndsWith("connection", StringComparison.Ordinal);
    }

    private static bool ContainsInlineCredential(string value) =>
        InlineAssignmentCredential().IsMatch(value)
        || UriCredential().IsMatch(value)
        || RedisCredential().IsMatch(value)
        || value.Contains("-----BEGIN " + "PRIVATE KEY-----", StringComparison.OrdinalIgnoreCase)
        || value.Contains("-----BEGIN RSA " + "PRIVATE KEY-----", StringComparison.OrdinalIgnoreCase);

    private static string TrimYamlScalar(string value)
    {
        var result = value.Trim();
        if (result.Length >= 2
            && ((result[0] == '"' && result[^1] == '"')
                || (result[0] == '\'' && result[^1] == '\'')))
            result = result[1..^1];
        return result;
    }

    private static string Normalize(string value) =>
        string.Concat(value.Where(char.IsAsciiLetterOrDigit)).ToLowerInvariant();

    private static IEnumerable<string> JsonConfigurationFiles()
    {
        var services = Path.Combine(RepositoryRoot, "Services");
        var files = Directory.EnumerateFiles(services, "*.json", SearchOption.AllDirectories)
            .Where(path => !HasSegment(path, "Tests")
                && !HasSegment(path, "bin")
                && !HasSegment(path, "obj"))
            .Where(path => Path.GetFileName(path).StartsWith(
                    "appsettings",
                    StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(path).Equals(
                    "launchSettings.json",
                    StringComparison.OrdinalIgnoreCase));
        var rootLaunchSettings = Path.Combine(RepositoryRoot, "launchSettings.json");
        return File.Exists(rootLaunchSettings)
            ? files.Append(rootLaunchSettings)
            : files;
    }

    private static IEnumerable<string> ComposeFiles() =>
        Directory.EnumerateFiles(Path.Combine(RepositoryRoot, "DevOps"), "docker-compose*.yml")
            .Concat(Directory.EnumerateFiles(
                Path.Combine(RepositoryRoot, "DevOps"),
                "docker-compose*.yaml"));

    private static bool HasSegment(string path, string segment) =>
        path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(value => value.Equals(segment, StringComparison.OrdinalIgnoreCase));

    private static string Relative(string path) => Path.GetRelativePath(RepositoryRoot, path);

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Directory.Packages.props")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the net-core-apps repository root.");
    }

    [GeneratedRegex(@"^\{\{secret:[A-Za-z0-9_.-]+\}\}$", RegexOptions.CultureInvariant)]
    private static partial Regex SecretAlias();

    [GeneratedRegex(
        @"(?<!\$)\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)(?<operator>:\?|:-|\?|-)?[^}]*\}",
        RegexOptions.CultureInvariant)]
    private static partial Regex UnescapedInterpolation();

    [GeneratedRegex(
        @"^\$\{[A-Za-z_][A-Za-z0-9_]*:\?[^}]+\}$",
        RegexOptions.CultureInvariant)]
    private static partial Regex RequiredInterpolationOnly();

    [GeneratedRegex(
        @"^\s*(?:-\s*)?(?<key>[A-Za-z_][A-Za-z0-9_.-]*(?:__[A-Za-z0-9_.-]+)*)\s*(?::|=)\s*(?<value>.*?)\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex ComposeSetting();

    [GeneratedRegex(
        @"^\s*(?<key>[A-Za-z_][A-Za-z0-9_]*)\s*=\s*(?<value>.*)\s*$",
        RegexOptions.CultureInvariant)]
    private static partial Regex EnvironmentSetting();

    [GeneratedRegex(
        @"(?:password|pwd|accountkey|sharedaccesskey|clientsecret)\s*=\s*[^;,\s]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InlineAssignmentCredential();

    [GeneratedRegex(
        @"\b[a-z][a-z0-9+.-]*://[^/\s:@]+:[^@$\s]+@",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UriCredential();

    [GeneratedRegex(
        @"\bredis:(?!//)[^@$\s]+@",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RedisCredential();
}
