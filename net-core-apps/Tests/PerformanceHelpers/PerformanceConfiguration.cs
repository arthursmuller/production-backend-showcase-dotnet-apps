using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PerformanceTesting;

internal static class PerformanceConfigurationLoader
{
    private static readonly HashSet<string> SupportedProfiles =
        new(StringComparer.OrdinalIgnoreCase) { "smoke", "baseline", "representative" };

    private static readonly HashSet<string> SupportedArguments =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "execute",
            "profile",
            "run-id",
            "base-url",
            "output",
        };

    public static PerformanceRunConfiguration Load(
        string[] args,
        WorkloadKind workload,
        PerformanceScenario scenario)
    {
        var arguments = ParseArguments(args);
        RequireExplicitExecution(arguments);

        var profileName = Get(arguments, "profile", "PERF_PROFILE") ?? "smoke";
        if (!SupportedProfiles.Contains(profileName))
        {
            throw new PerformanceConfigurationException(
                "PERF_PROFILE must be smoke, baseline, or representative.");
        }

        var document = PerformanceProfileValidator.ReadDocument(
            Path.Combine(AppContext.BaseDirectory, "profiles.json"));
        if (!TryGet(document.Profiles, profileName, out var profile))
            throw new PerformanceConfigurationException($"Profile '{profileName}' is missing from profiles.json.");

        var runId = Get(arguments, "run-id", "PERF_RUN_ID");
        if (string.IsNullOrWhiteSpace(runId))
            throw new PerformanceConfigurationException("PERF_RUN_ID (or --run-id) is required.");

        ValidateRunId(runId);

        var baseUrlText = Get(arguments, "base-url", "PERF_BASE_URL")
            ?? scenario.DefaultBaseUrl.AbsoluteUri;
        if (!Uri.TryCreate(baseUrlText, UriKind.Absolute, out var baseUrl) ||
            (baseUrl.Scheme != Uri.UriSchemeHttp && baseUrl.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(baseUrl.UserInfo) ||
            !string.IsNullOrEmpty(baseUrl.Query) ||
            !string.IsNullOrEmpty(baseUrl.Fragment))
        {
            throw new PerformanceConfigurationException(
                "PERF_BASE_URL must be an absolute HTTP(S) URL without credentials, query, or fragment.");
        }

        var loopback = IsLoopback(baseUrl);
        if (!loopback && !ReadBoolean("PERF_ALLOW_NON_LOOPBACK"))
        {
            throw new PerformanceConfigurationException(
                "Non-loopback targets are refused unless PERF_ALLOW_NON_LOOPBACK=true is supplied explicitly.");
        }

        var targetEnvironment = Environment.GetEnvironmentVariable("PERF_TARGET_ENVIRONMENT")
            ?? (loopback ? "local" : string.Empty);
        ValidateTargetEnvironment(targetEnvironment, loopback);

        var duration = ResolvePositiveInt(profile, profile.DurationSeconds, "PERF_DURATION_SECONDS", profileName);
        var concurrency = ResolvePositiveInt(profile, profile.Concurrency, "PERF_CONCURRENCY", profileName);
        var targetRps = ResolvePositiveDouble(profile, profile.TargetRps, "PERF_TARGET_RPS", profileName);
        var timeout = ResolvePositiveInt(
            profile,
            profile.RequestTimeoutSeconds,
            "PERF_REQUEST_TIMEOUT_SECONDS",
            profileName);
        var recovery = ResolveNonNegativeInt(profile.RecoverySeconds, "PERF_RECOVERY_SECONDS", 0);
        var startConcurrency = ResolveNonNegativeInt(
            profile.StartConcurrency,
            "PERF_START_CONCURRENCY",
            workload == WorkloadKind.Stress ? 1 : concurrency);

        if (duration > 604_800)
            throw new PerformanceConfigurationException("PERF_DURATION_SECONDS cannot exceed seven days.");
        if (concurrency > 10_000)
            throw new PerformanceConfigurationException("PERF_CONCURRENCY cannot exceed 10,000.");
        if (targetRps > 1_000_000)
            throw new PerformanceConfigurationException("PERF_TARGET_RPS cannot exceed 1,000,000.");
        if (timeout > 300)
            throw new PerformanceConfigurationException("PERF_REQUEST_TIMEOUT_SECONDS cannot exceed 300.");
        if (startConcurrency <= 0 || startConcurrency > concurrency)
        {
            throw new PerformanceConfigurationException(
                "PERF_START_CONCURRENCY must be greater than zero and no greater than PERF_CONCURRENCY.");
        }

        var arrivalModel = PerformanceProfileValidator.ParseArrivalModel(profile.ArrivalModel!);
        var burstEvery = ResolveOptionalPositiveInt(
            profile.BurstEverySeconds,
            "PERF_BURST_EVERY_SECONDS");
        var burstDuration = ResolveOptionalPositiveInt(
            profile.BurstDurationSeconds,
            "PERF_BURST_DURATION_SECONDS");
        var burstMultiplier = ResolveOptionalPositiveDouble(
            profile.BurstMultiplier,
            "PERF_BURST_MULTIPLIER");
        ValidateArrivalModel(
            workload,
            arrivalModel,
            recovery,
            startConcurrency,
            concurrency,
            burstEvery,
            burstDuration,
            burstMultiplier);

        var operations = HasOperationOverride()
            ? CreateOperationOverride(scenario)
            : PerformanceProfileValidator.ResolveOperations(document, profile);
        ValidateExecutionSafety(operations);

        var accessToken = Environment.GetEnvironmentVariable("PERF_ACCESS_TOKEN");
        ValidateAccessToken(accessToken);
        if (operations.Any(operation => operation.RequiresAuthentication) &&
            string.IsNullOrWhiteSpace(accessToken))
        {
            throw new PerformanceConfigurationException(
                "The selected mix contains authenticated operations. Supply PERF_ACCESS_TOKEN explicitly.");
        }

        var outputDirectory = Get(arguments, "output", "PERF_OUTPUT_DIR")
            ?? Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "performance");

        return new PerformanceRunConfiguration(
            runId,
            profileName.ToLowerInvariant(),
            targetEnvironment,
            EnsureTrailingSlash(baseUrl),
            operations,
            arrivalModel,
            accessToken,
            Path.GetFullPath(outputDirectory),
            duration,
            recovery,
            startConcurrency,
            concurrency,
            targetRps,
            timeout,
            burstEvery,
            burstDuration,
            burstMultiplier,
            ReadThresholds(),
            ReadOperationalObservations());
    }

    public static OperationalObservations ReadOperationalObservations() => new(
        ReadNullableDouble("PERF_DEPENDENCY_SATURATION_PERCENT"),
        ReadNullableDouble("PERF_TARGET_CPU_PERCENT"),
        ReadNullableDouble("PERF_TARGET_WORKING_SET_MB"),
        ReadNullableDouble("PERF_FRESHNESS_LAG_MS"),
        ReadNullableLong("PERF_BACKLOG_COUNT"));

    private static IReadOnlyList<PerformanceOperation> CreateOperationOverride(
        PerformanceScenario scenario)
    {
        var operation = new PerformanceOperation(
            "environment-override",
            1,
            Environment.GetEnvironmentVariable("PERF_PATH") ?? scenario.DefaultPath,
            ParseMethod(Environment.GetEnvironmentVariable("PERF_METHOD") ?? "GET"),
            Environment.GetEnvironmentVariable("PERF_REQUEST_BODY"),
            Environment.GetEnvironmentVariable("PERF_CONTENT_TYPE") ?? "application/json",
            ReadBoolean("PERF_REQUIRES_AUTHENTICATION"));
        PerformanceProfileValidator.ValidateOperation(operation);
        return [operation];
    }

    private static HttpMethod ParseMethod(string methodText)
    {
        try
        {
            return new HttpMethod(methodText.Trim().ToUpperInvariant());
        }
        catch (FormatException)
        {
            throw new PerformanceConfigurationException("PERF_METHOD is not a valid HTTP method.");
        }
    }

    private static bool HasOperationOverride() =>
        Environment.GetEnvironmentVariable("PERF_PATH") is not null ||
        Environment.GetEnvironmentVariable("PERF_METHOD") is not null ||
        Environment.GetEnvironmentVariable("PERF_REQUEST_BODY") is not null ||
        Environment.GetEnvironmentVariable("PERF_CONTENT_TYPE") is not null ||
        Environment.GetEnvironmentVariable("PERF_REQUIRES_AUTHENTICATION") is not null;

    private static void ValidateExecutionSafety(IReadOnlyList<PerformanceOperation> operations)
    {
        if (operations.Any(operation => operation.IsMutation) &&
            !ReadBoolean("PERF_ALLOW_MUTATIONS"))
        {
            throw new PerformanceConfigurationException(
                "The selected mix contains mutating requests. Supply PERF_ALLOW_MUTATIONS=true explicitly.");
        }
    }

    private static void ValidateArrivalModel(
        WorkloadKind workload,
        ArrivalModelKind arrivalModel,
        int recoverySeconds,
        int startConcurrency,
        int concurrency,
        int? burstEverySeconds,
        int? burstDurationSeconds,
        double? burstMultiplier)
    {
        var expected = workload switch
        {
            WorkloadKind.Load => ArrivalModelKind.PacedFixedRate,
            WorkloadKind.Stress => ArrivalModelKind.LinearConcurrencyRampWithRecovery,
            WorkloadKind.Soak => ArrivalModelKind.PeriodicBurst,
            _ => throw new PerformanceConfigurationException("Unsupported workload kind."),
        };
        if (arrivalModel != expected)
        {
            throw new PerformanceConfigurationException(
                $"The {workload.ToString().ToLowerInvariant()} runner requires arrivalModel " +
                $"'{PerformanceProfileValidator.ArrivalModelName(expected)}'.");
        }

        if (workload == WorkloadKind.Load &&
            (recoverySeconds != 0 || startConcurrency != concurrency))
        {
            throw new PerformanceConfigurationException(
                "Load profiles require zero recovery and startConcurrency equal to concurrency.");
        }

        if (workload == WorkloadKind.Stress &&
            (recoverySeconds <= 0 || startConcurrency >= concurrency))
        {
            throw new PerformanceConfigurationException(
                "Stress profiles require positive recovery and startConcurrency below concurrency.");
        }

        if (workload == WorkloadKind.Soak &&
            (recoverySeconds != 0 || startConcurrency != concurrency))
        {
            throw new PerformanceConfigurationException(
                "Soak profiles require zero recovery and startConcurrency equal to concurrency.");
        }

        if (workload != WorkloadKind.Soak)
        {
            if (burstEverySeconds.HasValue || burstDurationSeconds.HasValue || burstMultiplier.HasValue)
            {
                throw new PerformanceConfigurationException(
                    "Burst settings are valid only for soak profiles.");
            }

            return;
        }

        if (!burstEverySeconds.HasValue || !burstDurationSeconds.HasValue || !burstMultiplier.HasValue ||
            burstDurationSeconds.Value >= burstEverySeconds.Value ||
            burstMultiplier.Value <= 1 || burstMultiplier.Value > 100)
        {
            throw new PerformanceConfigurationException(
                "Soak profiles require a burst duration shorter than its period and a multiplier above 1 and at most 100.");
        }
    }

    private static int ResolvePositiveInt(
        PerformanceProfile profile,
        int? profileValue,
        string environmentName,
        string profileName)
    {
        var environmentValue = ReadNullableInt(environmentName);
        var value = environmentValue ?? profileValue;
        if (value is null || value <= 0 || (!profile.Enabled && environmentValue is null))
        {
            throw new PerformanceConfigurationException(
                $"Profile '{profileName}' is not approved yet. Supply a positive {environmentName} explicitly.");
        }

        return value.Value;
    }

    private static double ResolvePositiveDouble(
        PerformanceProfile profile,
        double? profileValue,
        string environmentName,
        string profileName)
    {
        var environmentValue = ReadNullableDouble(environmentName);
        var value = environmentValue ?? profileValue;
        if (value is null || value <= 0 || (!profile.Enabled && environmentValue is null))
        {
            throw new PerformanceConfigurationException(
                $"Profile '{profileName}' is not approved yet. Supply a positive {environmentName} explicitly.");
        }

        return value.Value;
    }

    private static int ResolveNonNegativeInt(int? profileValue, string environmentName, int fallback)
    {
        var value = ReadNullableInt(environmentName) ?? profileValue ?? fallback;
        if (value < 0)
            throw new PerformanceConfigurationException($"{environmentName} cannot be negative.");
        return value;
    }

    private static int? ResolveOptionalPositiveInt(int? profileValue, string environmentName)
    {
        var value = ReadNullableInt(environmentName) ?? profileValue;
        if (value.HasValue && value.Value <= 0)
            throw new PerformanceConfigurationException($"{environmentName} must be positive.");
        return value;
    }

    private static double? ResolveOptionalPositiveDouble(double? profileValue, string environmentName)
    {
        var value = ReadNullableDouble(environmentName) ?? profileValue;
        if (value.HasValue && value.Value <= 0)
            throw new PerformanceConfigurationException($"{environmentName} must be positive.");
        return value;
    }

    private static PerformanceThresholds ReadThresholds() => new(
        ReadNullableDouble("PERF_MAX_ERROR_RATE_PERCENT"),
        ReadNullableDouble("PERF_MAX_P95_MS"),
        ReadNullableDouble("PERF_MIN_THROUGHPUT_RPS"),
        ReadNullableDouble("PERF_MAX_FRESHNESS_LAG_MS"),
        ReadNullableLong("PERF_MAX_BACKLOG_COUNT"));

    private static void RequireExplicitExecution(IReadOnlyDictionary<string, string?> arguments)
    {
        if (arguments.ContainsKey("execute") || ReadBoolean("PERF_EXECUTE"))
            return;

        throw new PerformanceConfigurationException(
            "No traffic was sent. Pass --execute or PERF_EXECUTE=true to run intentionally.");
    }

    private static Dictionary<string, string?> ParseArguments(string[] args)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            var item = args[index];
            if (!item.StartsWith("--", StringComparison.Ordinal))
                throw new PerformanceConfigurationException($"Unknown argument '{item}'.");

            var key = item[2..];
            if (!SupportedArguments.Contains(key))
                throw new PerformanceConfigurationException($"Unknown argument '--{key}'.");
            if (!result.TryAdd(key, null))
                throw new PerformanceConfigurationException($"Argument '--{key}' was supplied more than once.");

            if (key.Equals("execute", StringComparison.OrdinalIgnoreCase))
                continue;

            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                throw new PerformanceConfigurationException($"Argument '--{key}' requires a value.");

            result[key] = args[++index];
        }

        return result;
    }

    private static string? Get(
        IReadOnlyDictionary<string, string?> arguments,
        string argumentName,
        string environmentName) =>
        arguments.TryGetValue(argumentName, out var value)
            ? value
            : Environment.GetEnvironmentVariable(environmentName);

    private static bool IsLoopback(Uri uri)
    {
        if (uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address);
    }

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        var builder = new UriBuilder(uri);
        if (!builder.Path.EndsWith('/'))
            builder.Path += "/";
        return builder.Uri;
    }

    private static void ValidateRunId(string runId)
    {
        if (runId.Length > 80 || runId.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
        {
            throw new PerformanceConfigurationException(
                "PERF_RUN_ID may contain at most 80 ASCII letters, digits, '.', '-' or '_'.");
        }
    }

    private static void ValidateTargetEnvironment(string targetEnvironment, bool loopback)
    {
        if (string.IsNullOrWhiteSpace(targetEnvironment))
        {
            throw new PerformanceConfigurationException(
                loopback
                    ? "PERF_TARGET_ENVIRONMENT cannot be empty."
                    : "PERF_TARGET_ENVIRONMENT is required for a non-loopback target.");
        }

        if (targetEnvironment.Equals("production", StringComparison.OrdinalIgnoreCase) ||
            targetEnvironment.Equals("prod", StringComparison.OrdinalIgnoreCase))
        {
            throw new PerformanceConfigurationException(
                "This runner refuses targets identified as production. Use an isolated performance environment.");
        }

        if (targetEnvironment.Length > 64 || targetEnvironment.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
        {
            throw new PerformanceConfigurationException(
                "PERF_TARGET_ENVIRONMENT may contain at most 64 ASCII letters, digits, '.', '-' or '_'.");
        }
    }

    private static void ValidateAccessToken(string? accessToken)
    {
        if (accessToken is null)
            return;
        if (string.IsNullOrWhiteSpace(accessToken) ||
            accessToken.Length > PerformanceLimits.MaximumAccessTokenLength ||
            accessToken.Any(char.IsWhiteSpace))
        {
            throw new PerformanceConfigurationException(
                $"PERF_ACCESS_TOKEN must be non-empty, contain no whitespace, and be at most {PerformanceLimits.MaximumAccessTokenLength} characters.");
        }
    }

    private static bool ReadBoolean(string name)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (raw is null)
            return false;
        if (bool.TryParse(raw, out var value))
            return value;
        throw new PerformanceConfigurationException($"{name} must be true or false.");
    }

    private static int? ReadNullableInt(string name)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;
        throw new PerformanceConfigurationException($"{name} must be an integer.");
    }

    private static long? ReadNullableLong(string name)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;
        throw new PerformanceConfigurationException($"{name} must be an integer.");
    }

    private static double? ReadNullableDouble(string name)
    {
        var raw = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
            double.IsFinite(value))
        {
            return value;
        }

        throw new PerformanceConfigurationException($"{name} must be a finite number.");
    }

    private static bool TryGet<T>(
        IReadOnlyDictionary<string, T> dictionary,
        string key,
        out T value)
    {
        foreach (var pair in dictionary)
        {
            if (pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                value = pair.Value;
                return true;
            }
        }

        value = default!;
        return false;
    }
}

