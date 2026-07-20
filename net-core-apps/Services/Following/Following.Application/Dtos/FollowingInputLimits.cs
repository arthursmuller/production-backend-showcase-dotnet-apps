using Following.Domain.Exceptions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Following.Application.Dtos;

internal static class FollowingInputLimits
{
    internal const int MaxBatchItems = 100;
    internal const int MaxCategoryNameLength = 128;

    internal static void RequirePositiveId(int id, string fieldName)
    {
        if (id <= 0)
            throw new FollowingException($"{fieldName} must be greater than zero.");
    }

    internal static void ValidateIdBatch(IReadOnlyCollection<int> ids, string fieldName)
    {
        if (ids is null)
            return;
        if (ids.Count > MaxBatchItems)
            throw new FollowingException($"{fieldName} accepts at most {MaxBatchItems} values.");
        if (ids.Any(id => id <= 0))
            throw new FollowingException($"Every {fieldName} value must be greater than zero.");
    }

    internal static void ValidateCategoryBatch(CheckCategoryFollowDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);
        dto.Ids ??= Array.Empty<int>();
        dto.Names ??= Array.Empty<string>();
        ValidateIdBatch(dto.Ids, nameof(dto.Ids));

        if (dto.Names.Length > MaxBatchItems
            || dto.Ids.Length + dto.Names.Length > MaxBatchItems)
        {
            throw new FollowingException(
                $"Category checks accept at most {MaxBatchItems} combined IDs and names.");
        }

        if (dto.Names.Any(name => string.IsNullOrWhiteSpace(name)
            || name.Length > MaxCategoryNameLength))
        {
            throw new FollowingException(
                $"Category names are required and cannot exceed {MaxCategoryNameLength} characters.");
        }
    }

    internal static void ValidateCategoryName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxCategoryNameLength)
        {
            throw new FollowingException(
                $"CategoryName is required and cannot exceed {MaxCategoryNameLength} characters.");
        }
    }
}
