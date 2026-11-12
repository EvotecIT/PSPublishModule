using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PowerForge.ModuleLoader.V1;

/// <summary>Human-readable "Name Version" for messages, read from the module folder.</summary>
internal static class ModuleDescription
{
    private static readonly Regex ManifestVersion = new(@"\bModuleVersion\s*=\s*['""]([^'""]+)['""]", RegexOptions.IgnoreCase);

    /// <summary>Describes a module from its name and root folder (a version folder, or the manifest's ModuleVersion).</summary>
    internal static string Describe(string moduleName, string? moduleRoot)
    {
        var version = ReadVersion(moduleRoot, moduleName);
        return version is null ? moduleName : moduleName + " " + version;
    }

    /// <summary>Finds the module that shipped a file: the nearest folder (up to six levels) with a manifest.</summary>
    internal static (string? Name, string Description) DescribeOwner(string filePath)
    {
        try
        {
            var directory = new DirectoryInfo(Path.GetDirectoryName(filePath)!);
            for (var depth = 0; directory is not null && depth < 6; depth++, directory = directory.Parent)
            {
                var manifest = directory.EnumerateFiles("*.psd1").FirstOrDefault();
                if (manifest is null) continue;
                var name = Path.GetFileNameWithoutExtension(manifest.Name);
                return (name, "module " + Describe(name, directory.FullName));
            }
        }
        catch
        {
            // Fall back to the path when the folder cannot be inspected.
        }

        return (null, "another component (" + filePath + ")");
    }

    private static string? ReadVersion(string? moduleRoot, string moduleName)
    {
        if (string.IsNullOrWhiteSpace(moduleRoot)) return null;
        try
        {
            var root = moduleRoot!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (Version.TryParse(Path.GetFileName(root), out var folderVersion)) return folderVersion.ToString();
            var manifest = Path.Combine(root, moduleName + ".psd1");
            if (!File.Exists(manifest)) return null;
            var match = ManifestVersion.Match(File.ReadAllText(manifest));
            return match.Success ? match.Groups[1].Value : null;
        }
        catch
        {
            return null;
        }
    }
}
