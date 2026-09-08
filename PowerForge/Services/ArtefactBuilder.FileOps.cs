using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PowerForge;

public sealed partial class ArtefactBuilder
{
    private static readonly DateTimeOffset DeterministicZipEntryTimestamp =
        new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static void CopyDirectory(string sourceDir, string destDir, bool excludeBuildHostMetadata = false)
    {
        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException($"Directory not found: {sourceDir}");

        if (Directory.Exists(destDir))
            Directory.Delete(destDir, recursive: true);

        Directory.CreateDirectory(destDir);

        foreach (var dir in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = ComputeRelativePath(sourceDir, dir);
            if (excludeBuildHostMetadata && ModuleDependencyPackageFilter.IsBuildHostMetadataPath(rel))
                continue;

            Directory.CreateDirectory(Path.Combine(destDir, rel));
        }
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = ComputeRelativePath(sourceDir, file);
            if (excludeBuildHostMetadata && ModuleDependencyPackageFilter.IsBuildHostMetadataPath(rel))
                continue;

            var outPath = Path.Combine(destDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.Copy(file, outPath, overwrite: true);
        }
    }

    internal static void CreateZipFromDirectoryContents(
        string sourceDir,
        string zipPath,
        IReadOnlyCollection<string>? executableEntryNames = null)
        => CreateZipFromDirectoryContents(sourceDir, zipPath, executableEntryNames, entryTimestamp: null);

    internal static void CreateDeterministicZipFromDirectoryContents(
        string sourceDir,
        string zipPath,
        IReadOnlyCollection<string>? executableEntryNames = null)
        => CreateZipFromDirectoryContents(
            sourceDir,
            zipPath,
            executableEntryNames,
            DeterministicZipEntryTimestamp);

    private static void CreateZipFromDirectoryContents(
        string sourceDir,
        string zipPath,
        IReadOnlyCollection<string>? executableEntryNames,
        DateTimeOffset? entryTimestamp)
    {
        var unixFileModes = new Dictionary<string, int>(StringComparer.Ordinal);
        if (File.Exists(zipPath)) File.Delete(zipPath);
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);

        using (var fs = File.Create(zipPath))
        using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            foreach (var file in Directory
                         .EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories)
                         .OrderBy(file => ComputeRelativePath(sourceDir, file), StringComparer.Ordinal))
            {
                var rel = ComputeRelativePath(sourceDir, file).Replace('\\', '/');
#if NET8_0_OR_GREATER
                if (!OperatingSystem.IsWindows())
                {
                    UnixFileMode mode = File.GetUnixFileMode(file);
                    const UnixFileMode executeBits =
                        UnixFileMode.UserExecute |
                        UnixFileMode.GroupExecute |
                        UnixFileMode.OtherExecute;
                    if ((mode & executeBits) != 0)
                        unixFileModes[rel] = (int)mode;
                }
#endif
                var entry = zip.CreateEntry(rel, CompressionLevel.Optimal);
                if (entryTimestamp.HasValue)
                    entry.LastWriteTime = entryTimestamp.Value;
                using var entryStream = entry.Open();
                using var fileStream = File.OpenRead(file);
                fileStream.CopyTo(entryStream);
            }
        }

        var executableEntries = (executableEntryNames ?? Array.Empty<string>())
            .Where(static entry => !string.IsNullOrWhiteSpace(entry))
            .Select(static entry => entry.Replace('\\', '/').TrimStart('/'))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        foreach (string executableEntry in executableEntries)
            unixFileModes[executableEntry] = 0x1ED;
        if (unixFileModes.Count > 0)
            ZipArchiveUnixPermissionPatcher.ApplyUnixFilePermissions(zipPath, unixFileModes);
    }

    private static void ClearDirectorySafe(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));

        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        if (string.Equals(full.TrimEnd(Path.DirectorySeparatorChar), (root ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Refusing to clear root directory: {full}");

        if (Directory.Exists(full))
            Directory.Delete(full, recursive: true);

        Directory.CreateDirectory(full);
    }

    private static void ClearDirectoryContentsSafe(
        string path,
        IEnumerable<string>? excludePatterns = null,
        bool includeDirectories = true)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));

        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        if (string.Equals(full.TrimEnd(Path.DirectorySeparatorChar), (root ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Refusing to clear root directory contents: {full}");

        if (!Directory.Exists(full)) return;

        var excludes = (excludePatterns ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .ToArray();

        foreach (var entry in Directory.EnumerateFileSystemEntries(full))
        {
            try
            {
                var name = Path.GetFileName(entry);
                if (!string.IsNullOrWhiteSpace(name) && WildcardAnyMatch(name, excludes))
                    continue;

                if (Directory.Exists(entry))
                {
                    if (includeDirectories) Directory.Delete(entry, recursive: true);
                    continue;
                }
                else File.Delete(entry);
            }
            catch { /* best effort */ }
        }
    }

    private static bool WildcardAnyMatch(string text, IEnumerable<string> patterns)
        => (patterns ?? Array.Empty<string>()).Any(p => WildcardMatch(text, p));

    private static bool WildcardMatch(string text, string pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return false;
        if (pattern == "*") return true;

        var regex = "^" + Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        return Regex.IsMatch(text ?? string.Empty, regex, RegexOptions.IgnoreCase);
    }

    private static string ComputeRelativePath(string baseDir, string fullPath)
    {
        try
        {
            var baseUri = new Uri(AppendDirectorySeparatorChar(Path.GetFullPath(baseDir)));
            var pathUri = new Uri(Path.GetFullPath(fullPath));
            var rel = Uri.UnescapeDataString(baseUri.MakeRelativeUri(pathUri).ToString());
            return rel.Replace('/', Path.DirectorySeparatorChar);
        }
        catch { return Path.GetFileName(fullPath) ?? fullPath; }
    }

    private static string AppendDirectorySeparatorChar(string path)
        => path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;
}
