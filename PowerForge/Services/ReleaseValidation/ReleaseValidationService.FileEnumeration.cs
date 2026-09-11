namespace PowerForge;

public sealed partial class ReleaseValidationService
{
    // Enumerate one directory at a time so cancellation also applies to large trees of empty directories.
    private static IEnumerable<string> EnumerateValidationFiles(string root, CancellationToken cancellationToken)
    {
        var directories = new Stack<string>();
        directories.Push(root);
        while (directories.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var path in Directory.EnumerateFileSystemEntries(directories.Pop()))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((File.GetAttributes(path) & FileAttributes.Directory) != 0) directories.Push(path);
                else yield return path;
            }
        }
        cancellationToken.ThrowIfCancellationRequested();
    }
}
