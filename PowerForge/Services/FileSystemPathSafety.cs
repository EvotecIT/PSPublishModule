namespace PowerForge;

/// <summary>Filesystem preflight shared by artifact writers and validators; not a concurrent-mutation sandbox.</summary>
internal static class FileSystemPathSafety
{
    /// <summary>Rejects existing link components through the inclusive trusted boundary (or filesystem root).
    /// Callers must establish lexical containment before supplying a boundary.</summary>
    internal static void RejectReparsePoints(string fullPath, string? trustedBoundary, string description)
    {
        var current = fullPath;
        while (true)
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException(
                        $"{description} '{fullPath}' traverses a symbolic link or reparse point at '{current}'.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }

            if (trustedBoundary is not null && IsTrustedBoundary(current, trustedBoundary))
                return;
            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.Ordinal))
            {
                if (trustedBoundary is not null)
                    throw new InvalidOperationException($"{description} '{fullPath}' is outside '{trustedBoundary}'.");
                return;
            }
            current = parent;
        }
    }

    private static bool IsTrustedBoundary(string current, string boundary)
    {
        if (string.Equals(current, boundary, StringComparison.Ordinal)) return true;
        if (!string.Equals(current, boundary, StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(current) || !Directory.Exists(boundary)) return false;
        // Windows directories may independently enable case sensitivity. Compare the
        // OS identities, not a volume-wide or probe-file case-sensitivity assumption.
        return ExistingFilePathIdentityResolver.ResolveDirectoryStatus(current).Identity ==
            ExistingFilePathIdentityResolver.ResolveDirectoryStatus(boundary).Identity;
    }
}
