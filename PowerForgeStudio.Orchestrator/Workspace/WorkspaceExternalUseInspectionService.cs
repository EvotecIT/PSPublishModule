using PowerForge;
using PowerForgeStudio.Domain.Workspace;

namespace PowerForgeStudio.Orchestrator.Workspace;

/// <summary>Uses Windows Restart Manager for a bounded, secret-safe open-handle scan.</summary>
public sealed class WorkspaceExternalUseInspectionService : IWorkspaceExternalUseInspectionService
{
    private const int MaximumResources = 256;

    public Task<WorkspaceExternalUseEvidence> InspectAsync(
        string workingCopy,
        CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(workingCopy);
        return Task.Run(() => Inspect(root, cancellationToken), cancellationToken);
    }

    private static WorkspaceExternalUseEvidence Inspect(string root, CancellationToken token)
    {
        if (!OperatingSystem.IsWindows())
            return new(false, 0, [], "Open-handle inspection is currently available on Windows only.");
        if (!Directory.Exists(root))
            return new(false, 0, [], "The working copy no longer exists.");

        var resources = ReadResources(root, token);
        if (!LockInspector.TryGetLockingProcesses(resources, out var locks, out var errorCode))
            return new(false, resources.Count, [], $"Windows Restart Manager could not inspect open handles (error {errorCode}).");
        var processes = locks
            .Where(item => item.Pid != Environment.ProcessId)
            .DistinctBy(static item => item.Pid)
            .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static item => item.Pid)
            .Select(static item => new WorkspaceExternalProcess(item.Pid, item.Name))
            .ToArray();
        var warning = resources.Count == MaximumResources
            ? $"The scan sampled the first {MaximumResources} physical files; manual external-use confirmation is still required."
            : "Open-handle evidence cannot detect a terminal whose current directory is the working copy; manual confirmation is still required.";
        return new(true, resources.Count, processes, warning);
    }

    private static IReadOnlyList<string> ReadResources(string root, CancellationToken token)
    {
        var resources = new List<string>(MaximumResources);
        var gitPointer = Path.Combine(root, ".git");
        if (File.Exists(gitPointer)) resources.Add(gitPointer);
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.TryPop(out var directory) && resources.Count < MaximumResources)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                foreach (var path in Directory.EnumerateFileSystemEntries(directory).OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
                {
                    token.ThrowIfCancellationRequested();
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (!string.Equals(Path.GetFileName(path), ".git", StringComparison.OrdinalIgnoreCase))
                            pending.Push(path);
                    }
                    else if (!resources.Contains(path, StringComparer.OrdinalIgnoreCase))
                    {
                        resources.Add(path);
                        if (resources.Count == MaximumResources) break;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Other readable files still provide useful bounded evidence.
            }
        }
        return resources;
    }
}
