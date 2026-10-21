namespace PowerForge;

/// <summary>Identifies the ordinary workspace containers Xcode creates while preparing a build.</summary>
internal static class AppleXcodeWorkspaceWorkingDirectories
{
    internal static IReadOnlyList<string> GetMetadataDirectories(string projectPath, bool isWorkspace)
    {
        var packages = GetPackageMetadataRoot(projectPath, isWorkspace);
        var shared = Path.GetDirectoryName(packages)!;
        return new[] { shared, packages, Path.Combine(packages, "configuration") };
    }

    /// <summary>Locates the workspace's package metadata without materializing package files.</summary>
    internal static string GetPackageMetadataRoot(string projectPath, bool isWorkspace)
    {
        var workspace = isWorkspace ? projectPath : Path.Combine(projectPath, "project.xcworkspace");
        return Path.Combine(workspace, "xcshareddata", "swiftpm");
    }

    // Local mirrors tolerate creation/metadata only. Files, deletions, renames,
    // and links remain mutations. Exact release snapshots prepare these paths
    // before observing and never use this event exception.
    internal static bool IsExpectedDirectoryMutation(FileSystemEventArgs args, string projectPath, bool isWorkspace)
    {
        if (args.ChangeType != WatcherChangeTypes.Created && args.ChangeType != WatcherChangeTypes.Changed)
            return false;
        var paths = GetMetadataDirectories(projectPath, isWorkspace);
        var comparison = FrameworkCompatibility.GetPathStringComparisonForPath(paths[0]);
        // Creating the allowed metadata folders can also update their existing workspace
        // container's timestamp (notably with Windows directory notifications).
        var workspace = isWorkspace ? projectPath : Path.Combine(projectPath, "project.xcworkspace");
        var workspaceMetadataChanged = args.ChangeType == WatcherChangeTypes.Changed &&
            args.FullPath.Equals(workspace, comparison);
        if (!workspaceMetadataChanged && !paths.Any(path => args.FullPath.Equals(path, comparison))) return false;
        try
        {
            var attributes = File.GetAttributes(args.FullPath);
            return (attributes & FileAttributes.Directory) != 0 && (attributes & FileAttributes.ReparsePoint) == 0;
        }
        catch { return false; }
    }
}
