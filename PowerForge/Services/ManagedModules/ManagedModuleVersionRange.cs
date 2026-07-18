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
        if (trimmed.StartsWith("=", StringComparison.Ordinal))
        {
            var exact = Normalize(trimmed.Substring(1));
            return string.IsNullOrWhiteSpace(exact)
                ? Any
                : new ManagedModuleVersionRange(null, false, null, false, exact, ManagedModuleVersionComparer.IsPrerelease(exact!));
        }

        if (TryParseComparatorRange(trimmed, out var comparatorRange))
            return comparatorRange;

        if (!HasRangeDelimiters(trimmed))
            return new ManagedModuleVersionRange(
                trimmed,
                includeMinimum: true,
                null,
                false,
                null,
                ManagedModuleVersionComparer.IsPrerelease(trimmed));

        var includeMinimum = trimmed.StartsWith("[", StringComparison.Ordinal);
        var includeMaximum = trimmed.EndsWith("]", StringComparison.Ordinal);
        var body = trimmed.Trim('[', ']', '(', ')').Trim();
        if (!body.Contains(",", StringComparison.Ordinal))
            return new ManagedModuleVersionRange(
                null,
                false,
                null,
                false,
                body,
                ManagedModuleVersionComparer.IsPrerelease(body));

        var parts = body.Split(new[] { ',' }, 2);
        var minimum = Normalize(parts[0]);
        var maximum = parts.Length > 1 ? Normalize(parts[1]) : null;
        return new ManagedModuleVersionRange(
            minimum,
            includeMinimum,
            maximum,
            includeMaximum,
            null,
            ManagedModuleVersionComparer.IsPrerelease(minimum ?? string.Empty) ||
            ManagedModuleVersionComparer.IsPrerelease(maximum ?? string.Empty));
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

    private static bool TryParseComparatorRange(
        string value,
        out ManagedModuleVersionRange range)
    {
        var minimumVersion = default(string);
        var maximumVersion = default(string);
        var includeMinimum = false;
        var includeMaximum = false;
        var allowsPrerelease = false;
        var parsedAny = false;

        foreach (var rawToken in value.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = rawToken.Trim();
            if (token.StartsWith(">=", StringComparison.Ordinal))
            {
                var candidateVersion = Normalize(token.Substring(2));
                allowsPrerelease |= ManagedModuleVersionComparer.IsPrerelease(candidateVersion ?? string.Empty);
                ApplyMinimum(
                    ref minimumVersion,
                    ref includeMinimum,
                    candidateVersion,
                    includeCandidate: true);
                parsedAny = true;
                continue;
            }

            if (token.StartsWith(">", StringComparison.Ordinal))
            {
                var candidateVersion = Normalize(token.Substring(1));
                allowsPrerelease |= ManagedModuleVersionComparer.IsPrerelease(candidateVersion ?? string.Empty);
                ApplyMinimum(
                    ref minimumVersion,
                    ref includeMinimum,
                    candidateVersion,
                    includeCandidate: false);
                parsedAny = true;
                continue;
            }

            if (token.StartsWith("<=", StringComparison.Ordinal))
            {
                var candidateVersion = Normalize(token.Substring(2));
                allowsPrerelease |= ManagedModuleVersionComparer.IsPrerelease(candidateVersion ?? string.Empty);
                ApplyMaximum(
                    ref maximumVersion,
                    ref includeMaximum,
                    candidateVersion,
                    includeCandidate: true);
                parsedAny = true;
                continue;
            }

            if (token.StartsWith("<", StringComparison.Ordinal))
            {
                var candidateVersion = Normalize(token.Substring(1));
                allowsPrerelease |= ManagedModuleVersionComparer.IsPrerelease(candidateVersion ?? string.Empty);
                ApplyMaximum(
                    ref maximumVersion,
                    ref includeMaximum,
                    candidateVersion,
                    includeCandidate: false);
                parsedAny = true;
                continue;
            }

            if (parsedAny)
            {
                range = Any;
                return false;
            }
        }

        if (!parsedAny)
        {
            range = Any;
            return false;
        }

        range = new ManagedModuleVersionRange(
            minimumVersion,
            includeMinimum,
            maximumVersion,
            includeMaximum,
            null,
            allowsPrerelease);
        return true;
    }

    private static void ApplyMinimum(
        ref string? currentVersion,
        ref bool includeCurrent,
        string? candidateVersion,
        bool includeCandidate)
    {
        if (candidateVersion is null)
            return;
        if (currentVersion is null)
        {
            currentVersion = candidateVersion;
            includeCurrent = includeCandidate;
            return;
        }

        var comparison = ManagedModuleVersionComparer.Instance.Compare(candidateVersion, currentVersion);
        if (comparison > 0)
        {
            currentVersion = candidateVersion;
            includeCurrent = includeCandidate;
        }
        else if (comparison == 0)
        {
            includeCurrent = includeCurrent && includeCandidate;
        }
    }

    private static void ApplyMaximum(
        ref string? currentVersion,
        ref bool includeCurrent,
        string? candidateVersion,
        bool includeCandidate)
    {
        if (candidateVersion is null)
            return;
        if (currentVersion is null)
        {
            currentVersion = candidateVersion;
            includeCurrent = includeCandidate;
            return;
        }

        var comparison = ManagedModuleVersionComparer.Instance.Compare(candidateVersion, currentVersion);
        if (comparison < 0)
        {
            currentVersion = candidateVersion;
            includeCurrent = includeCandidate;
        }
        else if (comparison == 0)
        {
            includeCurrent = includeCurrent && includeCandidate;
        }
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
