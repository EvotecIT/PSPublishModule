using System;
using System.IO;

namespace PowerForge.Web.Cli;

/// <summary>Compares IndexNow file paths using the case behavior of the target volume.</summary>
internal static class IndexNowFilePathIdentity
{
    internal static bool MayReferToSameFile(string firstPath, string secondPath)
    {
        var first = ResolveExistingLinks(firstPath);
        var second = ResolveExistingLinks(secondPath);
        if (string.Equals(first, second, StringComparison.Ordinal))
            return true;
        if (!string.Equals(first, second, StringComparison.OrdinalIgnoreCase))
            return false;

        // The first component whose spelling differs determines whether the paths alias.
        // On Windows, case sensitivity can be configured per directory, not just per volume.
        var difference = 0;
        while (difference < first.Length && first[difference] == second[difference]) difference++;
        var separator = first.LastIndexOf(Path.DirectorySeparatorChar, difference);
        var directory = separator < 0 ? Path.GetPathRoot(first) : first[..(separator + 1)];
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return true; // Unknown directory behavior: reject the potentially destructive alias.

        var probeName = ".powerforge-indexnow-case-" + Guid.NewGuid().ToString("N") + "a";
        var probePath = Path.Combine(directory, probeName);
        try
        {
            using (new FileStream(probePath, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
            return File.Exists(Path.Combine(directory, probeName.ToUpperInvariant()));
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return true;
        }
        finally
        {
            if (File.Exists(probePath))
                File.Delete(probePath);
        }
    }

    private static string ResolveExistingLinks(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath) ?? throw new InvalidOperationException("indexnow: path has no root.");
        var segments = fullPath[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        for (var index = 0; index < segments.Length; index++)
        {
            current = Path.Combine(current, segments[index]);
            if (index < segments.Length - 1 && Directory.Exists(current))
            {
                var info = new DirectoryInfo(current);
                if (info.LinkTarget is not null)
                    current = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ??
                        throw new InvalidOperationException("indexnow: directory link cannot be resolved.");
            }
            else if (index == segments.Length - 1 && File.Exists(current))
            {
                var info = new FileInfo(current);
                if (info.LinkTarget is not null)
                    current = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ??
                        throw new InvalidOperationException("indexnow: file link cannot be resolved.");
            }
        }

        return Path.GetFullPath(current);
    }
}
