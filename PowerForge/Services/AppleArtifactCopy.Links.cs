namespace PowerForge;

internal static partial class AppleArtifactCopy
{
    /// <summary>
    /// Reads a relative artifact link and resolves each linked component inside the same
    /// regular tree. External links, cycles, missing targets, and linked roots reject.
    /// </summary>
    internal static AppleArtifactSymbolicLink InspectContainedSymbolicLink(string artifactRoot, string path)
    {
#if NET8_0_OR_GREATER
        var root = Path.GetFullPath(artifactRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path);
        var relative = FrameworkCompatibility.GetRelativePath(root, fullPath);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidOperationException($"Apple artifact symbolic link is outside its owning tree: {path}");
        for (var parent = Path.GetDirectoryName(fullPath); parent is not null; parent = Path.GetDirectoryName(parent))
        {
            if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException($"Apple artifact symbolic link has a linked root or ancestor: {path}");
            if (string.Equals(parent, root, FrameworkCompatibility.GetPathStringComparisonForPath(root))) break;
        }
        var linkTarget = ReadRelativeLinkTarget(fullPath);
        var components = FrameworkCompatibility.GetRelativePath(root, Path.GetDirectoryName(fullPath)!)
            .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
            .Where(static part => part != ".").ToList();
        var pending = new Queue<string>(SplitLinkTarget(linkTarget));
        var followed = 0;
        while (pending.Count > 0)
        {
            var part = pending.Dequeue();
            if (part == ".") continue;
            if (part == "..")
            {
                if (components.Count == 0)
                    throw new InvalidOperationException($"Apple artifact symbolic links must remain inside the archive or artifact: {path}");
                components.RemoveAt(components.Count - 1);
                continue;
            }
            var candidate = Path.Combine(new[] { root }.Concat(components).Append(part).ToArray());
            var attributes = File.GetAttributes(candidate);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                if (++followed > 40)
                    throw new InvalidOperationException($"Apple artifact symbolic link contains a cycle or excessive link chain: {path}");
                pending = new Queue<string>(SplitLinkTarget(ReadRelativeLinkTarget(candidate)).Concat(pending));
            }
            else
            {
                if (pending.Count > 0 && (attributes & FileAttributes.Directory) == 0)
                    throw new InvalidOperationException($"Apple artifact symbolic link traverses a non-directory target: {path}");
                components.Add(part);
            }
        }
        var resolved = Path.Combine(new[] { root }.Concat(components).ToArray());
        return new AppleArtifactSymbolicLink
        {
            Path = fullPath, ArchiveRoot = root, LinkTarget = linkTarget, ResolvedTargetPath = resolved,
            IsDirectory = (File.GetAttributes(resolved) & FileAttributes.Directory) != 0
        };
#else
        throw new PlatformNotSupportedException("Apple artifact symbolic-link validation requires .NET 8 or newer.");
#endif
    }

#if NET8_0_OR_GREATER
    private static string ReadRelativeLinkTarget(string path)
    {
        var target = new FileInfo(path).LinkTarget ?? new DirectoryInfo(path).LinkTarget;
        if (string.IsNullOrWhiteSpace(target) || Path.IsPathRooted(target))
            throw new InvalidOperationException($"Apple artifact symbolic links require a relative target inside the archive or artifact: {path}");
        return target!;
    }

    private static string[] SplitLinkTarget(string target)
        => target.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
#endif
}

/// <summary>Exact in-tree link metadata bound by the canonical artifact hash.</summary>
internal sealed class AppleArtifactSymbolicLink
{
    public string Path { get; set; } = string.Empty;
    public string ArchiveRoot { get; set; } = string.Empty;
    public string LinkTarget { get; set; } = string.Empty;
    public string ResolvedTargetPath { get; set; } = string.Empty;
    public bool IsDirectory { get; set; }
}
