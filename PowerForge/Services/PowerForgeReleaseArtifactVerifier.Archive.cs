using System.IO.Compression;
using System.Security.Cryptography;

namespace PowerForge;

public sealed partial class PowerForgeReleaseArtifactVerifier
{
    private const long MaxArchiveMetadataBytes = 4L * 1024L * 1024L;
    private const long MaxModuleSignedEntryBytes = 512L * 1024L * 1024L;
    private const long MaxModuleSignedEntriesBytes = 2L * 1024L * 1024L * 1024L;

    private static void VerifyPortableArchiveInventory(
        string projectRoot,
        string checksumsPath,
        string archivePath,
        string outputDirectory)
    {
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        Dictionary<string, ZipArchiveEntry> entries = ValidateArchiveEntries(archive);
        Dictionary<string, string> outputFiles = Directory.EnumerateFiles(outputDirectory, "*", SearchOption.AllDirectories)
            .ToDictionary(
                path => NormalizeArchivePath(
                    DotNetPublishReleaseArtifactVerifier.GetRelativePath(outputDirectory, path).Replace('\\', '/')),
                Path.GetFullPath,
                StringComparer.Ordinal);
        if (outputFiles.Count != entries.Count ||
            outputFiles.Keys.Any(path => !entries.ContainsKey(path)) ||
            entries.Keys.Any(path => !outputFiles.ContainsKey(path)))
            throw Invalid("Portable archive entries do not exactly match the trusted publish output inventory.");

        foreach (KeyValuePair<string, string> outputFile in outputFiles)
        {
            string relative = outputFile.Key;
            string representedPath = outputFile.Value;
            ZipArchiveEntry entry = entries[relative];
            string expectedDigest = VerifyChecksummedFile(
                projectRoot,
                checksumsPath,
                representedPath,
                $"portable output file '{relative}'");
            if (entry.Length != new FileInfo(representedPath).Length ||
                !string.Equals(ComputeSha256(entry), expectedDigest, StringComparison.OrdinalIgnoreCase))
                throw Invalid($"Portable archive contains different bytes for output file '{relative}'.");
        }
    }

    private static Dictionary<string, ZipArchiveEntry> ValidateArchiveEntries(ZipArchive archive)
    {
        var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.Ordinal);
        var duplicateGuard = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string normalized = NormalizeArchivePath(entry.FullName);
            if (!duplicateGuard.Add(normalized))
                throw Invalid($"Release archive contains duplicate entry '{normalized}'.");
            if (normalized.Length == 0 || entry.FullName.EndsWith("/", StringComparison.Ordinal) || entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                continue;
            entries.Add(normalized, entry);
        }
        return entries;
    }

    private static string NormalizeArchivePath(string? value)
    {
        string path = DotNetPublishReleaseArtifactVerifier.RequireText(value, "archive entry path").Replace('\\', '/');
        if (path.StartsWith("/", StringComparison.Ordinal) || Path.IsPathRooted(path))
            throw Invalid($"Release archive contains unsafe entry '{path}'.");
        string[] segments = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment == "." || segment == ".."))
            throw Invalid($"Release archive contains unsafe entry '{path}'.");
        return string.Join("/", segments);
    }

    private static byte[] ReadBoundedEntryBytes(ZipArchiveEntry entry, string label)
    {
        if (entry.Length < 0 || entry.Length > MaxArchiveMetadataBytes)
            throw Invalid($"{label} exceeds the {MaxArchiveMetadataBytes} byte metadata limit.");
        using Stream input = entry.Open();
        using var output = new MemoryStream((int)entry.Length);
        CopyBounded(input, output, MaxArchiveMetadataBytes, label);
        return output.ToArray();
    }

    private static byte[] ReadBoundedFileBytes(string path, string label)
    {
        var info = new FileInfo(path);
        if (info.Length < 0 || info.Length > MaxArchiveMetadataBytes)
            throw Invalid($"{label} exceeds the {MaxArchiveMetadataBytes} byte metadata limit.");
        using FileStream input = File.OpenRead(path);
        using var output = new MemoryStream((int)info.Length);
        CopyBounded(input, output, MaxArchiveMetadataBytes, label);
        return output.ToArray();
    }

    private static void CopyBounded(Stream input, Stream output, long maximumBytes, string label)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            total = checked(total + read);
            if (total > maximumBytes)
                throw Invalid($"{label} exceeds the {maximumBytes} byte limit.");
            output.Write(buffer, 0, read);
        }
    }

    private static string ComputeSha256(ZipArchiveEntry entry)
    {
        using Stream input = entry.Open();
        using SHA256 hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(input)).Replace("-", string.Empty);
    }
}
