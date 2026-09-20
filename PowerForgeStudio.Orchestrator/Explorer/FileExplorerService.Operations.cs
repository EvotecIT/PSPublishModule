using PowerForgeStudio.Domain.Hub;

namespace PowerForgeStudio.Orchestrator.Explorer;

public sealed partial class FileExplorerService
{
    /// <summary>
    /// Performs an explicit create/copy/move inside a working copy. Existing destinations are never overwritten.
    /// Linked directories and nested Git metadata are rejected before a recursive copy or directory move.
    /// </summary>
    public Task ExecuteAsync(WorkspaceFileOperationRequest request, CancellationToken cancellationToken = default,
        IProgress<FileTransferProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Task.Run(() => Execute(request, cancellationToken, progress), cancellationToken);
    }

    private static void Execute(WorkspaceFileOperationRequest request, CancellationToken token, IProgress<FileTransferProgress>? progress)
    {
        if (!Enum.IsDefined(request.Operation)) throw new ArgumentOutOfRangeException(nameof(request));
        var root = Path.GetFullPath(request.WorkingCopyRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var target = ValidateOperationPath(root, request.DestinationPath);
        if (File.Exists(target) || Directory.Exists(target))
            throw new IOException("The destination already exists. Choose another name; existing files are not overwritten.");
        if (!Directory.Exists(Path.GetDirectoryName(target)))
            throw new DirectoryNotFoundException("The destination folder does not exist.");

        token.ThrowIfCancellationRequested();
        if (request.Operation == WorkspaceFileOperation.CreateFile)
        {
            using var file = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            return;
        }
        if (request.Operation == WorkspaceFileOperation.CreateDirectory)
        {
            Directory.CreateDirectory(target);
            return;
        }

        var source = ValidateOperationPath(root, request.SourcePath ?? throw new ArgumentException("A source is required."));
        if (!File.Exists(source) && !Directory.Exists(source)) throw new FileNotFoundException("The selected item no longer exists.", source);
        if (request.Operation == WorkspaceFileOperation.Rename &&
            !string.Equals(Path.GetDirectoryName(source), Path.GetDirectoryName(target), PathComparison))
            throw new ArgumentException("Rename must keep the item in its current folder. Use Move to change folders.");

        if (Directory.Exists(source))
        {
            if (IsWithin(source, target)) throw new IOException("A folder cannot be copied or moved into itself.");
            var entries = InspectTransferTree(source, token);
            token.ThrowIfCancellationRequested();
            if (request.Operation == WorkspaceFileOperation.Copy)
            {
                // Stage beside the final destination so a failed/cancelled copy does not leave a partial target.
                var stage = Path.Combine(Path.GetDirectoryName(target)!, ".powerforge-copy-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(stage);
                try
                {
                    foreach (var item in entries)
                    {
                        token.ThrowIfCancellationRequested();
                        // Recheck after preflight; do not follow a link introduced while copying.
                        ValidateOperationPath(root, item.Path);
                        var destination = Path.Combine(stage, Path.GetRelativePath(source, item.Path));
                        if (item.IsDirectory) Directory.CreateDirectory(destination);
                        else CopyFile(item.Path, destination, token, progress);
                    }
                    token.ThrowIfCancellationRequested();
                    ValidateOperationPath(root, target);
                    Directory.Move(stage, target);
                }
                finally
                {
                    if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
                }
            }
            else Directory.Move(source, target);
        }
        else if (request.Operation == WorkspaceFileOperation.Copy)
        {
            CopyFile(source, target, token, progress);
        }
        else File.Move(source, target, overwrite: false);
    }

    private static void CopyFile(string source, string target, CancellationToken token, IProgress<FileTransferProgress>? progress)
    {
        var created = false;
        try
        {
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            UnixFileMode? mode = null;
            if (!OperatingSystem.IsWindows())
            {
                mode = File.GetUnixFileMode(source);
                options.UnixCreateMode = mode.Value;
            }
            using var output = new FileStream(target, options);
            created = true;
            var buffer = new byte[81920];
            int count;
            long copied = 0;
            long reportedAt = 0;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while ((count = input.Read(buffer)) > 0)
            {
                token.ThrowIfCancellationRequested();
                output.Write(buffer, 0, count);
                copied += count;
                if (reportedAt == 0 || watch.ElapsedMilliseconds - reportedAt >= 100)
                {
                    progress?.Report(new FileTransferProgress(source, copied, input.Length));
                    reportedAt = Math.Max(1, watch.ElapsedMilliseconds);
                }
            }
            token.ThrowIfCancellationRequested();
            if (!OperatingSystem.IsWindows() && mode is { } unixMode) File.SetUnixFileMode(target, unixMode);
            progress?.Report(new FileTransferProgress(source, copied, input.Length));
            token.ThrowIfCancellationRequested();
        }
        catch
        {
            if (created) File.Delete(target);
            throw;
        }
    }

    private static IReadOnlyList<(string Path, bool IsDirectory)> InspectTransferTree(string source, CancellationToken token)
    {
        var entries = new List<(string, bool)>();
        var pending = new Stack<string>();
        pending.Push(source);
        while (pending.TryPop(out var directory))
        {
            token.ThrowIfCancellationRequested();
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                token.ThrowIfCancellationRequested();
                if (string.Equals(Path.GetFileName(path), ".git", StringComparison.OrdinalIgnoreCase))
                    throw new IOException("This folder contains a Git repository. Manage working copies from the project view.");
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("This folder contains a link. Manage linked entries externally.");
                var isDirectory = (attributes & FileAttributes.Directory) != 0;
                entries.Add((path, isDirectory));
                if (isDirectory) pending.Push(path);
            }
        }
        return entries;
    }

    private static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static bool IsWithin(string root, string path) =>
        path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, PathComparison);

    private static string ValidateOperationPath(string root, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (OperatingSystem.IsWindows())
        {
            // Windows path normalization removes trailing dots/spaces; reject before resolving.
            var raw = path[(Path.GetPathRoot(path)?.Length ?? 0)..];
            foreach (var part in raw.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                if (part.Length > 0 && part is not "." and not ".." &&
                    (part.EndsWith(' ') || part.EndsWith('.') || IsWindowsDeviceName(part) || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                    throw new ArgumentException("The path contains an invalid file name.");
        }
        var full = Path.GetFullPath(path, root);
        if (!IsWithin(root, full)) throw new ArgumentException("Choose an item inside the selected working copy; its root cannot be changed.");
        var relative = Path.GetRelativePath(root, full);
        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parts.Any(p => string.Equals(p, ".git", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Git metadata cannot be modified from the file explorer.");
        var current = root;
        foreach (var part in parts.Prepend(""))
        {
            if (part.Length > 0)
            {
                if (part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    (OperatingSystem.IsWindows() && (part.EndsWith(' ') || part.EndsWith('.') || IsWindowsDeviceName(part))))
                    throw new ArgumentException("The path contains an invalid file name.");
                current = Path.Combine(current, part);
            }
            // GetAttributes also identifies dangling symbolic links when File.Exists reports false.
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new IOException("Operations through symbolic links or junctions are not supported.");
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
        return full;
    }

    private static bool IsWindowsDeviceName(string name)
    {
        var stem = name.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" ||
               stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) && stem[3] is >= '1' and <= '9';
    }
}
