namespace PowerForge;

public sealed partial class ReleaseValidationService
{
    // Enumerate one directory at a time so cancellation also applies to large trees of empty directories.
    private static IEnumerable<string> EnumerateValidationFiles(string root, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        FileSystemPathSafety.RejectReparsePoints(root, root, "Validation directory");
        var directories = new Stack<string>();
        directories.Push(root);
        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var path in Directory.EnumerateFileSystemEntries(directories.Pop()))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException($"Validation input '{path}' is a symbolic link or reparse point.");
                if ((attributes & FileAttributes.Directory) != 0) directories.Push(path);
                else
                {
                    FileSystemPathSafety.RequireRegularFile(path);
                    yield return path;
                }
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static bool HasNonEmptyValidationFile(string root, CancellationToken cancellationToken)
    {
        var found = false;
        // Complete the traversal even after finding a payload: a later linked entry
        // must not be hidden by an early Any() result.
        foreach (var path in EnumerateValidationFiles(root, cancellationToken))
            if (!found && new FileInfo(path).Length > 0) found = true;
        return found;
    }
}