internal static class PerformanceProfileValidator
{
    private static readonly HashSet<string> SupportedMethods =
        new(StringComparer.Ordinal) { "GET", "HEAD", "POST", "PUT", "PATCH", "DELETE" };

    public static PerformanceProfileDocument ReadDocument(string path)
    {
        if (!File.Exists(path))
            throw new PerformanceConfigurationException($"Performance profile file was not found: {path}");

        try
        {
            using var stream = File.OpenRead(path);
            var document = JsonSerializer.Deserialize<PerformanceProfileDocument>(
                stream,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                }) ?? throw new PerformanceConfigurationException("profiles.json is empty.");
            ValidateDocument(document);
            return document;
        }
        catch (JsonException exception)
        {
            throw new PerformanceConfigurationException(
                $"profiles.json is invalid: {exception.Message}");
        }
    }

    public static void ValidateDocument(PerformanceProfileDocument document)
    {
        if (document.Operations is null)
            throw new PerformanceConfigurationException("The operation catalog is required.");
        if (document.Operations.Count is < 2 or > PerformanceLimits.MaximumOperations)
        {
            throw new PerformanceConfigurationException(
                $"The operation catalog must contain between 2 and {PerformanceLimits.MaximumOperations} operations.");
        }

        EnsureCaseInsensitiveKeysAreUnique(document.Operations.Keys, "operation");
        foreach (var pair in document.Operations)
        {
            ValidateOperationName(pair.Key);
            if (pair.Value is null)
            {
                throw new PerformanceConfigurationException(
                    $"Operation '{pair.Key}' requires a definition.");
            }
            var operation = new PerformanceOperation(
                pair.Key,
                1,
                pair.Value.Path ?? string.Empty,
                ParseMethod(pair.Value.Method, pair.Key),
                pair.Value.Body,
                pair.Value.ContentType,
                pair.Value.RequiresAuthentication);
            ValidateOperation(operation);
        }

        if (document.Profiles is null || document.Profiles.Count == 0)
            throw new PerformanceConfigurationException("At least one profile is required.");
        EnsureCaseInsensitiveKeysAreUnique(document.Profiles.Keys, "profile");
        foreach (var pair in document.Profiles)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                throw new PerformanceConfigurationException("Profile names cannot be empty.");
            if (pair.Value is null)
            {
                throw new PerformanceConfigurationException(
                    $"Profile '{pair.Key}' requires a definition.");
            }
            _ = ParseArrivalModel(pair.Value.ArrivalModel ?? string.Empty);
            ValidateMix(
                document,
                pair.Key,
                pair.Value.Mix ?? throw new PerformanceConfigurationException(
                    $"Profile '{pair.Key}' requires a mix."));
        }
    }

    public static IReadOnlyList<PerformanceOperation> ResolveOperations(
        PerformanceProfileDocument document,
        PerformanceProfile profile)
    {
        var operations = new List<PerformanceOperation>(profile.Mix.Count);
        foreach (var entry in profile.Mix)
        {
            var operationName = entry.Operation!;
            var definition = document.Operations.First(pair =>
                pair.Key.Equals(operationName, StringComparison.OrdinalIgnoreCase));
            operations.Add(new PerformanceOperation(
                definition.Key,
                entry.Weight,
                definition.Value.Path!,
                ParseMethod(definition.Value.Method, definition.Key),
                definition.Value.Body,
                definition.Value.ContentType,
                definition.Value.RequiresAuthentication));
        }

        return operations;
    }

    public static void ValidateOperation(PerformanceOperation operation)
    {
        ValidateOperationName(operation.Name);

        if (!SupportedMethods.Contains(operation.Method.Method))
        {
            throw new PerformanceConfigurationException(
                $"Operation '{operation.Name}' uses unsupported method '{operation.Method.Method}'.");
        }

        if (operation.RequestPath.Length is < 1 or > PerformanceLimits.MaximumPathLength ||
            !operation.RequestPath.StartsWith("/", StringComparison.Ordinal) ||
            operation.RequestPath.StartsWith("//", StringComparison.Ordinal) ||
            operation.RequestPath.Contains('#') ||
            operation.RequestPath.Contains('\\') ||
            operation.RequestPath.Any(character => character is '\r' or '\n') ||
            !Uri.TryCreate(operation.RequestPath, UriKind.Relative, out _))
        {
            throw new PerformanceConfigurationException(
                $"Operation '{operation.Name}' must use an application-relative path of at most {PerformanceLimits.MaximumPathLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(operation.ContentType) ||
            operation.ContentType.Length > PerformanceLimits.MaximumContentTypeLength ||
            !operation.ContentType.All(char.IsAscii) ||
            !MediaTypeHeaderValue.TryParse(operation.ContentType, out var mediaType))
        {
            throw new PerformanceConfigurationException(
                $"Operation '{operation.Name}' has an invalid or oversized content type.");
        }

        if (operation.RequestBody is null)
            return;

        if (operation.Method == HttpMethod.Get || operation.Method == HttpMethod.Head)
        {
            throw new PerformanceConfigurationException(
                $"Operation '{operation.Name}' cannot attach a body to {operation.Method.Method}.");
        }

        if (Encoding.UTF8.GetByteCount(operation.RequestBody) > PerformanceLimits.MaximumBodyBytes)
        {
            throw new PerformanceConfigurationException(
                $"Operation '{operation.Name}' body exceeds {PerformanceLimits.MaximumBodyBytes} UTF-8 bytes.");
        }

        if (mediaType.MediaType?.Equals("application/json", StringComparison.OrdinalIgnoreCase) == true)
        {
            try
            {
                var validationBody = operation.RequestBody
                    .Replace("{runId}", "validation-run", StringComparison.Ordinal)
                    .Replace("{sequence1}", "1", StringComparison.Ordinal)
                    .Replace("{sequence}", "0", StringComparison.Ordinal);
                using var _ = JsonDocument.Parse(validationBody);
            }
            catch (JsonException exception)
            {
                throw new PerformanceConfigurationException(
                    $"Operation '{operation.Name}' body is not valid JSON: {exception.Message}");
            }
        }
    }

    public static ArrivalModelKind ParseArrivalModel(string value) => value switch
    {
        "paced-fixed-rate" => ArrivalModelKind.PacedFixedRate,
        "linear-concurrency-ramp-with-recovery" => ArrivalModelKind.LinearConcurrencyRampWithRecovery,
        "periodic-burst" => ArrivalModelKind.PeriodicBurst,
        _ => throw new PerformanceConfigurationException(
            "arrivalModel must be paced-fixed-rate, linear-concurrency-ramp-with-recovery, or periodic-burst."),
    };

    public static string ArrivalModelName(ArrivalModelKind value) => value switch
    {
        ArrivalModelKind.PacedFixedRate => "paced-fixed-rate",
        ArrivalModelKind.LinearConcurrencyRampWithRecovery => "linear-concurrency-ramp-with-recovery",
        ArrivalModelKind.PeriodicBurst => "periodic-burst",
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };

    private static void ValidateMix(
        PerformanceProfileDocument document,
        string profileName,
        IReadOnlyList<PerformanceMixEntry> mix)
    {
        if (mix.Count is < 2 or > PerformanceLimits.MaximumOperations)
        {
            throw new PerformanceConfigurationException(
                $"Profile '{profileName}' must declare between 2 and {PerformanceLimits.MaximumOperations} mix entries.");
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalWeight = 0;
        foreach (var entry in mix)
        {
            if (entry is null)
            {
                throw new PerformanceConfigurationException(
                    $"Profile '{profileName}' contains an empty mix entry.");
            }
            if (string.IsNullOrWhiteSpace(entry.Operation) ||
                !document.Operations.Keys.Any(name =>
                    name.Equals(entry.Operation, StringComparison.OrdinalIgnoreCase)))
            {
                throw new PerformanceConfigurationException(
                    $"Profile '{profileName}' references an unknown operation '{entry.Operation}'.");
            }

            if (!names.Add(entry.Operation))
            {
                throw new PerformanceConfigurationException(
                    $"Profile '{profileName}' references operation '{entry.Operation}' more than once.");
            }

            if (entry.Weight is < 1 or > PerformanceLimits.MaximumOperationWeight)
            {
                throw new PerformanceConfigurationException(
                    $"Profile '{profileName}' operation weights must be between 1 and {PerformanceLimits.MaximumOperationWeight}.");
            }

            totalWeight += entry.Weight;
        }

        if (totalWeight > PerformanceLimits.MaximumTotalWeight)
        {
            throw new PerformanceConfigurationException(
                $"Profile '{profileName}' total operation weight exceeds {PerformanceLimits.MaximumTotalWeight}.");
        }
    }

    private static HttpMethod ParseMethod(string method, string operationName)
    {
        if (string.IsNullOrWhiteSpace(method))
        {
            throw new PerformanceConfigurationException(
                $"Operation '{operationName}' requires an HTTP method.");
        }
        var normalized = method.Trim().ToUpperInvariant();
        if (!SupportedMethods.Contains(normalized))
        {
            throw new PerformanceConfigurationException(
                $"Operation '{operationName}' uses unsupported method '{method}'.");
        }

        return new HttpMethod(normalized);
    }

    private static void ValidateOperationName(string name)
    {
        if (name.Length is < 1 or > PerformanceLimits.MaximumOperationNameLength ||
            name.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.'))
        {
            throw new PerformanceConfigurationException(
                $"Operation names may contain at most {PerformanceLimits.MaximumOperationNameLength} ASCII letters, digits, '.', '-' or '_'.");
        }
    }

    private static void EnsureCaseInsensitiveKeysAreUnique(
        IEnumerable<string> keys,
        string kind)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            if (!seen.Add(key))
            {
                throw new PerformanceConfigurationException(
                    $"Duplicate {kind} name '{key}' differs only by case.");
            }
        }
    }
}

internal static class WeightedOperationSelector
{
    public static PerformanceOperation Select(
        IReadOnlyList<PerformanceOperation> operations,
        long sequence)
    {
        if (operations.Count == 0)
            throw new ArgumentException("At least one operation is required.", nameof(operations));
        if (sequence < 0)
            throw new ArgumentOutOfRangeException(nameof(sequence));

        var totalWeight = operations.Sum(operation => operation.Weight);
        var slot = sequence % totalWeight;
        foreach (var operation in operations)
        {
            if (slot < operation.Weight)
                return operation;
            slot -= operation.Weight;
        }

        throw new InvalidOperationException("The weighted operation mix is invalid.");
    }
}
