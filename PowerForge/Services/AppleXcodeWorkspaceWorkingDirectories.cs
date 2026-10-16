namespace PowerForge;

/// <summary>Identifies the ordinary workspace containers Xcode creates while preparing a build.</summary>
internal static class AppleXcodeWorkspaceWorkingDirectories
{
    internal static IReadOnlyList<string> GetMetadataDirectories(string projectPath, bool isWorkspace)
    {
        var workspace = isWorkspace ? projectPath : Path.Combine(projectPath, "project.xcworkspace");
        var shared = Path.Combine(workspace, "xcshareddata");
        var packages = Path.Combine(shared, "swiftpm");
        return new[] { shared, packages, Path.Combine(packages, "configuration") };
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
        if (!paths.Any(path => args.FullPath.Equals(path, comparison))) return false;
        try
        {
            var attributes = File.GetAttributes(args.FullPath);
            return (attributes & FileAttributes.Directory) != 0 && (attributes & FileAttributes.ReparsePoint) == 0;
        }
        catch { return false; }
    }
}
