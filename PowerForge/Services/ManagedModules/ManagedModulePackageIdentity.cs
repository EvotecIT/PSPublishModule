namespace PowerForge;

internal static class ManagedModulePackageIdentity
{
    public static string RequireSafeId(string value, string argumentName)
        => RequireSafePathSegment(value, argumentName, "package id");

    public static string RequireSafeVersion(string value, string argumentName)
        => RequireSafePathSegment(value, argumentName, "package version");

    private static string RequireSafePathSegment(string value, string argumentName, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"{label} is required.", argumentName);

        var trimmed = value.Trim();
        if (trimmed.Equals(".", StringComparison.Ordinal) ||
            trimmed.Equals("..", StringComparison.Ordinal) ||
            trimmed.Contains("..", StringComparison.Ordinal) ||
            trimmed.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
            trimmed.IndexOf(Path.AltDirectorySeparatorChar) >= 0 ||
            Path.GetInvalidFileNameChars().Any(trimmed.Contains) ||
            Path.IsPathRooted(trimmed))
        {
            throw new ArgumentException($"Unsafe {label} '{value}'.", argumentName);
        }

        return trimmed;
    }
}
