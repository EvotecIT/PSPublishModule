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

    private static PackagingInformation ResolvePackagingInformation(
        InformationConfiguration? information,
        DeliveryOptionsConfiguration? delivery,
        bool includeScriptFolders = true)
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
            IncludeAll = MergeDeliveryIncludeAll(Normalize(includeAll), delivery),
        };
    }

    internal static string[] ResolveModulePackageSourceFiles(
        string stagingRoot,
        InformationConfiguration? information,
        DeliveryOptionsConfiguration? delivery,
        bool includeScriptFolders = true,
        IReadOnlyList<string>? finalizedPayloadFiles = null)
    {
        if (finalizedPayloadFiles is { Count: > 0 })
            return ValidateFinalizedPayloadFiles(stagingRoot, finalizedPayloadFiles);

        var include = ResolvePackagingInformation(information, delivery, includeScriptFolders);
        return EnumerateModulePackageFiles(stagingRoot, include);
    }

    private static string[] MergeDeliveryIncludeAll(string[] includeAll, DeliveryOptionsConfiguration? delivery)
    {
        if (delivery?.Enable != true)
            return includeAll ?? Array.Empty<string>();

        var internalsPath = string.IsNullOrWhiteSpace(delivery.InternalsPath)
            ? "Internals"
            : delivery.InternalsPath.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.IsNullOrWhiteSpace(internalsPath))
            return includeAll ?? Array.Empty<string>();

        var merged = new List<string>((includeAll ?? Array.Empty<string>()).Length + 1);
        merged.AddRange(includeAll ?? Array.Empty<string>());

        if (!merged.Any(x => string.Equals(x, internalsPath, StringComparison.OrdinalIgnoreCase)))
            merged.Add(internalsPath);

        return merged.ToArray();
    }

    private static string ResolveOutputRoot(string? configuredPath, string projectRoot, string moduleName, string moduleVersion, string? preRelease, ArtefactType type)
        => ArtefactLayoutPathResolver.ResolveOutputRoot(configuredPath, projectRoot, moduleName, moduleVersion, preRelease, type);

    internal static string ResolveArtefactFileName(ArtefactConfiguration cfg, string moduleName, string moduleVersion, string? preRelease)
    {
        if (!string.IsNullOrWhiteSpace(cfg.ArtefactName))
            return ModulePathTokenFormatter.ReplacePathTokens(cfg.ArtefactName!.Trim(), moduleName, moduleVersion, preRelease);

        var tagWithPre = ModulePathTokenFormatter.ReplacePathTokens("<TagModuleVersionWithPreRelease>", moduleName, moduleVersion, preRelease);
        return cfg.IncludeTagName == true
            ? $"{moduleName}.{tagWithPre}.zip"
            : $"{moduleName}.zip";
    }

    private static void CopyModulePackage(
        string stagingRoot,
        string destinationModuleRoot,
        PackagingInformation include,
        IReadOnlyList<string>? finalizedPayloadFiles = null)
    {
        var src = Path.GetFullPath(stagingRoot);

        if (Directory.Exists(destinationModuleRoot))
            Directory.Delete(destinationModuleRoot, recursive: true);
        Directory.CreateDirectory(destinationModuleRoot);
        var sourceFiles = finalizedPayloadFiles is { Count: > 0 }
            ? ValidateFinalizedPayloadFiles(src, finalizedPayloadFiles)
            : EnumerateModulePackageFiles(src, include);
        if (finalizedPayloadFiles is not { Count: > 0 })
            CreatePackageDirectoryStructure(src, destinationModuleRoot, include);

        foreach (var file in sourceFiles)
        {
            var relativePath = ComputeRelativePath(src, file);
            var destinationPath = Path.Combine(destinationModuleRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath, overwrite: true);
        }
    }

    private static string[] ValidateFinalizedPayloadFiles(
        string stagingRoot,
        IReadOnlyList<string> finalizedPayloadFiles)
    {
        var root = Path.GetFullPath(stagingRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Staging directory not found: {root}");

        var comparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var seen = new HashSet<string>(CreateCurrentFileSystemPathComparer());
        var validated = new List<string>();
        foreach (var candidate in finalizedPayloadFiles)
        {
            if (string.IsNullOrWhiteSpace(candidate))
                throw new InvalidOperationException("The finalized module payload contains an empty path.");
            var fullPath = Path.GetFullPath(candidate);
            if (!fullPath.StartsWith(rootPrefix, comparison) || !File.Exists(fullPath))
                throw new InvalidOperationException($"Finalized module payload file is missing or outside staging: '{fullPath}'.");
            EnsureNoReparsePoints(root, fullPath);
            if (seen.Add(fullPath))
                validated.Add(fullPath);
        }

        return validated
            .OrderBy(path => ComputeRelativePath(root, path), StringComparer.Ordinal)
            .ToArray();
    }

    private static void EnsureNoReparsePoints(string stagingRoot, string filePath)
    {
        var current = new FileInfo(filePath).Directory;
        while (current is not null)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException($"Finalized module payload does not permit symbolic links or junctions: '{filePath}'.");
            if (Path.GetFullPath(current.FullName).Equals(
                    Path.GetFullPath(stagingRoot),
                    Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                break;
            current = current.Parent;
        }
        if ((File.GetAttributes(filePath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException($"Finalized module payload does not permit symbolic links or junctions: '{filePath}'.");
    }

    private static void CreatePackageDirectoryStructure(
        string stagingRoot,
        string destinationModuleRoot,
        PackagingInformation include)
    {
        foreach (var dirName in include.IncludeAll)
        {
            if (string.IsNullOrWhiteSpace(dirName)) continue;
            var sourceDir = Path.Combine(stagingRoot, dirName);
            if (!Directory.Exists(sourceDir)) continue;
            CreateDirectoryTree(sourceDir, Path.Combine(destinationModuleRoot, dirName), Array.Empty<string>());
        }

        foreach (var dirName in include.IncludePS1)
        {
            if (string.IsNullOrWhiteSpace(dirName)) continue;
            var sourceDir = Path.Combine(stagingRoot, dirName);
            if (!Directory.Exists(sourceDir)) continue;
            CreateDirectoryTree(
                sourceDir,
                Path.Combine(destinationModuleRoot, dirName),
                include.ExcludeFromPackage ?? Array.Empty<string>());
        }
    }

    private static void CreateDirectoryTree(
        string sourceRoot,
        string destinationRoot,
        string[] excludedDirectoryPatterns)
    {
        Directory.CreateDirectory(destinationRoot);
        var stack = new Stack<string>();
        stack.Push(Path.GetFullPath(sourceRoot));
        while (stack.Count > 0)
        {
            var current = stack.Pop();
            foreach (var directory in Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(directory);
                if (string.IsNullOrWhiteSpace(name) || WildcardAnyMatch(name, excludedDirectoryPatterns))
                    continue;

                var relativePath = ComputeRelativePath(sourceRoot, directory);
                Directory.CreateDirectory(Path.Combine(destinationRoot, relativePath));
                stack.Push(directory);
            }
        }
    }

    private static string[] EnumerateModulePackageFiles(string stagingRoot, PackagingInformation include)
    {
        var src = Path.GetFullPath(stagingRoot);
        if (!Directory.Exists(src)) throw new DirectoryNotFoundException($"Staging directory not found: {src}");

        var excludes = include.ExcludeFromPackage ?? Array.Empty<string>();
        var files = new List<string>();
        var seen = new HashSet<string>(CreateCurrentFileSystemPathComparer());

        void AddFile(string file)
        {
            var fullPath = Path.GetFullPath(file);
            if (seen.Add(fullPath))
                files.Add(fullPath);
        }

        foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(file);
            if (string.IsNullOrWhiteSpace(name) || WildcardAnyMatch(name, excludes)) continue;
            if (!WildcardAnyMatch(name, include.IncludeRoot)) continue;
            AddFile(file);
        }

        foreach (var dirName in include.IncludeAll)
        {
            if (string.IsNullOrWhiteSpace(dirName)) continue;
            var dir = Path.Combine(src, dirName);
            if (!Directory.Exists(dir)) continue;

            AddDirectoryFiles(
                dir,
                include.ExcludeFromPackage ?? Array.Empty<string>(),
                includeOnlyPs1: false,
                excludeDirectories: false,
                AddFile);
        }

        foreach (var dirName in include.IncludePS1)
        {
            if (string.IsNullOrWhiteSpace(dirName)) continue;
            var dir = Path.Combine(src, dirName);
            if (!Directory.Exists(dir)) continue;

            AddDirectoryFiles(
                dir,
                include.ExcludeFromPackage ?? Array.Empty<string>(),
                includeOnlyPs1: true,
                excludeDirectories: true,
                AddFile);
        }

        return files.ToArray();
    }

    private static StringComparer CreateCurrentFileSystemPathComparer()
        => Path.DirectorySeparatorChar == '\\'
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    private static void AddDirectoryFiles(
        string sourceDir,
        string[] excludeNamePatterns,
        bool includeOnlyPs1,
        bool excludeDirectories,
        Action<string> addFile)
    {
        var sourceFull = Path.GetFullPath(sourceDir);

        var stack = new Stack<string>();
        stack.Push(sourceFull);

        while (stack.Count > 0)
        {
            var current = stack.Pop();

            foreach (var file in Directory.EnumerateFiles(current, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(file);
                if (string.IsNullOrWhiteSpace(name) || WildcardAnyMatch(name, excludeNamePatterns)) continue;
                if (includeOnlyPs1 && !name.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)) continue;
                addFile(file);
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
        var raw = ModulePathTokenFormatter.ReplacePathTokens(value ?? string.Empty, moduleName, moduleVersion, preRelease).Trim().Trim('"');
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
        var raw = ModulePathTokenFormatter.ReplacePathTokens(value ?? string.Empty, moduleName, moduleVersion, preRelease).Trim().Trim('"');
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
        => ArtefactLayoutPathResolver.ResolveRequiredModulesRootForUnpacked(cfg, outputRoot, moduleName, moduleVersion, preRelease);

    private static string ResolveModulesRootForUnpacked(
        ArtefactConfiguration cfg,
        string outputRoot,
        string requiredModulesRoot,
        string projectRoot,
        string moduleName,
        string moduleVersion,
        string? preRelease)
        => ArtefactLayoutPathResolver.ResolveModulesRootForUnpacked(cfg, outputRoot, requiredModulesRoot, moduleName, moduleVersion, preRelease);

    private static string ResolveRequiredModulesRootForPacked(
        ArtefactConfiguration cfg,
        string outputRoot,
        string packedRoot,
        string moduleName,
        string moduleVersion,
        string? preRelease)
        => ArtefactLayoutPathResolver.ResolveRequiredModulesRootForPacked(
            cfg,
            outputRoot,
            packedRoot,
            moduleName,
            moduleVersion,
            preRelease);

    private static string ResolveModulesRootForPacked(
        ArtefactConfiguration cfg,
        string outputRoot,
        string packedRoot,
        string requiredModulesRoot,
        string moduleName,
        string moduleVersion,
        string? preRelease)
        => ArtefactLayoutPathResolver.ResolveModulesRootForPacked(
            cfg,
            outputRoot,
            packedRoot,
            requiredModulesRoot,
            moduleName,
            moduleVersion,
            preRelease);

}
