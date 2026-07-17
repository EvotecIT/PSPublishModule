namespace PowerForge;

/// <summary>
/// Compares managed-module repository sources without erasing filesystem or URI path semantics.
/// </summary>
internal static class ManagedModuleRepositorySourceComparer
{
    internal static bool Equals(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;

        var normalizedLeft = left!.Trim().Trim('"');
        var normalizedRight = right!.Trim().Trim('"');
        var leftIsLocal = TryResolveLocalPath(normalizedLeft, out var leftPath);
        var rightIsLocal = TryResolveLocalPath(normalizedRight, out var rightPath);

        if (leftIsLocal || rightIsLocal)
        {
            if (!leftIsLocal || !rightIsLocal)
                return false;

            return string.Equals(
                leftPath,
                rightPath,
                ResolvePathComparison(leftPath!, rightPath!));
        }

        if (Uri.TryCreate(normalizedLeft, UriKind.Absolute, out var leftUri) &&
            Uri.TryCreate(normalizedRight, UriKind.Absolute, out var rightUri))
        {
            return string.Equals(leftUri.Scheme, rightUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(leftUri.IdnHost, rightUri.IdnHost, StringComparison.OrdinalIgnoreCase) &&
                   leftUri.Port == rightUri.Port &&
                   string.Equals(leftUri.UserInfo, rightUri.UserInfo, StringComparison.Ordinal) &&
                   string.Equals(NormalizeUriPath(leftUri.AbsolutePath), NormalizeUriPath(rightUri.AbsolutePath), StringComparison.Ordinal) &&
                   string.Equals(leftUri.Query, rightUri.Query, StringComparison.Ordinal) &&
                   string.Equals(leftUri.Fragment, rightUri.Fragment, StringComparison.Ordinal);
        }

        return string.Equals(
            normalizedLeft.TrimEnd('/', '\\'),
            normalizedRight.TrimEnd('/', '\\'),
            StringComparison.Ordinal);
    }

    private static bool TryResolveLocalPath(string source, out string? path)
    {
        path = null;
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
        {
            if (!uri.IsFile)
                return false;

            source = uri.LocalPath;
        }

        try
        {
            var fullPath = Path.GetFullPath(source);
            var root = Path.GetPathRoot(fullPath);
            path = string.Equals(fullPath, root, StringComparison.OrdinalIgnoreCase)
                ? fullPath
                : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static StringComparison ResolvePathComparison(string leftPath, string rightPath)
    {
        var probeDirectory = FindExistingDirectory(leftPath) ?? FindExistingDirectory(rightPath);
        return probeDirectory is null
            ? FrameworkCompatibility.PathStringComparison()
            : FrameworkCompatibility.GetPathStringComparison(probeDirectory);
    }

    private static string? FindExistingDirectory(string path)
    {
        var candidate = File.Exists(path) ? Path.GetDirectoryName(path) : path;
        while (!string.IsNullOrWhiteSpace(candidate))
        {
            if (Directory.Exists(candidate))
                return candidate;

            candidate = Path.GetDirectoryName(candidate);
        }

        return null;
    }

    private static string NormalizeUriPath(string path)
        => path == "/" ? string.Empty : path.TrimEnd('/');
}
