namespace PowerForge;

/// <summary>
/// Evaluates PSResourceGet-style exact, wildcard, and NuGet range expressions against module versions.
/// </summary>
public static class ManagedModuleVersionSelector
{
    /// <summary>
    /// Tests whether a module version satisfies a PSResourceGet-style version expression.
    /// </summary>
    /// <param name="version">Module version to test.</param>
    /// <param name="expression">Exact version, wildcard version, or NuGet version range.</param>
    /// <returns><see langword="true"/> when the version satisfies the expression.</returns>
    public static bool IsMatch(string version, string? expression)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Version is required.", nameof(version));

        return ParseSearchExpression(expression).IsSatisfiedBy(version.Trim());
    }

    /// <summary>
    /// Tests whether an expression explicitly includes a prerelease version boundary.
    /// </summary>
    /// <param name="expression">Exact version, wildcard version, or NuGet version range.</param>
    /// <returns><see langword="true"/> when the expression contains a prerelease boundary.</returns>
    public static bool IncludesPrerelease(string? expression)
        => ParseSearchExpression(expression).AllowsPrerelease;

    private static ManagedModuleVersionRange ParseSearchExpression(string? expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return ManagedModuleVersionRange.Any;

        var trimmed = expression!.Trim();
        if (string.Equals(trimmed, "*", StringComparison.Ordinal))
            return ManagedModuleVersionRange.Any;

        if (TryConvertWildcardExpression(trimmed, out var wildcardRange))
            return ManagedModuleVersionRange.Parse(wildcardRange);

        if (HasRangeSyntax(trimmed))
            return ManagedModuleVersionRange.Parse(trimmed);

        // PSResourceGet treats a plain -Version value as exact. The shared range
        // parser intentionally keeps NuGet dependency semantics for plain values,
        // so search wraps exact values explicitly instead of changing that contract.
        return ManagedModuleVersionRange.Parse("[" + trimmed + "]");
    }

    private static bool HasRangeSyntax(string value)
        => value.StartsWith("[", StringComparison.Ordinal) ||
           value.StartsWith("(", StringComparison.Ordinal) ||
           value.StartsWith(">", StringComparison.Ordinal) ||
           value.StartsWith("<", StringComparison.Ordinal) ||
           value.StartsWith("=", StringComparison.Ordinal) ||
           value.EndsWith("]", StringComparison.Ordinal) ||
           value.EndsWith(")", StringComparison.Ordinal) ||
           value.IndexOf(",", StringComparison.Ordinal) >= 0;

    private static bool TryConvertWildcardExpression(string value, out string? range)
    {
        range = null;
        var parts = value.Split('.');
        if (parts.Length is < 2 or > 4 ||
            !string.Equals(parts[parts.Length - 1], "*", StringComparison.Ordinal))
        {
            return false;
        }

        var specified = new int[parts.Length - 1];
        for (var i = 0; i < specified.Length; i++)
        {
            if (!int.TryParse(parts[i], out var part) || part < 0)
                return false;

            specified[i] = part;
        }

        var segmentCount = Math.Max(3, specified.Length + 1);
        var lower = new int[segmentCount];
        var upper = new int[segmentCount];
        Array.Copy(specified, lower, specified.Length);
        Array.Copy(specified, upper, specified.Length);
        upper[specified.Length - 1]++;
        range = "[" + string.Join(".", lower) + "," + string.Join(".", upper) + ")";
        return true;
    }
}
