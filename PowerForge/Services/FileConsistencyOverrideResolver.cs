using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Resolves per-path file consistency overrides (encoding) based on simple patterns.
/// </summary>
public static class FileConsistencyOverrideResolver
{
    /// <summary>
    /// Determines whether a pattern matches the provided relative path.
    /// </summary>
    public static bool Matches(string pattern, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        if (string.IsNullOrWhiteSpace(relativePath)) return false;

        var normalizedPath = NormalizePath(relativePath);
        var fileName = Path.GetFileName(relativePath);
        var extension = Path.GetExtension(relativePath);
        return IsMatch(pattern.Trim(), normalizedPath, fileName, extension);
    }

    /// <summary>
    /// Resolves an encoding override for a relative path using the provided pattern map.
    /// </summary>
    public static FileConsistencyEncoding? ResolveEncodingOverride(
        string relativePath,
        IReadOnlyDictionary<string, FileConsistencyEncoding>? overrides)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        if (overrides is null || overrides.Count == 0) return null;

        var normalizedPath = NormalizePath(relativePath);
        var fileName = Path.GetFileName(relativePath);
        var extension = Path.GetExtension(relativePath);

        FileConsistencyEncoding? best = null;
        var bestScore = int.MinValue;

        foreach (var entry in overrides)
        {
            var pattern = entry.Key;
            if (string.IsNullOrWhiteSpace(pattern)) continue;
            var trimmed = pattern.Trim();
            if (!IsMatch(trimmed, normalizedPath, fileName, extension)) continue;

            var score = ScorePattern(trimmed);
            if (score > bestScore)
            {
                bestScore = score;
                best = entry.Value;
            }
        }

        return best;
    }

    /// <summary>
    /// Resolves a line ending override for a relative path using the provided pattern map.
    /// </summary>
    public static FileConsistencyLineEnding? ResolveLineEndingOverride(
        string relativePath,
        IReadOnlyDictionary<string, FileConsistencyLineEnding>? overrides)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return null;
        if (overrides is null || overrides.Count == 0) return null;

        var normalizedPath = NormalizePath(relativePath);
        var fileName = Path.GetFileName(relativePath);
        var extension = Path.GetExtension(relativePath);

        FileConsistencyLineEnding? best = null;
        var bestScore = int.MinValue;

        foreach (var entry in overrides)
        {
            var pattern = entry.Key;
            if (string.IsNullOrWhiteSpace(pattern)) continue;
            var trimmed = pattern.Trim();
            if (!IsMatch(trimmed, normalizedPath, fileName, extension)) continue;

            var score = ScorePattern(trimmed);
            if (score > bestScore)
            {
                bestScore = score;
                best = entry.Value;
            }
        }

        return best;
    }

    /// <summary>
    /// Resolves the expected encoding for a relative path, falling back to <paramref name="defaultEncoding"/>.
    /// </summary>
    public static TextEncodingKind ResolveExpectedEncoding(
        string relativePath,
        TextEncodingKind defaultEncoding,
        IReadOnlyDictionary<string, FileConsistencyEncoding>? overrides)        
    {
        var match = ResolveEncodingOverride(relativePath, overrides);
        return match.HasValue ? match.Value.ToTextEncodingKind() : defaultEncoding;
    }

    /// <summary>
    /// Resolves the expected line ending for a relative path, falling back to <paramref name="defaultLineEnding"/>.
    /// </summary>
    public static FileConsistencyLineEnding ResolveExpectedLineEnding(
        string relativePath,
        FileConsistencyLineEnding defaultLineEnding,
        IReadOnlyDictionary<string, FileConsistencyLineEnding>? overrides)
    {
        var match = ResolveLineEndingOverride(relativePath, overrides);
        return match ?? defaultLineEnding;
    }

    private static bool IsMatch(string pattern, string normalizedPath, string fileName, string extension)
    {
        var trimmed = pattern.Trim();
        if (IsExtensionPattern(trimmed))
        {
            var ext = NormalizeExtensionPattern(trimmed);
            return !string.IsNullOrWhiteSpace(extension) && extension.Equals(ext, StringComparison.OrdinalIgnoreCase);
        }

        var hasWildcard = ContainsWildcard(trimmed);
        var hasSeparator = HasPathSeparator(trimmed);

        if (!hasWildcard)
        {
            if (hasSeparator)
                return string.Equals(NormalizePath(trimmed), normalizedPath, StringComparison.OrdinalIgnoreCase);

            return string.Equals(trimmed, fileName, StringComparison.OrdinalIgnoreCase);
        }

        if (hasSeparator)
            return MatchesWildcard(NormalizePath(trimmed), normalizedPath);

        return MatchesWildcard(trimmed, fileName);
    }

    private static int ScorePattern(string pattern)
    {
        var trimmed = pattern.Trim();
        var hasWildcard = ContainsWildcard(trimmed);
        var hasSeparator = HasPathSeparator(trimmed);

        if (IsExtensionPattern(trimmed)) return 100 + trimmed.Length;
        if (!hasWildcard && hasSeparator) return 1000 + trimmed.Length;
        if (!hasWildcard) return 900 + trimmed.Length;
        if (hasSeparator) return 800 + trimmed.Length;
        return 700 + trimmed.Length;
    }

    private static bool IsExtensionPattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        if (HasPathSeparator(pattern)) return false;

        if (ContainsWildcard(pattern))
        {
            return pattern.StartsWith("*.", StringComparison.Ordinal) &&
                   pattern.IndexOfAny(new[] { '*', '?' }, 2) < 0;
        }

        return pattern.StartsWith(".", StringComparison.Ordinal);
    }

    private static string NormalizeExtensionPattern(string pattern)
    {
        var trimmed = pattern.Trim();
        if (trimmed.StartsWith("*.", StringComparison.Ordinal)) trimmed = trimmed.Substring(1);
        if (!trimmed.StartsWith(".", StringComparison.Ordinal)) trimmed = "." + trimmed;
        return trimmed;
    }

    private static bool ContainsWildcard(string pattern)
        => pattern.IndexOf('*') >= 0 || pattern.IndexOf('?') >= 0;

    private static bool HasPathSeparator(string pattern)
        => pattern.IndexOf('/') >= 0 || pattern.IndexOf('\\') >= 0;

    private static string NormalizePath(string path)
        => path.Replace('\\', '/');

    private static bool MatchesWildcard(string pattern, string candidate)
    {
        var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return Regex.IsMatch(candidate, regex, RegexOptions.IgnoreCase);
    }
}
