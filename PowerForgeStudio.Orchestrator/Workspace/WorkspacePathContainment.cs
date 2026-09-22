namespace PowerForgeStudio.Orchestrator.Workspace;

/// <summary>Compares workspace paths without treating sibling prefixes as descendants.</summary>
public static class WorkspacePathContainment
{
    public static bool ContainsOrEquals(string workspaceRoot, string candidate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidate);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
        var path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(candidate));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var prefix = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
        return string.Equals(path, root, comparison) || path.StartsWith(prefix, comparison);
    }
}
