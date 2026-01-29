using System;

namespace PowerForge;

/// <summary>
/// Shared helper for stepping X-pattern versions (e.g., 1.2.X or 1.2.3.X).
/// </summary>
internal static class VersionPatternStepper
{
    internal static string Step(string expectedVersion, Version? currentVersion)
    {
        if (string.IsNullOrWhiteSpace(expectedVersion))
            throw new ArgumentException("ExpectedVersion is required.", nameof(expectedVersion));

        var parts = expectedVersion.Split('.');
        var segs = new string?[4];
        for (int i = 0; i < 4; i++)
            segs[i] = i < parts.Length ? parts[i] : null;

        var stepIndex = -1;
        for (int i = 0; i < segs.Length; i++)
        {
            if (string.Equals(segs[i], "X", StringComparison.OrdinalIgnoreCase))
            {
                stepIndex = i;
                break;
            }
        }

        if (stepIndex < 0)
            throw new ArgumentException("ExpectedVersion must contain an 'X' placeholder (or be an exact version).", nameof(expectedVersion));

        var prepared = new int?[4];
        for (int i = 0; i < segs.Length; i++)
        {
            var s = segs[i];
            if (string.IsNullOrWhiteSpace(s)) { prepared[i] = null; continue; }
            if (i == stepIndex) { prepared[i] = null; continue; }

            if (!int.TryParse(s, out var v))
                throw new ArgumentException($"ExpectedVersion segment '{s}' is not a number.", nameof(expectedVersion));
            prepared[i] = v;
        }

        var baseline = currentVersion ?? new Version(0, 0, 0, 0);
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
