using System;

namespace PowerForge;

/// <summary>
/// Shared helper for stepping X-pattern versions (e.g., 1.2.X or 1.2.3.X).
/// </summary>
internal static class VersionPatternStepper
{
    internal static string Step(string expectedVersion, Version? currentVersion)
    {
        var (prepared, stepIndex) = ParsePattern(expectedVersion);

        var baseline = currentVersion ?? new Version(0, 0, 0, 0);
        if (currentVersion is not null && CompareFixedPrefix(prepared, currentVersion, stepIndex) < 0)
        {
            throw new InvalidOperationException(
                $"ExpectedVersion pattern '{expectedVersion}' cannot produce a version greater than current version '{currentVersion}' because its fixed prefix is lower. " +
                "Choose a pattern with the same or a higher fixed prefix, or provide an exact version.");
        }

        var stepValue = currentVersion is null ? 1 : GetPart(currentVersion, stepIndex);
        if (stepValue < 0) stepValue = 0;

        prepared[stepIndex] = stepValue;

        var candidate = CreateVersion(prepared);
        if (candidate.CompareTo(baseline) > 0)
        {
            prepared[stepIndex] = 0;
            candidate = CreateVersion(prepared);
        }

        while (candidate.CompareTo(baseline) <= 0)
        {
            prepared[stepIndex] = (prepared[stepIndex] ?? 0) + 1;
            candidate = CreateVersion(prepared);
        }

        return candidate.ToString();
    }

    internal static bool CanRepresent(string expectedVersion, string exactVersion)
    {
        var (prepared, stepIndex) = ParsePattern(expectedVersion);
        if (!PackageVersionUtility.TryNormalizeExact(exactVersion, out var normalizedVersion))
            return false;

        var candidate = Version.Parse(PackageVersionUtility.GetNumericVersion(normalizedVersion));
        for (var index = 0; index < prepared.Length; index++)
        {
            if (index == stepIndex)
            {
                continue;
            }

            var actual = GetPart(candidate, index);
            if (actual < 0)
            {
                actual = 0;
            }
            if (prepared[index].HasValue)
            {
                if (prepared[index]!.Value != actual)
                {
                    return false;
                }
            }
            else if (actual != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static (int?[] Prepared, int StepIndex) ParsePattern(string expectedVersion)
    {
        if (string.IsNullOrWhiteSpace(expectedVersion))
        {
            throw new ArgumentException("ExpectedVersion is required.", nameof(expectedVersion));
        }

        var parts = expectedVersion.Split('.');
        if (parts.Length > 4)
        {
            throw new ArgumentException("ExpectedVersion cannot contain more than four numeric segments.", nameof(expectedVersion));
        }

        var segments = new string?[4];
        for (var index = 0; index < segments.Length; index++)
            segments[index] = index < parts.Length ? parts[index] : null;

        var stepIndex = -1;
        for (var index = 0; index < segments.Length; index++)
        {
            if (!string.Equals(segments[index], "X", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (stepIndex >= 0)
            {
                throw new ArgumentException("ExpectedVersion can contain only one 'X' placeholder.", nameof(expectedVersion));
            }
            stepIndex = index;
        }

        if (stepIndex < 0)
        {
            throw new ArgumentException("ExpectedVersion must contain an 'X' placeholder (or be an exact version).", nameof(expectedVersion));
        }

        var prepared = new int?[4];
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];
            if (string.IsNullOrWhiteSpace(segment) || index == stepIndex)
            {
                prepared[index] = null;
                continue;
            }

            if (!int.TryParse(segment, out var value))
            {
                throw new ArgumentException($"ExpectedVersion segment '{segment}' is not a number.", nameof(expectedVersion));
            }
            prepared[index] = value;
        }

        return (prepared, stepIndex);
    }

    private static int CompareFixedPrefix(int?[] prepared, Version currentVersion, int stepIndex)
    {
        for (var index = 0; index < stepIndex; index++)
        {
            var expectedPart = prepared[index] ?? 0;
            var currentPart = GetPart(currentVersion, index);
            var comparison = expectedPart.CompareTo(currentPart);
            if (comparison != 0)
                return comparison;
        }

        return 0;
    }

    private static int GetPart(Version v, int index)
        => index switch
        {
            0 => v.Major,
            1 => v.Minor,
            2 => v.Build,
            3 => v.Revision,
            _ => throw new ArgumentOutOfRangeException(nameof(index))
        };

    private static Version CreateVersion(int?[] parts)
    {
        if (parts is null) throw new ArgumentNullException(nameof(parts));

        var lastNonNull = -1;
        for (int i = 0; i < parts.Length; i++)
            if (parts[i].HasValue) lastNonNull = i;

        if (!parts[0].HasValue || !parts[1].HasValue)
            throw new InvalidOperationException("ExpectedVersion must include at least major and minor values.");

        var major = parts[0]!.Value;
        var minor = parts[1]!.Value;

        if (lastNonNull <= 1) return new Version(major, minor);

        var build = parts[2] ?? 0;
        if (lastNonNull == 2) return new Version(major, minor, build);

        var revision = parts[3] ?? 0;
        return new Version(major, minor, build, revision);
    }
}
