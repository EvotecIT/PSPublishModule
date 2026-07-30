namespace PowerForge;

/// <summary>
/// Owns the canonical operating-system labels used by imported results and evidence catalogs.
/// </summary>
internal static class BenchmarkPlatformNormalizer
{
    internal static string NormalizeFamily(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string normalized = value!.Trim();
        if (Contains(normalized, "windows"))
            return "Windows";
        if (Contains(normalized, "linux") ||
            Contains(normalized, "ubuntu") ||
            Contains(normalized, "debian") ||
            Contains(normalized, "fedora") ||
            Contains(normalized, "red hat") ||
            Contains(normalized, "rhel") ||
            Contains(normalized, "suse"))
            return "Linux";
        if (Contains(normalized, "mac") ||
            Contains(normalized, "osx") ||
            Contains(normalized, "darwin"))
            return "macOS";
        return normalized;
    }

    internal static string NormalizeId(string? value)
    {
        string family = NormalizeFamily(value);
        if (family.Equals("Windows", StringComparison.OrdinalIgnoreCase))
            return "windows";
        if (family.Equals("Linux", StringComparison.OrdinalIgnoreCase))
            return "linux";
        if (family.Equals("macOS", StringComparison.OrdinalIgnoreCase))
            return "macos";
        return family.ToLowerInvariant().Replace(" ", "-");
    }

    private static bool Contains(string value, string candidate)
        => value.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0;
}
