using System;
using System.IO;

namespace PowerForge.Web.Cli;

internal static class WebCliFileSystem
{
    internal static void CleanOutputDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var fullPath = Path.GetFullPath(path.Trim().Trim('"'));
        var root = Path.GetPathRoot(fullPath) ?? string.Empty;
        var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedPath = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.IsNullOrWhiteSpace(normalizedRoot) &&
            string.Equals(normalizedRoot, normalizedPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Refusing to clean root path: {fullPath}");

        if (!Directory.Exists(fullPath))
            return;

        foreach (var dir in Directory.GetDirectories(fullPath))
            Directory.Delete(dir, true);
        foreach (var file in Directory.GetFiles(fullPath))
            File.Delete(file);
    }
}
