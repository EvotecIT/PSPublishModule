namespace PowerForge;

/// <summary>Filesystem preflight shared by artifact writers and validators; not a concurrent-mutation sandbox.</summary>
internal static class FileSystemPathSafety
{
    /// <summary>Rejects special file inputs before a potentially blocking open; not a concurrent-replacement guard.</summary>
    internal static void RequireRegularFile(string path, bool followSymbolicLinks = false)
    {
        var attributes = File.GetAttributes(path);
        var regular = FrameworkCompatibility.IsWindows()
            ? (attributes & (FileAttributes.Directory | FileAttributes.Device |
                (followSymbolicLinks ? 0 : FileAttributes.ReparsePoint))) == 0
            : ExistingFilePathIdentityResolver.IsRegularUnixFile(path, followSymbolicLinks);
        if (!regular)
            throw new InvalidOperationException($"Validation input must be a regular file: '{path}'.");
    }

    /// <summary>Compares existing case-variant paths through OS identity without writing case probes.
    /// Differently named hard links remain distinct pathnames; callers needing file uniqueness use physical identity.</summary>
    internal static IEqualityComparer<string> ExistingPathComparer { get; } = new ExistingCaseAliasComparer();

    private sealed class ExistingCaseAliasComparer : IEqualityComparer<string>
    {
        public bool Equals(string? first, string? second)
        {
            if (string.Equals(first, second, StringComparison.Ordinal)) return true;
            if (first is null || second is null || !string.Equals(first, second, StringComparison.OrdinalIgnoreCase)) return false;
            if (Directory.Exists(first) && Directory.Exists(second))
                return ExistingFilePathIdentityResolver.ResolveDirectoryStatus(first).Identity ==
                    ExistingFilePathIdentityResolver.ResolveDirectoryStatus(second).Identity;
            return File.Exists(first) && File.Exists(second) &&
                ExistingFilePathIdentityResolver.Resolve(first) == ExistingFilePathIdentityResolver.Resolve(second);
        }

        public int GetHashCode(string value) => StringComparer.OrdinalIgnoreCase.GetHashCode(value);
    }

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
