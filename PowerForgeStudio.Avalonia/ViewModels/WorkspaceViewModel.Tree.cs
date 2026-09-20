using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Avalonia.ViewModels;

public sealed partial class WorkspaceViewModel
{
    private readonly Dictionary<string, ProjectGitStatus> _gitSnapshots = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    private void UpdateGitDecorations(string root, ProjectGitStatus snapshot)
    {
        if (string.IsNullOrEmpty(root)) return;
        _gitSnapshots[root] = snapshot;
        var changes = snapshot.UntrackedFiles.Concat(snapshot.StagedChanges).Concat(snapshot.UnstagedChanges)
            .GroupBy(change => Path.GetFullPath(change.Path, root), OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Any(change => change.Kind == GitChangeKind.Unmerged) ? "U" : group.Last().KindDisplay,
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var node in LoadedNodes().Where(node => !string.IsNullOrEmpty(node.Path) && SamePath(node.RepositoryRoot, root)))
        {
            node.StatusMarker = node.Kind == "branch" && SamePath(node.Path, root)
                ? !snapshot.IsGitRepository ? "not Git" : snapshot.HasConflicts ? "conflicts" : changes.Count == 1 ? "1 change" : changes.Count > 0 ? $"{changes.Count} changes" : "clean"
                : node.Kind is "project" or "folder" ? "" : changes.GetValueOrDefault(Path.GetFullPath(node.Path), "");
        }
    }
}
