namespace PowerForge;

public sealed partial class ArtefactBuilder
{
    private static StringComparison GetArtefactPathComparison(string path)
    {
        string directory = Path.GetFullPath(path);
        while (!Directory.Exists(directory))
        {
            string? parent = Path.GetDirectoryName(directory);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, directory, StringComparison.Ordinal))
                return FrameworkCompatibility.ConservativePathStringComparison();
            directory = parent;
        }

        // An empty directory has no names to inspect. Read its Windows flag rather
        // than caching a case-sensitive guess that could permit a payload collision.
        if (WindowsDirectoryCaseSensitivity.TryGet(directory, out bool caseSensitive))
            return caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        return FrameworkCompatibility.GetPathStringComparison(directory);
    }
}
