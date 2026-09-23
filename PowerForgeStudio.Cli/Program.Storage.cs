using PowerForgeStudio.Domain.Workspace;
using PowerForgeStudio.Orchestrator.Workspace;

internal static partial class Program
{
    private static async Task WriteStorageAsync(string workspaceRoot, int top, bool outputJson)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, args) => { args.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += onCancel;
        WorkspaceStorageSnapshot snapshot;
        try
        {
            snapshot = await new WorkspaceStorageInspectionService()
                .InspectAsync(workspaceRoot, new StorageConsoleProgress(), cancellation.Token).ConfigureAwait(false);
        }
        finally { Console.CancelKeyPress -= onCancel; }
        var worktrees = snapshot.Entries.Where(static entry => !entry.IsPrimary && entry.Exists)
            .OrderByDescending(static entry => entry.SizeBytes).ThenBy(static entry => entry.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        var candidates = snapshot.Entries.Where(static entry => entry.IsReviewCandidate)
            .OrderByDescending(static entry => entry.SizeBytes).ThenBy(static entry => entry.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        var broken = snapshot.Entries.Where(static entry => entry.IsBroken)
            .OrderBy(static entry => entry.Path, StringComparer.OrdinalIgnoreCase).ToArray();
        var otherFolders = snapshot.UnregisteredFolders
            .OrderByDescending(static entry => entry.SizeBytes).ThenBy(static entry => entry.Path, StringComparer.OrdinalIgnoreCase).ToArray();

        if (outputJson)
        {
            WriteJson(new {
                success = true,
                snapshot.WorkspaceRoot,
                snapshot.InspectedAtUtc,
                snapshot.IndexedBytes,
                snapshot.WorktreeBytes,
                WorkingCopyCount = snapshot.Entries.Count,
                WorktreeCount = worktrees.Length,
                snapshot.ReviewCandidateCount,
                BrokenReferenceCount = broken.Length,
                OtherFolderCount = otherFolders.Length,
                snapshot.OtherFolderBytes,
                snapshot.UnmeasuredOtherFolderCount,
                snapshot.OtherFolderScanWarning,
                snapshot.RegistrationScanWarning,
                snapshot.IsRegistrationInventoryComplete,
                LargestWorktrees = worktrees.Take(top),
                ReviewCandidates = candidates.Take(top),
                BrokenReferences = broken.Take(top),
                OtherFolders = otherFolders.Take(top),
                note = "Logical file size is not verified reclaimable space. Candidates require the Studio removal review."
            });
            return;
        }

        Console.WriteLine($"Workspace: {snapshot.WorkspaceRoot}");
        if (snapshot.RegistrationScanWarning is { Length: > 0 } registrationWarning)
            Console.WriteLine("Partial inventory: " + registrationWarning);
        Console.WriteLine($"Indexed: {snapshot.IndexedDisplay} logical across {snapshot.Entries.Count} working copies");
        Console.WriteLine($"Worktrees: {snapshot.WorktreeDisplay} logical across {worktrees.Length} existing copies");
        Console.WriteLine($"Local review candidates: {snapshot.ReviewCandidateCount}; broken references: {broken.Length}");
        Console.WriteLine($"Other _worktrees folders: {otherFolders.Length}, {snapshot.OtherFolderDisplay} measured logical" +
                          (snapshot.UnmeasuredOtherFolderCount > 0 ? $", {snapshot.UnmeasuredOtherFolderCount} not measured" : "") +
                          "; no removal action is offered");
        if (snapshot.OtherFolderScanWarning is { Length: > 0 } warning)
            Console.WriteLine(warning);
        WriteStorageRows("Largest worktrees", worktrees.Take(top));
        WriteStorageRows("Local review candidates", candidates.Take(top));
        WriteStorageRows("Broken references", broken.Take(top));
        Console.WriteLine("Other _worktrees folders (not classified as cleanup candidates):");
        foreach (var folder in otherFolders.Take(top))
            Console.WriteLine($"  {folder.SizeDisplay,10}  {folder.Kind,-26}  {folder.Path}");
        Console.WriteLine("Sizes are logical, not verified reclaimable space. Review removal in Studio before any cleanup.");
    }

    private static void WriteStorageRows(string heading, IEnumerable<WorkspaceStorageEntry> entries)
    {
        Console.WriteLine(heading + ":");
        foreach (var entry in entries)
            Console.WriteLine($"  {entry.SizeDisplay,10}  {entry.LocalStateDisplay,-20}  {entry.Path}");
    }

    private sealed class StorageConsoleProgress : IProgress<WorkspaceStorageScanProgress>
    {
        public void Report(WorkspaceStorageScanProgress value)
            => Console.Error.WriteLine($"Storage {(value.IsOtherFolder ? "other folder" : $"{value.CompletedRepositories}/{value.TotalRepositories}")}: " +
                                       $"{Path.GetFileName(value.WorkingCopyPath)} - " +
                                       $"{value.MeasuredDisplay} measured in current copy");
    }
}
