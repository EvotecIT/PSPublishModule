using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PowerForgeStudio.Domain.Hub;
using PowerForgeStudio.Orchestrator.Host;

namespace PowerForgeStudio.Orchestrator.Explorer;

/// <summary>Moves reviewed workspace items into a private Studio recovery store and restores them later.</summary>
public sealed class FileRecoveryService : IFileRecoveryService
{
    private const string ManifestFileName = "manifest.json";
    private const string PayloadName = "payload";
    private readonly string _recoveryRoot;

    public FileRecoveryService(string? recoveryRoot = null)
    {
        _recoveryRoot = Path.GetFullPath(recoveryRoot ?? PowerForgeStudioHostPaths.GetFileRecoveryRootPath());
    }

    public Task<WorkspaceFileDeletionPreview> InspectDeleteAsync(
        string workingCopyRoot,
        string sourcePath,
        CancellationToken cancellationToken = default)
        => Task.Run(() => InspectDelete(workingCopyRoot, sourcePath, cancellationToken), cancellationToken);

    public async Task<WorkspaceFileRecoveryEntry> DeleteAsync(
        WorkspaceFileDeletionPreview preview,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var current = await InspectDeleteAsync(
            preview.WorkingCopyRoot,
            preview.SourcePath,
            cancellationToken).ConfigureAwait(false);
        if (!string.Equals(current.SnapshotSha256, preview.SnapshotSha256, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The selected item changed after review. Inspect it again before moving it to recovery.");

        return await Task.Run(
            () => MoveToRecovery(current, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<WorkspaceFileRecoveryEntry>> ListAsync(
        string? workingCopyRoot = null,
        CancellationToken cancellationToken = default)
        => Task.Run(() => List(workingCopyRoot, cancellationToken), cancellationToken);

    public Task RestoreAsync(
        WorkspaceFileRecoveryEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Task.Run(() => Restore(entry, cancellationToken), cancellationToken);
    }

    public Task DeletePermanentlyAsync(
        WorkspaceFileRecoveryEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return Task.Run(() => DeletePermanently(entry, cancellationToken), cancellationToken);
    }

    private static WorkspaceFileDeletionPreview InspectDelete(
        string workingCopyRoot,
        string sourcePath,
        CancellationToken token)
    {
        var root = Path.GetFullPath(workingCopyRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(root);

        var source = FileExplorerService.ValidateOperationPath(root, sourcePath);
        var isDirectory = Directory.Exists(source);
        if (!isDirectory && !File.Exists(source))
            throw new FileNotFoundException("The selected item no longer exists.", source);

        var entries = isDirectory
            ? FileExplorerService.InspectTransferTree(source, token)
            : [];
        token.ThrowIfCancellationRequested();
        var snapshots = new List<EntrySnapshot>(entries.Count + 1)
        {
            CaptureSnapshot(source, source, isDirectory)
        };
        snapshots.AddRange(entries.Select(item => CaptureSnapshot(source, item.Path, item.IsDirectory)));
        snapshots.Sort(static (left, right) => StringComparer.Ordinal.Compare(left.RelativePath, right.RelativePath));
        var size = snapshots.Where(static entry => !entry.IsDirectory).Sum(static entry => entry.Length);
        return new WorkspaceFileDeletionPreview(
            root,
            source,
            isDirectory,
            snapshots.Count,
            size,
            ComputeSnapshotHash(snapshots));
    }

    private WorkspaceFileRecoveryEntry MoveToRecovery(
        WorkspaceFileDeletionPreview preview,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (string.Equals(preview.WorkingCopyRoot, _recoveryRoot, FileExplorerService.PathComparison) ||
            FileExplorerService.IsWithin(preview.WorkingCopyRoot, _recoveryRoot) ||
            FileExplorerService.IsWithin(_recoveryRoot, preview.WorkingCopyRoot))
            throw new IOException("The Studio recovery store must be outside the selected working copy.");
        Directory.CreateDirectory(_recoveryRoot);
        ValidateRecoveryRoot();

        var id = Guid.NewGuid().ToString("N");
        var entryDirectory = Path.Combine(_recoveryRoot, id);
        var payload = Path.Combine(entryDirectory, PayloadName);
        Directory.CreateDirectory(entryDirectory);
        var entry = new WorkspaceFileRecoveryEntry(
            id,
            preview.WorkingCopyRoot,
            preview.SourcePath,
            payload,
            preview.IsDirectory,
            preview.ItemCount,
            preview.SizeBytes,
            DateTimeOffset.UtcNow);
        WriteManifest(entryDirectory, new RecoveryManifest(entry, "Prepared"));

        try
        {
            token.ThrowIfCancellationRequested();
            if (preview.IsDirectory)
                Directory.Move(preview.SourcePath, payload);
            else
                File.Move(preview.SourcePath, payload, overwrite: false);
            WriteManifest(entryDirectory, new RecoveryManifest(entry, "Available"));
            return entry;
        }
        catch
        {
            if (!File.Exists(payload) && !Directory.Exists(payload) && Directory.Exists(entryDirectory))
                Directory.Delete(entryDirectory, recursive: true);
            throw;
        }
    }

    private IReadOnlyList<WorkspaceFileRecoveryEntry> List(string? workingCopyRoot, CancellationToken token)
    {
        if (!Directory.Exists(_recoveryRoot))
            return [];
        ValidateRecoveryRoot();
        var rootFilter = string.IsNullOrWhiteSpace(workingCopyRoot)
            ? null
            : Path.GetFullPath(workingCopyRoot);
        var entries = new List<WorkspaceFileRecoveryEntry>();
        foreach (var entryDirectory in Directory.EnumerateDirectories(_recoveryRoot))
        {
            token.ThrowIfCancellationRequested();
            if ((File.GetAttributes(entryDirectory) & FileAttributes.ReparsePoint) != 0)
                continue;
            var manifest = ReadManifest(entryDirectory);
            if (manifest is null || !IsManifestBoundToEntryDirectory(entryDirectory, manifest))
                continue;
            if (manifest is { State: "Prepared" or "Available" or "Deleting" or "Restoring" or "Restored" } incomplete &&
                !File.Exists(incomplete.Entry.RecoveryPath) && !Directory.Exists(incomplete.Entry.RecoveryPath))
            {
                Directory.Delete(entryDirectory, recursive: true);
                continue;
            }
            if (manifest is { State: "Prepared" or "Restoring" or "Deleting" } pending &&
                (File.Exists(pending.Entry.RecoveryPath) || Directory.Exists(pending.Entry.RecoveryPath)))
            {
                manifest = pending with { State = "Available" };
                WriteManifest(entryDirectory, manifest);
            }
            if (manifest?.Entry is not { } entry ||
                !string.Equals(manifest.State, "Available", StringComparison.Ordinal) ||
                (!File.Exists(entry.RecoveryPath) && !Directory.Exists(entry.RecoveryPath)))
                continue;
            if (rootFilter is not null &&
                !string.Equals(Path.GetFullPath(entry.WorkingCopyRoot), rootFilter, FileExplorerService.PathComparison))
                continue;
            entries.Add(entry);
        }

        return entries
            .OrderByDescending(static entry => entry.DeletedAtUtc)
            .ToArray();
    }

    private void Restore(WorkspaceFileRecoveryEntry requested, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var available = LoadAvailableEntry(requested);
        var entryDirectory = available.EntryDirectory;
        var manifest = available.Manifest;
        var entry = manifest.Entry;

        var root = Path.GetFullPath(entry.WorkingCopyRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException("The original working copy no longer exists.");
        var destination = FileExplorerService.ValidateOperationPath(root, entry.OriginalPath);
        if (File.Exists(destination) || Directory.Exists(destination))
            throw new IOException("The original path is already occupied. Move or rename that item before restoring.");
        var parent = Path.GetDirectoryName(destination);
        if (string.IsNullOrWhiteSpace(parent) || !Directory.Exists(parent))
            throw new DirectoryNotFoundException("The original parent folder no longer exists. Restore it first.");
        if (!File.Exists(entry.RecoveryPath) && !Directory.Exists(entry.RecoveryPath))
            throw new IOException("The recovered payload is missing.");
        RejectReparsePoint(entry.RecoveryPath);

        WriteManifest(entryDirectory, manifest with { State = "Restoring" });
        token.ThrowIfCancellationRequested();
        if (entry.IsDirectory)
            Directory.Move(entry.RecoveryPath, destination);
        else
            File.Move(entry.RecoveryPath, destination, overwrite: false);
        WriteManifest(entryDirectory, manifest with { State = "Restored" });
    }

    private void DeletePermanently(WorkspaceFileRecoveryEntry requested, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var available = LoadAvailableEntry(requested);
        WriteManifest(available.EntryDirectory, available.Manifest with { State = "Deleting" });
        token.ThrowIfCancellationRequested();
        if (available.Manifest.Entry.IsDirectory)
        {
            _ = FileExplorerService.InspectTransferTree(available.Manifest.Entry.RecoveryPath, token);
            Directory.Delete(available.Manifest.Entry.RecoveryPath, recursive: true);
        }
        else
            File.Delete(available.Manifest.Entry.RecoveryPath);
        TryDeleteEntryDirectory(available.EntryDirectory);
    }

    private AvailableRecoveryEntry LoadAvailableEntry(WorkspaceFileRecoveryEntry requested)
    {
        ValidateRecoveryRoot();
        if (!Guid.TryParseExact(requested.Id, "N", out _))
            throw new ArgumentException("The recovery entry identifier is invalid.");
        var entryDirectory = Path.GetFullPath(Path.Combine(_recoveryRoot, requested.Id));
        if (!FileExplorerService.IsWithin(_recoveryRoot, entryDirectory))
            throw new ArgumentException("The recovery entry is outside the Studio recovery store.");
        RejectReparsePoint(entryDirectory);
        var manifest = ReadManifest(entryDirectory);
        if (manifest?.Entry is not { } entry || !string.Equals(manifest.State, "Available", StringComparison.Ordinal))
            throw new IOException("The recovery entry is no longer available.");
        if (!IsManifestBoundToEntryDirectory(entryDirectory, manifest))
            throw new IOException("The recovery entry payload path is invalid.");
        if (!string.Equals(entry.Id, requested.Id, StringComparison.Ordinal) ||
            !string.Equals(entry.OriginalPath, requested.OriginalPath, FileExplorerService.PathComparison))
            throw new IOException("The recovery entry does not match the selected item.");
        if (!File.Exists(entry.RecoveryPath) && !Directory.Exists(entry.RecoveryPath))
            throw new IOException("The recovered payload is missing.");
        RejectReparsePoint(entry.RecoveryPath);
        return new AvailableRecoveryEntry(entryDirectory, manifest);
    }

    private static EntrySnapshot CaptureSnapshot(string sourceRoot, string path, bool isDirectory)
    {
        var info = isDirectory ? null : new FileInfo(path);
        var timestamp = isDirectory
            ? new DirectoryInfo(path).LastWriteTimeUtc.Ticks
            : info!.LastWriteTimeUtc.Ticks;
        return new EntrySnapshot(
            Path.GetRelativePath(sourceRoot, path),
            isDirectory,
            info?.Length ?? 0,
            timestamp,
            (int)File.GetAttributes(path));
    }

    private static string ComputeSnapshotHash(IEnumerable<EntrySnapshot> entries)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var entry in entries)
        {
            var line = $"{entry.RelativePath}\0{entry.IsDirectory}\0{entry.Length}\0{entry.LastWriteTicks}\0{entry.Attributes}\n";
            hash.AppendData(Encoding.UTF8.GetBytes(line));
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Recovery operations through symbolic links or junctions are not supported.");
    }

    private void ValidateRecoveryRoot()
    {
        if (!Directory.Exists(_recoveryRoot))
            throw new DirectoryNotFoundException("The Studio recovery store does not exist.");
        var pathRoot = Path.GetPathRoot(_recoveryRoot);
        if (string.IsNullOrWhiteSpace(pathRoot))
            throw new IOException("The Studio recovery store path is invalid.");
        var current = pathRoot;
        RejectReparsePoint(current);
        var relative = Path.GetRelativePath(pathRoot, _recoveryRoot);
        if (relative == ".")
            return;
        foreach (var part in relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
        {
            current = Path.Combine(current, part);
            RejectReparsePoint(current);
        }
    }

    private static RecoveryManifest? ReadManifest(string entryDirectory)
    {
        var path = Path.Combine(entryDirectory, ManifestFileName);
        if (!File.Exists(path))
            return null;
        try
        {
            return JsonSerializer.Deserialize<RecoveryManifest>(File.ReadAllText(path));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool IsManifestBoundToEntryDirectory(string entryDirectory, RecoveryManifest manifest)
    {
        try
        {
            var directoryId = Path.GetFileName(entryDirectory);
            return Guid.TryParseExact(directoryId, "N", out _) &&
                   string.Equals(manifest.Entry.Id, directoryId, StringComparison.Ordinal) &&
                   string.Equals(
                       Path.GetFullPath(manifest.Entry.RecoveryPath),
                       Path.Combine(entryDirectory, PayloadName),
                       FileExplorerService.PathComparison);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static void WriteManifest(string entryDirectory, RecoveryManifest manifest)
    {
        var path = Path.Combine(entryDirectory, ManifestFileName);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, path, overwrite: true);
    }

    private static void TryDeleteEntryDirectory(string entryDirectory)
    {
        try
        {
            Directory.Delete(entryDirectory, recursive: true);
        }
        catch (IOException)
        {
            // The payload is already gone. A later listing removes the Deleting manifest.
        }
        catch (UnauthorizedAccessException)
        {
            // The payload is already gone. A later listing removes the Deleting manifest.
        }
    }

    private sealed record EntrySnapshot(
        string RelativePath,
        bool IsDirectory,
        long Length,
        long LastWriteTicks,
        int Attributes);

    private sealed record RecoveryManifest(WorkspaceFileRecoveryEntry Entry, string State);
    private sealed record AvailableRecoveryEntry(string EntryDirectory, RecoveryManifest Manifest);
}
