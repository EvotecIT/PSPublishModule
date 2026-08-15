using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

internal static class PowerForgePayloadInventoryHash
{
    internal static string ComputeDirectory(string rootPath, IEnumerable<string>? excludedPaths = null)
    {
        string root = Path.GetFullPath(rootPath);
        var excluded = new HashSet<string>(
            (excludedPaths ?? Array.Empty<string>()).Select(Path.GetFullPath),
            StringComparer.OrdinalIgnoreCase);
        var entries = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .Where(path => !excluded.Contains(path))
            .Select(path => new PayloadEntry(
                DotNetPublishReleaseArtifactVerifier.GetRelativePath(root, path).Replace('\\', '/'),
                new FileInfo(path).Length,
                DotNetPublishReleaseArtifactVerifier.ComputeSha256(path)));
        return Compute(entries);
    }

    internal static string ComputeArchive(
        IReadOnlyDictionary<string, ZipArchiveEntry> entries,
        IEnumerable<string>? excludedPaths = null)
    {
        var excluded = new HashSet<string>(excludedPaths ?? Array.Empty<string>(), StringComparer.Ordinal);
        return Compute(entries
            .Where(entry => !excluded.Contains(entry.Key))
            .Select(entry => new PayloadEntry(entry.Key, entry.Value.Length, ComputeSha256(entry.Value))));
    }

    private static string Compute(IEnumerable<PayloadEntry> entries)
    {
        var canonical = new StringBuilder();
        foreach (PayloadEntry entry in entries.OrderBy(entry => entry.Path, StringComparer.Ordinal))
        {
            Append(canonical, entry.Path);
            Append(canonical, entry.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Append(canonical, entry.Sha256.ToLowerInvariant());
            canonical.Append('\n');
        }
        using SHA256 hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString())))
            .Replace("-", string.Empty)
            .ToLowerInvariant();
    }

    private static string ComputeSha256(ZipArchiveEntry entry)
    {
        using Stream input = entry.Open();
        using SHA256 hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(input)).Replace("-", string.Empty);
    }

    private static void Append(StringBuilder builder, string value)
    {
        builder.Append(value.Length);
        builder.Append(':');
        builder.Append(value);
        builder.Append('|');
    }

    private sealed class PayloadEntry
    {
        internal PayloadEntry(string path, long length, string sha256)
        {
            Path = path;
            Length = length;
            Sha256 = sha256;
        }

        internal string Path { get; }
        internal long Length { get; }
        internal string Sha256 { get; }
    }
}
