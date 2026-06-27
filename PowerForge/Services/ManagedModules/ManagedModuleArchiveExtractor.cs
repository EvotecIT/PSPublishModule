using System.IO.Compression;

namespace PowerForge;

internal sealed class ManagedModuleArchiveExtractor
{
    private static readonly string[] PackageMetadataPrefixes = { "_rels/", "package/" };
    private static readonly string[] PackageMetadataFiles = { "[Content_Types].xml", ".signature.p7s" };

    public ManagedModuleArchiveExtractionResult ExtractPackage(string packagePath, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(packagePath))
            throw new ArgumentException("Package path is required.", nameof(packagePath));
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("Destination path is required.", nameof(destinationPath));

        Directory.CreateDirectory(destinationPath);
        var destinationRoot = Path.GetFullPath(destinationPath);
        var fileCount = 0;
        long bytesWritten = 0;

        using var archive = ZipFile.OpenRead(packagePath);
        foreach (var entry in archive.Entries)
        {
            var normalized = Normalize(entry.FullName);
            if (string.IsNullOrWhiteSpace(normalized) || IsPackageMetadata(normalized))
                continue;
            if (!IsSafePath(normalized))
                throw new InvalidOperationException($"Package contains unsafe path '{entry.FullName}'.");

            var targetPath = Path.GetFullPath(Path.Combine(destinationRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsInsideDirectory(destinationRoot, targetPath))
                throw new InvalidOperationException($"Package entry '{entry.FullName}' escapes the destination directory.");

            if (entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            using var source = entry.Open();
            using var destination = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            source.CopyTo(destination);
            fileCount++;
            bytesWritten += destination.Length;
        }

        return new ManagedModuleArchiveExtractionResult(fileCount, bytesWritten);
    }

    private static string Normalize(string path)
        => path.Replace('\\', '/').Trim('/');

    private static bool IsPackageMetadata(string normalizedPath)
    {
        if (normalizedPath.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            return true;
        if (PackageMetadataFiles.Any(file => normalizedPath.Equals(file, StringComparison.OrdinalIgnoreCase)))
            return true;

        return PackageMetadataPrefixes.Any(prefix => normalizedPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSafePath(string normalizedPath)
    {
        if (normalizedPath.StartsWith("/", StringComparison.Ordinal) ||
            normalizedPath.Contains(":", StringComparison.Ordinal))
            return false;

        var parts = normalizedPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 && parts.All(static part => part != "." && part != "..");
    }

    private static bool IsInsideDirectory(string directory, string path)
    {
        var root = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class ManagedModuleArchiveExtractionResult
{
    public ManagedModuleArchiveExtractionResult(int fileCount, long bytesWritten)
    {
        FileCount = fileCount;
        BytesWritten = bytesWritten;
    }

    public int FileCount { get; }

    public long BytesWritten { get; }
}
