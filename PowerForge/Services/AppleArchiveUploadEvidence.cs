using System.Security.Cryptography;
using System.Text.Json;

namespace PowerForge;

/// <summary>Bounded, non-authorizing diagnostics for a rejected private upload input.</summary>
internal sealed class AppleArchiveUploadEvidence
{
    private const int MaximumEntries = 20000;
    private const int MaximumDeltas = 256;

    private AppleArchiveUploadEvidence(string path, string sha256)
    {
        Path = path;
        Sha256 = sha256;
    }

    internal string Path { get; }
    internal string Sha256 { get; }

    internal static AppleArchiveUploadEvidence Write(
        string root,
        string archive,
        string approvedSha256,
        Manifest beforeManifest,
        string detail)
    {
        var afterManifest = CaptureEntries(archive);
        var before = beforeManifest.Entries;
        var after = afterManifest.Entries;
        var previous = before.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        var current = after.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        var changed = previous.Keys.Concat(current.Keys).Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Where(path => !previous.TryGetValue(path, out var oldEntry) ||
                           !current.TryGetValue(path, out var newEntry) ||
                           JsonSerializer.Serialize(oldEntry) != JsonSerializer.Serialize(newEntry))
            .ToArray();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            SchemaVersion = 1,
            RetainedAtUtc = DateTimeOffset.UtcNow,
            ArchivePath = archive,
            ApprovedArchiveSha256 = approvedSha256,
            Failure = detail,
            BeforeTruncated = beforeManifest.Truncated,
            AfterTruncated = afterManifest.Truncated,
            Before = before,
            After = after,
            ChangedEntryCount = changed.Length,
            DeltasTruncated = changed.Length > MaximumDeltas,
            Deltas = changed.Take(MaximumDeltas).Select(path => new
            {
                Path = path,
                Before = previous.TryGetValue(path, out var oldEntry) ? oldEntry : null,
                After = current.TryGetValue(path, out var newEntry) ? newEntry : null
            }),
            Cleanup = new
            {
                OwnedRootPath = root,
                AutomaticDeletionAllowed = false,
                Instruction = "Retain until remote delivery and integrity failure are investigated. Verify ownership, containment, aliases and open handles before deliberate cleanup. Never use this snapshot as release evidence."
            }
        }, new JsonSerializerOptions { WriteIndented = true });
        var path = System.IO.Path.Combine(root, "upload-integrity-failure.json");
        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            stream.Write(payload, 0, payload.Length);
        using var sha = SHA256.Create();
        return new AppleArchiveUploadEvidence(path, ToHex(sha.ComputeHash(payload)));
    }

    internal static Manifest CaptureEntries(string archive)
    {
        var entries = new List<Entry>();
        var truncated = false;
        var files = new List<(string Path, Entry Entry)>();
        var pending = new Stack<string>();
        pending.Push(archive);
        while (pending.Count > 0)
        {
            if (entries.Count >= MaximumEntries)
            {
                truncated = true;
                break;
            }
            var path = pending.Pop();
            var relative = FrameworkCompatibility.GetRelativePath(archive, path).Replace('\\', '/');
            var attributes = File.GetAttributes(path);
            var linked = (attributes & FileAttributes.ReparsePoint) != 0;
            var directory = (attributes & FileAttributes.Directory) != 0;
            var entry = new Entry { Path = relative, Attributes = (int)attributes };
            entries.Add(entry);
            if (linked)
            {
#if NET8_0_OR_GREATER
                entry.LinkTarget = new FileInfo(path).LinkTarget ?? new DirectoryInfo(path).LinkTarget;
#endif
                entry.Kind = "link (not followed)";
                continue;
            }
#if NET8_0_OR_GREATER
            if (!OperatingSystem.IsWindows())
                entry.Mode = (int)File.GetUnixFileMode(path);
#endif
            if (directory)
            {
                entry.Kind = "directory";
                entry.PhysicalIdentity = ExistingFilePathIdentityResolver.ResolveDirectoryStatus(path).MutationIdentity;
                foreach (var child in Directory.EnumerateFileSystemEntries(path))
                {
                    if (entries.Count + pending.Count >= MaximumEntries)
                    {
                        truncated = true;
                        break;
                    }
                    pending.Push(child);
                }
                continue;
            }
#if NET8_0_OR_GREATER
            if (!OperatingSystem.IsWindows() && !ExistingFilePathIdentityResolver.IsRegularUnixFile(path))
            {
                entry.Kind = "unsupported (not opened)";
                continue;
            }
#endif
            files.Add((path, entry));
        }
        var linkCounts = ExistingFilePathIdentityResolver.ResolveHardLinkCounts(files.Select(file => file.Path).ToArray());
        for (var index = 0; index < files.Count; index++)
        {
            var (path, entry) = files[index];
            // Recheck after traversal before opening; an injected symlink is diagnostic data only.
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("A private upload diagnostic entry was replaced with a link; the snapshot remains retained.");
            var links = linkCounts[index];
            entry.HardLinkCount = links;
            if (links != 1)
            {
                entry.Kind = "hard-link alias (not opened)";
                continue;
            }
            entry.Kind = "file";
            entry.PhysicalIdentity = ExistingFilePathIdentityResolver.ResolveStatus(path).MutationIdentity;
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            entry.Length = stream.Length;
            using var hash = SHA256.Create();
            entry.Sha256 = ToHex(hash.ComputeHash(stream));
            if (!entry.PhysicalIdentity.Equals(ExistingFilePathIdentityResolver.ResolveStatus(path).MutationIdentity, StringComparison.Ordinal))
                throw new InvalidOperationException("A private upload diagnostic entry changed while it was being captured; the snapshot remains retained.");
        }
        return new Manifest(entries.OrderBy(entry => entry.Path, StringComparer.Ordinal).ToArray(), truncated);
    }

    private static string ToHex(byte[] value) => BitConverter.ToString(value).Replace("-", string.Empty).ToLowerInvariant();

    internal sealed class Manifest
    {
        internal Manifest(Entry[] entries, bool truncated)
        {
            Entries = entries;
            Truncated = truncated;
        }

        internal Entry[] Entries { get; }
        internal bool Truncated { get; }
    }

    internal sealed class Entry
    {
        public string Path { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public int Attributes { get; set; }
        public int? Mode { get; set; }
        public string? LinkTarget { get; set; }
        public long? HardLinkCount { get; set; }
        public long? Length { get; set; }
        public string? Sha256 { get; set; }
        public string? PhysicalIdentity { get; set; }
    }
}
