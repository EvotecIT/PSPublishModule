namespace PowerForge;

internal sealed class ManagedModuleVersionRange
{
    private ManagedModuleVersionRange(
        string? minimumVersion,
        bool includeMinimum,
        string? maximumVersion,
        bool includeMaximum,
        string? exactVersion,
        bool allowsPrerelease)
    {
        MinimumVersion = minimumVersion;
        IncludeMinimum = includeMinimum;
        MaximumVersion = maximumVersion;
        IncludeMaximum = includeMaximum;
        ExactVersion = exactVersion;
        AllowsPrerelease = allowsPrerelease;
    }

    public string? MinimumVersion { get; }

    public bool IncludeMinimum { get; }

    public string? MaximumVersion { get; }

    public bool IncludeMaximum { get; }

    public string? ExactVersion { get; }

    public bool AllowsPrerelease { get; }

    public bool IsUnbounded => ExactVersion is null && MinimumVersion is null && MaximumVersion is null;

    public static ManagedModuleVersionRange Any { get; } = new(null, false, null, false, null, false);

    public static ManagedModuleVersionRange FromBounds(string? minimumVersion, string? maximumVersion)
        => string.IsNullOrWhiteSpace(minimumVersion) && string.IsNullOrWhiteSpace(maximumVersion)
            ? Any
            : new ManagedModuleVersionRange(
                Normalize(minimumVersion),
                includeMinimum: true,
                Normalize(maximumVersion),
                includeMaximum: true,
                exactVersion: null,
                allowsPrerelease: ManagedModuleVersionComparer.IsPrerelease(minimumVersion ?? string.Empty) ||
                                   ManagedModuleVersionComparer.IsPrerelease(maximumVersion ?? string.Empty));

    public static ManagedModuleVersionRange Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Any;

        var trimmed = value!.Trim();
        var allowsPrerelease = ManagedModuleVersionComparer.IsPrerelease(trimmed);
        if (!HasRangeDelimiters(trimmed))
            return new ManagedModuleVersionRange(trimmed, includeMinimum: true, null, false, null, allowsPrerelease);

        var includeMinimum = trimmed.StartsWith("[", StringComparison.Ordinal);
        var includeMaximum = trimmed.EndsWith("]", StringComparison.Ordinal);
        var body = trimmed.Trim('[', ']', '(', ')').Trim();
        if (!body.Contains(",", StringComparison.Ordinal))
            return new ManagedModuleVersionRange(null, false, null, false, body, allowsPrerelease);

        var parts = body.Split(new[] { ',' }, 2);
        var minimum = Normalize(parts[0]);
        var maximum = parts.Length > 1 ? Normalize(parts[1]) : null;
        return new ManagedModuleVersionRange(minimum, includeMinimum, maximum, includeMaximum, null, allowsPrerelease);
    }

    public bool IsSatisfiedBy(string version)
    {
        if (ExactVersion is not null)
            return ManagedModuleVersionComparer.Instance.Compare(version, ExactVersion) == 0;

        if (MinimumVersion is not null)
        {
            var comparison = ManagedModuleVersionComparer.Instance.Compare(version, MinimumVersion);
            if (comparison < 0 || (comparison == 0 && !IncludeMinimum))
                return false;
        }

        if (MaximumVersion is not null)
        {
            var comparison = ManagedModuleVersionComparer.Instance.Compare(version, MaximumVersion);
            if (comparison > 0 || (comparison == 0 && !IncludeMaximum))
                return false;
        }

        return true;
    }

    public override string ToString()
    {
        if (ExactVersion is not null)
            return "[" + ExactVersion + "]";
        if (MinimumVersion is null && MaximumVersion is null)
            return "*";
        if (MaximumVersion is null)
            return (IncludeMinimum ? "[" : "(") + MinimumVersion + ",)";
        if (MinimumVersion is null)
            return "(," + MaximumVersion + (IncludeMaximum ? "]" : ")");

        return (IncludeMinimum ? "[" : "(") + MinimumVersion + "," + MaximumVersion + (IncludeMaximum ? "]" : ")");
    }

    private static bool HasRangeDelimiters(string value)
        => value.StartsWith("[", StringComparison.Ordinal) ||
           value.StartsWith("(", StringComparison.Ordinal) ||
           value.EndsWith("]", StringComparison.Ordinal) ||
           value.EndsWith(")", StringComparison.Ordinal);

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
