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
    {
        if (expression is null)
            return Any(allowPrerelease);

        var trimmedExpression = expression.Trim();
        if (trimmedExpression.Length == 0 || string.Equals(trimmedExpression, "*", StringComparison.Ordinal))
            return Any(allowPrerelease);

        ModuleStateVersion? exact = null;
        ModuleStateVersion? minimum = null;
        ModuleStateVersion? maximum = null;
        var minimumInclusive = true;
        var maximumInclusive = true;

        foreach (var token in trimmedExpression.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.StartsWith(">=", StringComparison.Ordinal))
            {
                minimum = ModuleStateVersion.Parse(token.Substring(2));
                minimumInclusive = true;
            }
            else if (token.StartsWith(">", StringComparison.Ordinal))
            {
                minimum = ModuleStateVersion.Parse(token.Substring(1));
                minimumInclusive = false;
            }
            else if (token.StartsWith("<=", StringComparison.Ordinal))
            {
                maximum = ModuleStateVersion.Parse(token.Substring(2));
                maximumInclusive = true;
            }
            else if (token.StartsWith("<", StringComparison.Ordinal))
            {
                maximum = ModuleStateVersion.Parse(token.Substring(1));
                maximumInclusive = false;
            }
            else if (token.StartsWith("=", StringComparison.Ordinal))
            {
                exact = ModuleStateVersion.Parse(token.Substring(1));
            }
            else
            {
                exact = ModuleStateVersion.Parse(token);
            }
        }

        if (exact.HasValue && (minimum.HasValue || maximum.HasValue))
            throw new ArgumentException("Exact module version policy cannot be combined with range constraints.", nameof(expression));

        var effectiveAllowPrerelease = allowPrerelease ||
                                       exact is { IsPrerelease: true } ||
                                       minimum is { IsPrerelease: true } ||
                                       maximum is { IsPrerelease: true };

        return new ModuleStateVersionPolicy(exact, minimum, minimumInclusive, maximum, maximumInclusive, effectiveAllowPrerelease);
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
