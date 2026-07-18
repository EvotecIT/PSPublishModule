using System;

namespace PowerForge;

internal sealed class ModuleStateVersionPolicy
{
    private ModuleStateVersionPolicy(
        ModuleStateVersion? exactVersion,
        ModuleStateVersion? minimumVersion,
        bool minimumInclusive,
        ModuleStateVersion? maximumVersion,
        bool maximumInclusive,
        bool allowPrerelease)
    {
        ExactVersion = exactVersion;
        MinimumVersion = minimumVersion;
        MinimumInclusive = minimumInclusive;
        MaximumVersion = maximumVersion;
        MaximumInclusive = maximumInclusive;
        AllowPrerelease = allowPrerelease;
    }

    internal ModuleStateVersion? ExactVersion { get; }

    internal ModuleStateVersion? MinimumVersion { get; }

    internal bool MinimumInclusive { get; }

    internal ModuleStateVersion? MaximumVersion { get; }

    internal bool MaximumInclusive { get; }

    internal bool AllowPrerelease { get; }

    internal static ModuleStateVersionPolicy Any(bool allowPrerelease = false)
        => new(null, null, true, null, true, allowPrerelease);

    internal static ModuleStateVersionPolicy Parse(string? expression, bool allowPrerelease = false)
        => FromManagedRange(ManagedModuleVersionSelector.ParseExpression(expression), allowPrerelease);

    private static ModuleStateVersionPolicy FromManagedRange(ManagedModuleVersionRange range, bool allowPrerelease)
    {
        if (range.IsUnbounded)
            return Any(allowPrerelease);

        if (range.ExactVersion is not null)
        {
            var exact = ModuleStateVersion.Parse(range.ExactVersion);
            return new ModuleStateVersionPolicy(exact, null, true, null, true, allowPrerelease || exact.IsPrerelease);
        }

        var minimum = string.IsNullOrWhiteSpace(range.MinimumVersion)
            ? (ModuleStateVersion?)null
            : ModuleStateVersion.Parse(range.MinimumVersion!);
        var maximum = string.IsNullOrWhiteSpace(range.MaximumVersion)
            ? (ModuleStateVersion?)null
            : ModuleStateVersion.Parse(range.MaximumVersion!);
        var effectiveAllowPrerelease = allowPrerelease ||
                                       range.AllowsPrerelease ||
                                       minimum is { IsPrerelease: true } ||
                                       maximum is { IsPrerelease: true };
        return new ModuleStateVersionPolicy(null, minimum, range.IncludeMinimum, maximum, range.IncludeMaximum, effectiveAllowPrerelease);
    }

    internal bool IsSatisfiedBy(string version)
    {
        if (!ModuleStateVersion.TryParse(version, out var parsed))
            return false;

        if (ExactVersion.HasValue)
            return parsed.Equals(ExactVersion.Value);

        if (parsed.IsPrerelease && !AllowPrerelease)
            return false;

        if (MinimumVersion.HasValue)
        {
            var minimumComparison = parsed.CompareTo(MinimumVersion.Value);
            if (minimumComparison < 0 || (minimumComparison == 0 && !MinimumInclusive))
                return false;
        }

        if (MaximumVersion.HasValue)
        {
            var maximumComparison = parsed.CompareTo(MaximumVersion.Value);
            if (maximumComparison > 0 || (maximumComparison == 0 && !MaximumInclusive))
                return false;
        }

        return true;
    }
}
