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
        var normalizedVersion = NormalizeVersion(version, nameof(version));
        return ParseSearchExpression(expression).IsSatisfiedBy(normalizedVersion);
    }

    /// <summary>
    /// Tests whether repository version metadata is selectable for a PSResourceGet-style search expression.
    /// Unlisted versions remain selectable only when the expression is an exact version pin.
    /// </summary>
    /// <param name="version">Repository version metadata to test.</param>
    /// <param name="expression">Exact version, wildcard version, or NuGet version range.</param>
    /// <returns><see langword="true"/> when the version is both in range and eligible for selection.</returns>
    internal static bool IsSelectable(ManagedModuleVersionInfo version, string? expression)
    {
        if (version is null)
            throw new ArgumentNullException(nameof(version));

        return IsSelectable(version, ParseSearchExpression(expression));
    }

    /// <summary>
    /// Tests whether repository version metadata is selectable for an already parsed range.
    /// </summary>
    internal static bool IsSelectable(ManagedModuleVersionInfo version, ManagedModuleVersionRange range)
    {
        if (version is null || range is null)
            return false;

        var normalizedVersion = NormalizeVersion(version.Version, nameof(version));
        return range.IsSatisfiedBy(normalizedVersion) &&
               (range.ExactVersion is not null || version.Listed);
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

        if (trimmed.IndexOf('*') >= 0 || trimmed.IndexOf('?') >= 0)
            throw InvalidExpression(trimmed);

        if (HasRangeSyntax(trimmed))
        {
            ValidateRangeExpression(trimmed);
            return ManagedModuleVersionRange.Parse(trimmed);
        }

        // PSResourceGet treats a plain -Version value as exact. The shared range
        // parser intentionally keeps NuGet dependency semantics for plain values,
        // so search wraps exact values explicitly instead of changing that contract.
        ValidateVersion(trimmed, nameof(expression), trimmed);
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
        Array.Copy(specified, lower, specified.Length);
        range = "[" + string.Join(".", lower) + ",)";
        return true;
    }

    private static void ValidateRangeExpression(string expression)
    {
        if (expression.StartsWith("=", StringComparison.Ordinal))
        {
            ValidateVersion(expression.Substring(1), nameof(expression), expression);
            return;
        }

        if (expression.StartsWith(">", StringComparison.Ordinal) ||
            expression.StartsWith("<", StringComparison.Ordinal))
        {
            var tokens = expression.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                throw InvalidExpression(expression);

            foreach (var token in tokens)
            {
                var prefixLength = token.StartsWith(">=", StringComparison.Ordinal) ||
                                   token.StartsWith("<=", StringComparison.Ordinal)
                    ? 2
                    : token.StartsWith(">", StringComparison.Ordinal) ||
                      token.StartsWith("<", StringComparison.Ordinal)
                        ? 1
                        : 0;
                if (prefixLength == 0)
                    throw InvalidExpression(expression);

                var boundary = token.Substring(prefixLength);
                ValidateVersion(boundary, nameof(expression), expression);
            }

            var comparatorRange = ManagedModuleVersionRange.Parse(expression);
            ValidateBounds(
                comparatorRange.MinimumVersion,
                comparatorRange.IncludeMinimum,
                comparatorRange.MaximumVersion,
                comparatorRange.IncludeMaximum,
                expression);
            return;
        }

        var hasOpeningDelimiter = expression.StartsWith("[", StringComparison.Ordinal) ||
                                  expression.StartsWith("(", StringComparison.Ordinal);
        var hasClosingDelimiter = expression.EndsWith("]", StringComparison.Ordinal) ||
                                  expression.EndsWith(")", StringComparison.Ordinal);
        if (!hasOpeningDelimiter || !hasClosingDelimiter)
            throw InvalidExpression(expression);

        var body = expression.Substring(1, expression.Length - 2).Trim();
        var commaIndex = body.IndexOf(',');
        if (commaIndex < 0)
        {
            if (!expression.StartsWith("[", StringComparison.Ordinal) ||
                !expression.EndsWith("]", StringComparison.Ordinal))
            {
                throw InvalidExpression(expression);
            }

            ValidateVersion(body, nameof(expression), expression);
            return;
        }

        if (body.IndexOf(',', commaIndex + 1) >= 0)
            throw InvalidExpression(expression);

        var minimum = body.Substring(0, commaIndex).Trim();
        var maximum = body.Substring(commaIndex + 1).Trim();
        if (minimum.Length == 0 && maximum.Length == 0)
            throw InvalidExpression(expression);
        if (minimum.Length > 0)
            ValidateVersion(minimum, nameof(expression), expression);
        if (maximum.Length > 0)
            ValidateVersion(maximum, nameof(expression), expression);
        ValidateBounds(
            minimum,
            expression.StartsWith("[", StringComparison.Ordinal),
            maximum,
            expression.EndsWith("]", StringComparison.Ordinal),
            expression);
    }

    private static void ValidateBounds(
        string? minimum,
        bool includeMinimum,
        string? maximum,
        bool includeMaximum,
        string expression)
    {
        if (string.IsNullOrWhiteSpace(minimum) || string.IsNullOrWhiteSpace(maximum))
            return;

        var comparison = ManagedModuleVersionComparer.Instance.Compare(minimum, maximum);
        if (comparison > 0 || (comparison == 0 && (!includeMinimum || !includeMaximum)))
            throw InvalidExpression(expression);
    }

    private static string NormalizeVersion(string? version, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(version))
            throw new ArgumentException("Version is required.", parameterName);

        var normalizedVersion = version!.Trim();
        ValidateVersion(normalizedVersion, parameterName, version);
        return normalizedVersion;
    }

    private static void ValidateVersion(string value, string parameterName, string originalExpression)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw InvalidExpression(originalExpression, parameterName);

        var trimmed = value.Trim();
        var plusIndex = trimmed.IndexOf('+');
        if (plusIndex >= 0)
        {
            if (plusIndex == 0 || plusIndex == trimmed.Length - 1 || trimmed.IndexOf('+', plusIndex + 1) >= 0)
                throw InvalidExpression(originalExpression, parameterName);
            ValidateIdentifiers(trimmed.Substring(plusIndex + 1), originalExpression, parameterName);
            trimmed = trimmed.Substring(0, plusIndex);
        }

        var dashIndex = trimmed.IndexOf('-');
        if (dashIndex >= 0)
        {
            if (dashIndex == 0 || dashIndex == trimmed.Length - 1)
                throw InvalidExpression(originalExpression, parameterName);
            ValidateIdentifiers(trimmed.Substring(dashIndex + 1), originalExpression, parameterName);
            trimmed = trimmed.Substring(0, dashIndex);
        }

        var parts = trimmed.Split('.');
        if (parts.Length is < 1 or > 4)
            throw InvalidExpression(originalExpression, parameterName);
        foreach (var part in parts)
        {
            if (part.Length == 0 || !part.All(static character => character >= '0' && character <= '9') ||
                !int.TryParse(part, out _))
            {
                throw InvalidExpression(originalExpression, parameterName);
            }
        }
    }

    private static void ValidateIdentifiers(string value, string originalExpression, string parameterName)
    {
        foreach (var identifier in value.Split('.'))
        {
            if (identifier.Length == 0 || identifier.Any(static character =>
                    !(character >= '0' && character <= '9') &&
                    !(character >= 'A' && character <= 'Z') &&
                    !(character >= 'a' && character <= 'z') &&
                    character != '-'))
            {
                throw InvalidExpression(originalExpression, parameterName);
            }
        }
    }

    private static ArgumentException InvalidExpression(string expression, string parameterName = "expression")
        => new($"Version expression '{expression}' is invalid.", parameterName);
}
