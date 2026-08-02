namespace PowerForge.Web;

internal static class VisualStoryPathGuard
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    internal static string ResolveRelativePath(
        string root,
        string relativePath,
        string label,
        bool allowRoot = false)
    {
        var portablePath = relativePath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(portablePath) ||
            portablePath.Length >= 2 && char.IsLetter(portablePath[0]) && portablePath[1] == ':')
            throw new InvalidOperationException($"Visual-story {label} path must be relative: {relativePath}");
        if (portablePath.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries)
            .Any(static segment => string.Equals(segment, "..", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Visual-story {label} path cannot contain parent traversal: {relativePath}");
        }

        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, portablePath));
        EnsureContainedPath(fullRoot, fullPath, label, allowRoot);
        return fullPath;
    }

    internal static void EnsureContainedPath(
        string root,
        string path,
        string label,
        bool allowRoot = false)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(path);
        EnsureExistingPathIsNotLink(fullRoot, label);
        if (string.Equals(fullRoot, fullPath, PathComparison))
        {
            if (!allowRoot)
                throw new InvalidOperationException($"Visual-story {label} must be inside its allowed root.");
            return;
        }

        var prefix = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, PathComparison))
            throw new InvalidOperationException($"Visual-story {label} path escapes its allowed root.");

        EnsureNoLinkTraversal(fullRoot, fullPath, label);
    }

    private static void EnsureExistingPathIsNotLink(string path, string label)
    {
        FileAttributes attributes;
        try
        {
            attributes = File.GetAttributes(path);
        }
        catch (FileNotFoundException)
        {
            return;
        }
        catch (DirectoryNotFoundException)
        {
            return;
        }

        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException(
                $"Visual-story {label} path cannot be a symbolic link or reparse point.");
        }
    }

    private static void EnsureNoLinkTraversal(string root, string path, string label)
    {
        var relative = Path.GetRelativePath(root, path);
        var current = root;
        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (FileNotFoundException)
            {
                break;
            }
            catch (DirectoryNotFoundException)
            {
                break;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"Visual-story {label} path cannot traverse a symbolic link or reparse point.");
            }
        }
    }
}
