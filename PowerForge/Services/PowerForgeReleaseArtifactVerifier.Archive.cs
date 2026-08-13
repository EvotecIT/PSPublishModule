using System.IO.Compression;
using System.Security.Cryptography;

namespace PowerForge;

public sealed partial class PowerForgeReleaseArtifactVerifier
{
    private const long MaxArchiveMetadataBytes = 4L * 1024L * 1024L;
    private const long MaxModuleSignedEntryBytes = 512L * 1024L * 1024L;
    private const long MaxModuleSignedEntriesBytes = 2L * 1024L * 1024L * 1024L;

    private static void VerifyArchiveContainsFile(
        string archivePath,
        string outputDirectory,
        string representedPath,
        string expectedDigest)
    {
        string relative = DotNetPublishReleaseArtifactVerifier.GetRelativePath(outputDirectory, representedPath).Replace('\\', '/');
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        Dictionary<string, ZipArchiveEntry> entries = ValidateArchiveEntries(archive);
        if (!entries.TryGetValue(NormalizeArchivePath(relative), out ZipArchiveEntry? entry) || entry.Length == 0)
            throw Invalid($"Portable archive does not contain signed file '{relative}'.");
        long representedLength = new FileInfo(representedPath).Length;
        if (entry.Length != representedLength)
            throw Invalid($"Portable archive contains different bytes for signed file '{relative}'.");
        string digest = ComputeSha256(entry);
        if (!string.Equals(digest, expectedDigest, StringComparison.OrdinalIgnoreCase))
            throw Invalid($"Portable archive contains different bytes for signed file '{relative}'.");
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
