using Comment.Application.Dtos;
using Comment.Domain.AggregatesModel;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Comment.API.AppInterfaces.Routes
{
    /// <summary>
    /// Explicit query binder for bounded cursor-based comment lists. Minimal APIs
    /// do not recursively bind the nested Targets collection in the
    /// same way as MVC, so the route layer owns parsing and validation.
    /// </summary>
    public sealed class CommentsCriteriaQuery
    {
        private const int MaximumPageSize = 50;
        private const int MaximumFilterItems = 20;

        private static readonly Regex TargetPattern = new(
            @"^Targets\[(?<index>\d+)\]\.(?<field>TargetId|TargetType)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private CommentsCriteriaQuery(CommentsCriteria criteria) => Criteria = criteria;

        public CommentsCriteria Criteria { get; }

        public static ValueTask<CommentsCriteriaQuery> BindAsync(
            HttpContext context,
            ParameterInfo? _)
        {
            ArgumentNullException.ThrowIfNull(context);

            var query = context.Request.Query;
            var errors = new List<string>();
            var pageSize = ParseNullableInt(query, nameof(CommentsCriteria.PageSize), errors);
            var userId = ParseNullableInt(query, nameof(CommentsCriteria.UserId), errors);

            foreach (var unsupported in new[] { "PageNumber", "Search", "SortBy" })
                if (query.ContainsKey(unsupported))
                    errors.Add($"{unsupported} is not supported by cursor pagination.");
            foreach (var key in query.Keys)
            {
                if (!IsSupportedQueryKey(key) &&
                    !new[] { "PageNumber", "Search", "SortBy" }.Contains(
                        key,
                        StringComparer.OrdinalIgnoreCase))
                    errors.Add($"Query parameter '{key}' is not supported.");
            }

            var ids = ParseIntegerArray(query, nameof(CommentsCriteria.Ids), errors);
            var dateRange = ParseDateRange(query, nameof(CommentsCriteria.DateRange), errors);
            var targets = ParseTargets(query, errors);
            return ValueTask.FromResult(Create(new CommentsCriteria
            {
                PageSize = pageSize,
                DateRange = dateRange,
                Ids = ids,
                UserId = userId,
                Targets = targets,
            }, errors));
        }

        private static CommentsCriteriaQuery Create(
            CommentsCriteria source,
            List<string> errors)
        {
            if (source.PageSize is <= 0 or > MaximumPageSize)
                errors.Add($"PageSize must be between 1 and {MaximumPageSize}.");
            if (source.UserId is <= 0)
                errors.Add("UserId must be greater than zero.");

            var ids = NormalizeIds(source.Ids, errors);
            var dateRange = NormalizeDateRange(source.DateRange, errors);
            var targets = NormalizeTargets(source.Targets, errors);

            if (errors.Count > 0)
                throw new BadHttpRequestException(
                    string.Join(" ", errors.Distinct(StringComparer.Ordinal)),
                    StatusCodes.Status400BadRequest);

            return new CommentsCriteriaQuery(new CommentsCriteria
            {
                PageSize = source.PageSize,
                DateRange = dateRange,
                Ids = ids,
                UserId = source.UserId,
                Targets = targets,
            });
        }

        private static int[]? NormalizeIds(int[]? values, ICollection<string> errors)
        {
            if (values is null)
                return null;
            if (values.Length > MaximumFilterItems)
                errors.Add($"Ids cannot contain more than {MaximumFilterItems} values.");
            if (values.Any(value => value <= 0))
                errors.Add("Ids values must be positive integers.");
            return values.Where(value => value > 0).Distinct().ToArray();
        }

        private static DateTime?[]? NormalizeDateRange(
            DateTime?[]? values,
            ICollection<string> errors)
        {
            if (values is null)
                return null;
            if (values.Length > 2)
                errors.Add("DateRange accepts at most a start and end value.");
            if (values.Length > 1 &&
                values[0] is { } start &&
                values[1] is { } end &&
                start > end)
                errors.Add("DateRange start cannot be later than its end.");
            return values.ToArray();
        }

        private static CommentTargetFilter[]? NormalizeTargets(
            CommentTargetFilter[]? values,
            ICollection<string> errors)
        {
            if (values is null)
                return null;
            if (values.Length > MaximumFilterItems)
                errors.Add(
                    $"Targets cannot contain more than {MaximumFilterItems} values.");

            var normalized = new List<CommentTargetFilter>(values.Length);
            for (var index = 0; index < values.Length; index++)
            {
                var value = values[index];
                if (value is null ||
                    string.IsNullOrWhiteSpace(value.TargetId) ||
                    string.IsNullOrWhiteSpace(value.TargetType))
                {
                    errors.Add($"Targets[{index}] requires both TargetId and TargetType.");
                    continue;
                }

                var targetId = value.TargetId.Trim();
                var targetType = value.TargetType.Trim();
                if (targetId.Length > CommentFieldLimits.TargetId)
                {
                    errors.Add(
                        $"Targets[{index}].TargetId cannot exceed {CommentFieldLimits.TargetId} characters.");
                    continue;
                }
                if (targetType.Length > CommentFieldLimits.TargetType)
                {
                    errors.Add(
                        $"Targets[{index}].TargetType cannot exceed {CommentFieldLimits.TargetType} characters.");
                    continue;
                }

                normalized.Add(new CommentTargetFilter
                {
                    TargetId = targetId,
                    TargetType = targetType.ToLowerInvariant(),
                });
            }

            return normalized.ToArray();
        }

        private static int? ParseNullableInt(
            IQueryCollection query,
            string key,
            ICollection<string> errors)
        {
            var raw = GetScalar(query, key, errors);
            if (string.IsNullOrWhiteSpace(raw))
                return null;
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                return value;

            errors.Add($"{key} must be a valid integer.");
            return null;
        }

        private static int[]? ParseIntegerArray(
            IQueryCollection query,
            string key,
            ICollection<string> errors)
        {
            var values = GetArrayValues(query, key);
            if (values.Count == 0)
                return null;
            if (values.Count > MaximumFilterItems)
            {
                errors.Add($"{key} cannot contain more than {MaximumFilterItems} values.");
                return null;
            }

            var parsed = new List<int>(values.Count);
            foreach (var raw in values)
            {
                if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) &&
                    value > 0)
                    parsed.Add(value);
                else
                    errors.Add($"{key} values must be positive integers.");
            }

            return parsed.Distinct().ToArray();
        }

        private static DateTime?[]? ParseDateRange(
            IQueryCollection query,
            string key,
            ICollection<string> errors)
        {
            var values = GetArrayValues(query, key);
            if (values.Count == 0)
                return null;
            if (values.Count > 2)
            {
                errors.Add("DateRange accepts at most a start and end value.");
                return null;
            }

            var parsed = new DateTime?[values.Count];
            for (var index = 0; index < values.Count; index++)
            {
                if (DateTime.TryParse(
                    values[index],
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind,
                    out var value))
                {
                    parsed[index] = value;
                }
                else
                {
                    errors.Add("DateRange values must use an invariant ISO-8601 date/time.");
                }
            }

            return parsed;
        }

        private static CommentTargetFilter[]? ParseTargets(
            IQueryCollection query,
            ICollection<string> errors)
        {
            var indexed = new SortedDictionary<int, TargetBuilder>();
            foreach (var item in query)
            {
                var match = TargetPattern.Match(item.Key);
                if (!match.Success)
                    continue;

                if (!int.TryParse(
                    match.Groups["index"].Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var index))
                {
                    errors.Add("Targets contains an invalid index.");
                    continue;
                }

                if (!indexed.TryGetValue(index, out var builder))
                {
                    builder = new TargetBuilder();
                    indexed[index] = builder;
                }

                var values = item.Value
                    .OfType<string>()
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Select(value => value.Trim())
                    .ToArray();
                if (values.Length != 1)
                {
                    errors.Add($"{item.Key} must have exactly one value.");
                    continue;
                }

                if (match.Groups["field"].Value.Equals("TargetId", StringComparison.OrdinalIgnoreCase))
                    builder.TargetId = values[0];
                else
                    builder.TargetType = values[0];
            }

            if (indexed.Count == 0)
                return null;
            if (indexed.Count > MaximumFilterItems)
            {
                errors.Add($"Targets cannot contain more than {MaximumFilterItems} values.");
                return null;
            }

            var result = new List<CommentTargetFilter>(indexed.Count);
            foreach (var item in indexed)
            {
                var targetId = item.Value.TargetId?.Trim();
                var targetType = item.Value.TargetType?.Trim();
                if (string.IsNullOrWhiteSpace(targetId) ||
                    string.IsNullOrWhiteSpace(targetType))
                {
                    errors.Add($"Targets[{item.Key}] requires both TargetId and TargetType.");
                    continue;
                }
                if (targetId.Length > CommentFieldLimits.TargetId)
                {
                    errors.Add(
                        $"Targets[{item.Key}].TargetId cannot exceed {CommentFieldLimits.TargetId} characters.");
                    continue;
                }
                if (targetType.Length > CommentFieldLimits.TargetType)
                {
                    errors.Add(
                        $"Targets[{item.Key}].TargetType cannot exceed {CommentFieldLimits.TargetType} characters.");
                    continue;
                }

                result.Add(new CommentTargetFilter
                {
                    TargetId = targetId,
                    TargetType = targetType.ToLowerInvariant(),
                });
            }

            return result.ToArray();
        }

        private static string? GetScalar(
            IQueryCollection query,
            string key,
            ICollection<string> errors)
        {
            if (!query.TryGetValue(key, out var values) || values.Count == 0)
                return null;
            if (values.Count > 1)
                errors.Add($"{key} must have only one value.");
            return values[0];
        }

        private static bool IsSupportedQueryKey(string key) =>
            key.Equals(nameof(CommentsCriteria.PageSize), StringComparison.OrdinalIgnoreCase) ||
            key.Equals(nameof(CommentsCriteria.UserId), StringComparison.OrdinalIgnoreCase) ||
            key.Equals(nameof(CommentsCriteria.Ids), StringComparison.OrdinalIgnoreCase) ||
            key.Equals(nameof(CommentsCriteria.DateRange), StringComparison.OrdinalIgnoreCase) ||
            key.Equals("cursor", StringComparison.OrdinalIgnoreCase) ||
            IsIndexedKey(key, nameof(CommentsCriteria.Ids)) ||
            IsIndexedKey(key, nameof(CommentsCriteria.DateRange)) ||
            TargetPattern.IsMatch(key);

        private static bool IsIndexedKey(string key, string prefix)
        {
            if (!key.StartsWith($"{prefix}[", StringComparison.OrdinalIgnoreCase) ||
                !key.EndsWith("]", StringComparison.Ordinal))
                return false;

            var indexText = key.Substring(prefix.Length + 1, key.Length - prefix.Length - 2);
            return int.TryParse(
                indexText,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out _);
        }

        private static IReadOnlyList<string> GetArrayValues(
            IQueryCollection query,
            string key,
            bool splitCommaSeparated = true)
        {
            var values = new List<string>();
            var indexedValues = new SortedDictionary<int, List<string>>();
            foreach (var item in query)
            {
                var isExact = item.Key.Equals(key, StringComparison.OrdinalIgnoreCase);
                if (isExact)
                {
                    AppendValues(values, item.Value, splitCommaSeparated);
                    continue;
                }

                if (!item.Key.StartsWith($"{key}[", StringComparison.OrdinalIgnoreCase)
                    || !item.Key.EndsWith("]", StringComparison.Ordinal))
                    continue;

                var indexText = item.Key.Substring(key.Length + 1, item.Key.Length - key.Length - 2);
                if (!int.TryParse(
                    indexText,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var index))
                    continue;

                if (!indexedValues.TryGetValue(index, out var indexed))
                {
                    indexed = new List<string>();
                    indexedValues[index] = indexed;
                }

                AppendValues(indexed, item.Value, splitCommaSeparated);
            }

            foreach (var indexed in indexedValues.Values)
                values.AddRange(indexed);

            return values;
        }

        private static void AppendValues(
            ICollection<string> destination,
            IEnumerable<string?> source,
            bool splitCommaSeparated)
        {
            foreach (var raw in source)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;
                if (!splitCommaSeparated)
                {
                    destination.Add(raw.Trim());
                    continue;
                }

                foreach (var value in raw.Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    destination.Add(value);
            }
        }

        private sealed class TargetBuilder
        {
            public string? TargetId { get; set; }
            public string? TargetType { get; set; }
        }
    }
}
