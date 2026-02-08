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
    private sealed class PackagingInformation
    {
        public string[] ExcludeFromPackage { get; set; } = Array.Empty<string>();
        public string[] IncludeRoot { get; set; } = Array.Empty<string>();
        public string[] IncludePS1 { get; set; } = Array.Empty<string>();
        public string[] IncludeAll { get; set; } = Array.Empty<string>();
    }

    private static PackagingInformation ResolvePackagingInformation(InformationConfiguration? information, bool includeScriptFolders = true)
    {
        var info = information ?? new InformationConfiguration();

        var includeRoot = (info.IncludeRoot is { Length: > 0 } ? info.IncludeRoot : DefaultIncludeRoot).ToArray();
        var includePS1 = includeScriptFolders
            ? (info.IncludePS1 is { Length: > 0 } ? info.IncludePS1 : DefaultIncludePS1).ToArray()
            : Array.Empty<string>();
        var includeAll = (info.IncludeAll is { Length: > 0 } ? info.IncludeAll : DefaultIncludeAll).ToArray();
        var exclude = (info.ExcludeFromPackage is { Length: > 0 } ? info.ExcludeFromPackage : DefaultExcludeFromPackage).ToArray();

        if (info.IncludeToArray is { Length: > 0 })
        {
            foreach (var entry in info.IncludeToArray.Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Key)))
            {
                if (entry.Values is not { Length: > 0 }) continue;
                if (entry.Key.Equals("IncludeRoot", StringComparison.OrdinalIgnoreCase)) includeRoot = entry.Values;
                if (includeScriptFolders && entry.Key.Equals("IncludePS1", StringComparison.OrdinalIgnoreCase)) includePS1 = entry.Values;
                if (entry.Key.Equals("IncludeAll", StringComparison.OrdinalIgnoreCase)) includeAll = entry.Values;
                if (entry.Key.Equals("ExcludeFromPackage", StringComparison.OrdinalIgnoreCase)) exclude = entry.Values;
            }
        }

        static string[] Normalize(string[] values)
            => (values ?? Array.Empty<string>())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
                .Select(v => v.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToArray();

        return new PackagingInformation
        {
            ExcludeFromPackage = Normalize(exclude),
            IncludeRoot = Normalize(includeRoot),
            IncludePS1 = Normalize(includePS1),
            IncludeAll = Normalize(includeAll),
        };
    }

    private static string ResolveOutputRoot(string? configuredPath, string projectRoot, string moduleName, string moduleVersion, string? preRelease, ArtefactType type)
    {
        var raw = BuildServices.ReplacePathTokens(configuredPath ?? string.Empty, moduleName, moduleVersion, preRelease).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(raw))
        {
            // Default: <ProjectRoot>\Artefacts\<Type>
            return Path.GetFullPath(Path.Combine(projectRoot, "Artefacts", type.ToString()));
        }

        return Path.GetFullPath(Path.IsPathRooted(raw) ? raw : Path.Combine(projectRoot, raw));
    }

    private static string ResolveArtefactFileName(ArtefactConfiguration cfg, string moduleName, string moduleVersion, string? preRelease)
    {
        if (!string.IsNullOrWhiteSpace(cfg.ArtefactName))
            return BuildServices.ReplacePathTokens(cfg.ArtefactName!.Trim(), moduleName, moduleVersion, preRelease);

        var tagWithPre = BuildServices.ReplacePathTokens("<TagModuleVersionWithPreRelease>", moduleName, moduleVersion, preRelease);
        return cfg.IncludeTagName == true
            ? $"{moduleName}.{tagWithPre}.zip"
            : $"{moduleName}.zip";
    }

    private static void CopyModulePackage(string stagingRoot, string destinationModuleRoot, PackagingInformation include)
    {
        var src = Path.GetFullPath(stagingRoot);
        if (!Directory.Exists(src)) throw new DirectoryNotFoundException($"Staging directory not found: {src}");

        if (Directory.Exists(destinationModuleRoot))
            Directory.Delete(destinationModuleRoot, recursive: true);
        Directory.CreateDirectory(destinationModuleRoot);

        var excludes = include.ExcludeFromPackage ?? Array.Empty<string>();

        bool IsExcludedName(string name)
            => WildcardAnyMatch(name, excludes);

        // 1) Root files
        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            if (string.IsNullOrWhiteSpace(name) || IsExcludedName(name)) continue;
            if (!WildcardAnyMatch(name, include.IncludeRoot)) continue;
            File.Copy(file, Path.Combine(destinationModuleRoot, name), overwrite: true);
        }

        // 2) IncludeAll directories
        foreach (var dirName in include.IncludeAll)
        {
            if (string.IsNullOrWhiteSpace(dirName)) continue;
            var dir = Path.Combine(src, dirName);
            if (!Directory.Exists(dir)) continue;

            CopyDirectoryFiltered(
                dir,
                Path.Combine(destinationModuleRoot, dirName),
                include.ExcludeFromPackage ?? Array.Empty<string>(),
                includeOnlyPs1: false,
                excludeDirectories: false);
        }

        // 3) IncludePS1 directories
        foreach (var dirName in include.IncludePS1)
        {
            if (string.IsNullOrWhiteSpace(dirName)) continue;
            var dir = Path.Combine(src, dirName);
            if (!Directory.Exists(dir)) continue;

            CopyDirectoryFiltered(
                dir,
                Path.Combine(destinationModuleRoot, dirName),
                include.ExcludeFromPackage ?? Array.Empty<string>(),
                includeOnlyPs1: true,
                excludeDirectories: true);
        }
    }

    private static void CopyDirectoryFiltered(
        string sourceDir,
        string destDir,
        string[] excludeNamePatterns,
        bool includeOnlyPs1,
        bool excludeDirectories)
    {
        var sourceFull = Path.GetFullPath(sourceDir);
        Directory.CreateDirectory(destDir);

        var stack = new Stack<string>();
        stack.Push(sourceFull);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            var rel = ComputeRelativePath(sourceFull, current);
            var targetDir = string.IsNullOrEmpty(rel) || rel == "." ? destDir : Path.Combine(destDir, rel);
            Directory.CreateDirectory(targetDir);

            foreach (var file in Directory.EnumerateFiles(current, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(file);
                if (string.IsNullOrWhiteSpace(name) || WildcardAnyMatch(name, excludeNamePatterns)) continue;
                if (includeOnlyPs1 && !name.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)) continue;

                var destFile = Path.Combine(targetDir, name);
                File.Copy(file, destFile, overwrite: true);
            }

            foreach (var dir in Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (excludeDirectories && WildcardAnyMatch(name, excludeNamePatterns)) continue;
                stack.Push(dir);
            }
        }
    }

    private static void CopyExtraMappings(
        ArtefactConfiguration cfg,
        string projectRoot,
        string destinationRoot,
        string moduleName,
        string moduleVersion,
        string? preRelease,
        List<ArtefactCopyEntry> copied,
        bool enforceRelativeDestination = false)
    {
        foreach (var mapping in cfg.DirectoryOutput ?? Array.Empty<ArtefactCopyMapping>())
        {
            if (mapping is null) continue;
            var src = ResolveInputPath(mapping.Source, projectRoot, moduleName, moduleVersion, preRelease);
            var dest = ResolveOutputPath(mapping.Destination, destinationRoot, cfg.DestinationDirectoriesRelative == true, enforceRelativeDestination, moduleName, moduleVersion, preRelease);
            CopyDirectory(src, dest);
            copied.Add(new ArtefactCopyEntry(src, dest, isDirectory: true));
        }

        foreach (var mapping in cfg.FilesOutput ?? Array.Empty<ArtefactCopyMapping>())
        {
            if (mapping is null) continue;
            var src = ResolveInputPath(mapping.Source, projectRoot, moduleName, moduleVersion, preRelease);
            var dest = ResolveOutputPath(mapping.Destination, destinationRoot, cfg.DestinationFilesRelative == true, enforceRelativeDestination, moduleName, moduleVersion, preRelease);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(src, dest, overwrite: true);
            copied.Add(new ArtefactCopyEntry(src, dest, isDirectory: false));
        }
    }

    private static string ResolveInputPath(string value, string projectRoot, string moduleName, string moduleVersion, string? preRelease)
    {
        var raw = BuildServices.ReplacePathTokens(value ?? string.Empty, moduleName, moduleVersion, preRelease).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(raw)) throw new ArgumentException("Copy mapping source path is empty.", nameof(value));
        return Path.GetFullPath(Path.IsPathRooted(raw) ? raw : Path.Combine(projectRoot, raw));
    }

    private static string ResolveOutputPath(
        string value,
        string destinationRoot,
        bool relativeToRoot,
        bool enforceRelativeDestination,
        string moduleName,
        string moduleVersion,
        string? preRelease)
    {
        var raw = BuildServices.ReplacePathTokens(value ?? string.Empty, moduleName, moduleVersion, preRelease).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(raw)) throw new ArgumentException("Copy mapping destination path is empty.", nameof(value));

        if (enforceRelativeDestination && Path.IsPathRooted(raw))
            throw new InvalidOperationException($"Packed artefact copy destinations must be relative, but got rooted path '{raw}'.");

        if (relativeToRoot || !Path.IsPathRooted(raw))
            return Path.GetFullPath(Path.Combine(destinationRoot, raw));

        return Path.GetFullPath(raw);
    }

    private static string ResolveRequiredModulesRootForUnpacked(
        ArtefactConfiguration cfg,
        string outputRoot,
        string projectRoot,
        string moduleName,
        string moduleVersion,
        string? preRelease)
    {
        var path = cfg.RequiredModules.Path;
        if (string.IsNullOrWhiteSpace(path))
            return outputRoot;

        var replaced = BuildServices.ReplacePathTokens(path ?? string.Empty, moduleName, moduleVersion, preRelease).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(replaced)) return outputRoot;

        var full = Path.IsPathRooted(replaced) ? replaced : Path.Combine(outputRoot, replaced);
        return Path.GetFullPath(full);
    }

    private static string ResolveModulesRootForUnpacked(
        ArtefactConfiguration cfg,
        string outputRoot,
        string requiredModulesRoot,
        string projectRoot,
        string moduleName,
        string moduleVersion,
        string? preRelease)
    {
        var path = cfg.RequiredModules.ModulesPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            // If RequiredModulesPath is set, default to the same location to keep a self-contained Modules folder.
            return requiredModulesRoot;
        }

        var replaced = BuildServices.ReplacePathTokens(path ?? string.Empty, moduleName, moduleVersion, preRelease).Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(replaced)) return requiredModulesRoot;

        var full = Path.IsPathRooted(replaced) ? replaced : Path.Combine(outputRoot, replaced);
        return Path.GetFullPath(full);
    }

}
