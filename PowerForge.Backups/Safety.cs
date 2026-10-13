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

    internal static bool Overlaps(string left, string right)
    {
        var a = Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var b = Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return a.StartsWith(b, comparison) || b.StartsWith(a, comparison);
    }

    internal static void DemandPhysical(string path)
    {
        DemandAbsolute(path);
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("A backup path traverses a reparse point.");
    }

    internal static async Task<string> HashAsync(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, token));
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
        if (!string.Equals(Path.GetDirectoryName(run), Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
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
