using System.Security.Cryptography;
using System.Text.Json;

namespace PowerForge.Backups;

internal static class Safety
{
    internal static void DemandAbsolute(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
            throw new InvalidDataException("An absolute local path is required.");
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

    internal static void WriteJson(string path, object value)
    {
        DemandPhysical(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, BackupOptions.Json));
        File.Move(temporary, path, true);
    }

    internal static void RemoveOwnedRun(string root, string run)
    {
        DemandPhysical(root);
        DemandPhysical(run);
        if (!string.Equals(Path.GetDirectoryName(run), Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
            !File.Exists(Path.Combine(run, ".powerforge-backup-run")))
            throw new IOException("Refusing to remove an unmarked backup run.");
        foreach (var path in Directory.EnumerateFileSystemEntries(run, "*", SearchOption.AllDirectories)) DemandPhysical(path);
        Directory.Delete(run, true);
    }
}
