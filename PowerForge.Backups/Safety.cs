using System.Security.Cryptography;
using System.Text.Json;

namespace PowerForge.Backups;

internal static class Safety
{
    internal static void DemandAbsolute(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidDataException("An absolute local path is required.");
        if (OperatingSystem.IsWindows())
        {
            var full = Path.GetFullPath(path).Replace('/', '\\');
            if (full.StartsWith(@"\\?\", StringComparison.Ordinal) || full.StartsWith(@"\\.\", StringComparison.Ordinal) ||
                full.StartsWith(@"\??\", StringComparison.Ordinal))
                throw new InvalidDataException("Windows device and NT namespace paths are not supported.");
        }
    }

    /// <summary>How paths compare on this platform: Windows ignores case, Linux and macOS work roots may differ only by case.</summary>
    internal static StringComparison PathComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    internal static bool Overlaps(string left, string right)
    {
        var a = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var b = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return a.StartsWith(b, PathComparison) || b.StartsWith(a, PathComparison);
    }

    internal static void DemandPhysical(string path)
    {
        DemandAbsolute(path);
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            // One read, not an existence check followed by a read: a live SQLite database creates and removes its
            // -journal, -wal and -shm sidecars at any moment, and a sidecar gone between the two calls threw.
            FileAttributes attributes;
            try { attributes = File.GetAttributes(current); }
            catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { continue; }
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("A backup path traverses a reparse point.");
        }
    }

    internal static async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token));
    }

    // How often a long read or copy reports the bytes done, so a view never looks frozen.
    internal static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(500);

    internal static async Task<string> HashAsync(string path, Action<long> progress, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await PumpAsync(stream, hash.AppendData, progress, token);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    internal static async Task CopyAsync(string source, string destination, Action<long> progress, CancellationToken token)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.Asynchronous);
        await PumpAsync(input, chunk => output.Write(chunk), progress, token);
    }

    private static async Task PumpAsync(Stream input, Action<ReadOnlySpan<byte>> consume, Action<long> progress, CancellationToken token)
    {
        var buffer = new byte[4 << 20];
        long done = 0;
        var reported = System.Diagnostics.Stopwatch.StartNew();
        int read;
        while ((read = await input.ReadAsync(buffer, token)) > 0)
        {
            consume(buffer.AsSpan(0, read));
            done += read;
            if (reported.Elapsed < ProgressInterval) continue;
            progress(done);
            reported.Restart();
        }
        progress(done);
    }

    internal static void DemandFreeSpace(string path, long reserve, long requiredBytes = 0)
    {
        if (reserve < 0 || requiredBytes < 0 || requiredBytes > long.MaxValue - reserve ||
            new DriveInfo(Path.GetPathRoot(path)!).AvailableFreeSpace < reserve + requiredBytes)
            throw new IOException("Insufficient free staging space.");
    }

    internal static void WriteJson(string path, object value)
    {
        DemandPhysical(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(output, value, BackupOptions.Json);
            output.Flush(true);
        }
        File.Move(temporary, path, true);
    }

    internal static long RemoveOwnedRun(string root, string run)
    {
        DemandPhysical(root);
        DemandPhysical(run);
        if (!string.Equals(Path.GetDirectoryName(run), Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), PathComparison) ||
            !File.Exists(Path.Combine(run, ".powerforge-backup-run")) ||
            File.ReadAllText(Path.Combine(run, ".powerforge-backup-run")).Trim() != "powerforge-database-backup-v1")
            throw new IOException("Refusing to remove an unmarked backup run.");
        long bytes = 0;
        var pending = new Stack<string>(); pending.Push(run);
        while (pending.TryPop(out var directory))
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                DemandPhysical(path);
                if (Directory.Exists(path)) pending.Push(path);
                else bytes += new FileInfo(path).Length;
            }
        Directory.Delete(run, true);
        return bytes;
    }
}
