namespace PowerForge.Web;

public static partial class WebVisualStoryStager
{
    private static StringComparison GetFileSystemPathComparison(string path)
    {
        var directory = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
        while (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            directory = Path.GetDirectoryName(directory);
        if (string.IsNullOrWhiteSpace(directory))
            directory = Path.GetPathRoot(path);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return StringComparison.Ordinal;

        var probeName = ".powerforge-case-probe-" + Guid.NewGuid().ToString("N");
        var probePath = Path.Combine(directory, probeName);
        var alternatePath = Path.Combine(directory, probeName.ToUpperInvariant());
        try
        {
            using (new FileStream(
                       probePath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.ReadWrite | FileShare.Delete))
            {
            }
            return File.Exists(alternatePath)
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        }
        finally
        {
            if (File.Exists(probePath))
                File.Delete(probePath);
        }
    }

    private static bool SamePath(string left, string right, StringComparison comparison)
        => string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), comparison);

    internal static StringComparison GetFileSystemPathComparisonForTesting(string path)
        => GetFileSystemPathComparison(path);
}
