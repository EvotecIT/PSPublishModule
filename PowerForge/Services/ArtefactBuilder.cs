using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Creates packed/unpacked artefacts for a built module using typed configuration segments.
/// </summary>
public sealed class ArtefactBuilder
{
    private static readonly string[] DefaultExcludeFromPackage = { ".*", "Ignore", "Examples", "package.json", "Publish", "Docs" };
    private static readonly string[] DefaultIncludeRoot = { "*.psm1", "*.psd1", "License*" };
    private static readonly string[] DefaultIncludePS1 = { "Private", "Public", "Enums", "Classes" };
    private static readonly string[] DefaultIncludeAll = { "Images", "Resources", "Templates", "Bin", "Lib", "Data" };

    private readonly ILogger _logger;
    private bool _skipPsResourceGetSave;

    /// <summary>
    /// Creates a new builder that logs progress via <paramref name="logger"/>.
    /// </summary>
    public ArtefactBuilder(ILogger logger) => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Builds a single artefact described by <paramref name="segment"/> using the built module from <paramref name="stagingPath"/>.
    /// </summary>
    /// <param name="segment">Artefact configuration segment.</param>
    /// <param name="projectRoot">Project root used for resolving relative paths.</param>
    /// <param name="stagingPath">Path to the built module staging folder.</param>
    /// <param name="moduleName">Module name.</param>
    /// <param name="moduleVersion">Resolved module version (without prerelease).</param>
    /// <param name="preRelease">Optional prerelease tag.</param>
    /// <param name="requiredModules">Required modules from configuration (used when AddRequiredModules is enabled).</param>
    /// <param name="information">Optional include/exclude configuration for packaging.</param>
    public ArtefactBuildResult Build(
        ConfigurationArtefactSegment segment,
        string projectRoot,
        string stagingPath,
        string moduleName,
        string moduleVersion,
        string? preRelease,
        IReadOnlyList<ManifestEditor.RequiredModule> requiredModules,
        InformationConfiguration? information = null)
    {
        if (segment is null) throw new ArgumentNullException(nameof(segment));
        if (string.IsNullOrWhiteSpace(projectRoot)) throw new ArgumentException("ProjectRoot is required.", nameof(projectRoot));
        if (string.IsNullOrWhiteSpace(stagingPath)) throw new ArgumentException("StagingPath is required.", nameof(stagingPath));
        if (string.IsNullOrWhiteSpace(moduleName)) throw new ArgumentException("ModuleName is required.", nameof(moduleName));
        if (string.IsNullOrWhiteSpace(moduleVersion)) throw new ArgumentException("ModuleVersion is required.", nameof(moduleVersion));

        var cfg = segment.Configuration ?? new ArtefactConfiguration();
        if (cfg.Enabled != true)
            throw new InvalidOperationException($"Artefact '{segment.ArtefactType}' is not enabled.");

        var root = ResolveOutputRoot(cfg.Path, projectRoot, moduleName, moduleVersion, preRelease, segment.ArtefactType);

        return segment.ArtefactType switch
        {
            ArtefactType.Unpacked => BuildUnpacked(cfg, root, projectRoot, stagingPath, moduleName, moduleVersion, preRelease, requiredModules, information),
            ArtefactType.Packed => BuildPacked(cfg, root, projectRoot, stagingPath, moduleName, moduleVersion, preRelease, requiredModules, information),
            _ => throw new NotSupportedException($"Artefact type '{segment.ArtefactType}' is not supported yet.")
        };
    }

    private ArtefactBuildResult BuildUnpacked(
        ArtefactConfiguration cfg,
        string outputRoot,
        string projectRoot,
        string stagingPath,
        string moduleName,
        string moduleVersion,
        string? preRelease,
        IReadOnlyList<ManifestEditor.RequiredModule> requiredModules,
        InformationConfiguration? information)
    {
        if (cfg.DoNotClear != true)
            ClearDirectorySafe(outputRoot);
        Directory.CreateDirectory(outputRoot);

        var include = ResolvePackagingInformation(information);

        var requiredRoot = ResolveRequiredModulesRootForUnpacked(cfg, outputRoot, projectRoot, moduleName, moduleVersion, preRelease);
        var modulesRoot = ResolveModulesRootForUnpacked(cfg, outputRoot, requiredRoot, projectRoot, moduleName, moduleVersion, preRelease);

        var copied = new List<ArtefactCopyEntry>();
        var modules = new List<ArtefactModuleEntry>();

        var mainModuleDest = Path.Combine(modulesRoot, moduleName);
        _logger.Info($"Creating unpacked artefact at '{outputRoot}'");
        CopyModulePackage(stagingPath, mainModuleDest, include);
        modules.Add(new ArtefactModuleEntry(moduleName, isMainModule: true, version: moduleVersion, path: mainModuleDest));

        if (cfg.RequiredModules.Enabled == true)
        {
            foreach (var rm in (requiredModules ?? Array.Empty<ManifestEditor.RequiredModule>()).Where(m => !string.IsNullOrWhiteSpace(m.ModuleName)))
            {
                var depEntry = SaveRequiredModuleToFolder(rm, requiredRoot);
                modules.Add(depEntry);
            }
        }

        CopyExtraMappings(
            cfg,
            projectRoot,
            outputRoot,
            moduleName,
            moduleVersion,
            preRelease,
            copied);

        return new ArtefactBuildResult(ArtefactType.Unpacked, cfg.ID, outputRoot, modules.ToArray(), copied.ToArray());
    }

    private ArtefactBuildResult BuildPacked(
        ArtefactConfiguration cfg,
        string outputRoot,
        string projectRoot,
        string stagingPath,
        string moduleName,
        string moduleVersion,
        string? preRelease,
        IReadOnlyList<ManifestEditor.RequiredModule> requiredModules,
        InformationConfiguration? information)
    {
        Directory.CreateDirectory(outputRoot);
        if (cfg.DoNotClear != true)
            ClearDirectoryContentsSafe(outputRoot);

        var include = ResolvePackagingInformation(information);

        var artefactName = ResolveArtefactFileName(cfg, moduleName, moduleVersion, preRelease);
        var zipPath = Path.Combine(outputRoot, artefactName);

        var tempRoot = Path.Combine(Path.GetTempPath(), "PowerForge", "artefacts", $"{moduleName}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        var copied = new List<ArtefactCopyEntry>();
        var modules = new List<ArtefactModuleEntry>();

        try
        {
            var mainModuleDest = Path.Combine(tempRoot, moduleName);
            _logger.Info($"Staging packed artefact '{zipPath}'");
            CopyModulePackage(stagingPath, mainModuleDest, include);
            modules.Add(new ArtefactModuleEntry(moduleName, isMainModule: true, version: moduleVersion, path: mainModuleDest));

            if (cfg.RequiredModules.Enabled == true)
            {
                foreach (var rm in (requiredModules ?? Array.Empty<ManifestEditor.RequiredModule>()).Where(m => !string.IsNullOrWhiteSpace(m.ModuleName)))
                {
                    var depEntry = SaveRequiredModuleToFolder(rm, tempRoot);
                    modules.Add(depEntry);
                }
            }

            CopyExtraMappings(
                cfg,
                projectRoot,
                tempRoot,
                moduleName,
                moduleVersion,
                preRelease,
                copied,
                enforceRelativeDestination: true);

            CreateZipFromDirectoryContents(tempRoot, zipPath);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort */ }
        }

        return new ArtefactBuildResult(ArtefactType.Packed, cfg.ID, zipPath, modules.ToArray(), copied.ToArray());
    }

    private ArtefactModuleEntry SaveRequiredModuleToFolder(ManifestEditor.RequiredModule requiredModule, string destinationRoot)
    {
        if (requiredModule is null) throw new ArgumentNullException(nameof(requiredModule));
        if (string.IsNullOrWhiteSpace(requiredModule.ModuleName))
            throw new ArgumentException("Required module name is empty.", nameof(requiredModule));
        if (string.IsNullOrWhiteSpace(destinationRoot))
            throw new ArgumentException("DestinationRoot is required.", nameof(destinationRoot));

        var name = requiredModule.ModuleName.Trim();
        var minimumVersionArgument = NormalizeVersionArgument(requiredModule.ModuleVersion);
        var requiredVersionArgument = NormalizeVersionArgument(requiredModule.RequiredVersion);
        var versionArgument = requiredVersionArgument ?? minimumVersionArgument;

        Directory.CreateDirectory(destinationRoot);
        var tempRoot = Path.Combine(Path.GetTempPath(), "PowerForge", "artefacts", "saved", $"{name}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var runner = new PowerShellRunner();
            IReadOnlyList<PSResourceInfo> saved;

            var psg = new PowerShellGetClient(runner, _logger);
            var psgOpts = new PowerShellGetSaveOptions(
                name: name,
                destinationPath: tempRoot,
                minimumVersion: minimumVersionArgument,
                requiredVersion: requiredVersionArgument,
                repository: null,
                prerelease: false,
                acceptLicense: true,
                credential: null);

            if (_skipPsResourceGetSave)
            {
                saved = psg.Save(psgOpts, timeout: TimeSpan.FromMinutes(10));
            }
            else
            {
                var psrg = new PSResourceGetClient(runner, _logger);
                var psrgOpts = new PSResourceSaveOptions(
                    name: name,
                    destinationPath: tempRoot,
                    version: versionArgument,
                    repository: null,
                    prerelease: false,
                    trustRepository: true,
                    skipDependencyCheck: true,
                    acceptLicense: true);

                try
                {
                    saved = psrg.Save(psrgOpts, timeout: TimeSpan.FromMinutes(10));
                }
                catch (Exception ex)
                {
                    _skipPsResourceGetSave = true;

                    var raw = ex.Message ?? string.Empty;
                    var reason = SimplifyPsResourceGetFailureMessage(raw);

                    if (ex is PowerShellToolNotAvailableException)
                    {
                        _logger.Warn("PSResourceGet is not available; using Save-Module for required modules.");
                    }
                    else if (IsSecretStoreLockedMessage(raw))
                    {
                        _logger.Warn("PSResourceGet cannot access SecretStore (locked); using Save-Module for required modules.");
                    }
                    else if (!string.IsNullOrWhiteSpace(reason))
                    {
                        _logger.Warn($"Save-PSResource failed; using Save-Module for required modules. {reason}");
                    }
                    else
                    {
                        _logger.Warn("Save-PSResource failed; using Save-Module for required modules.");
                    }

                    if (_logger.IsVerbose && !string.IsNullOrWhiteSpace(raw))
                        _logger.Verbose(raw.Trim());

                    saved = psg.Save(psgOpts, timeout: TimeSpan.FromMinutes(10));
                }
            }
            var resolved = saved.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
            var resolvedVersion = resolved?.Version;

            var moduleRoot = Path.Combine(tempRoot, name);
            if (!Directory.Exists(moduleRoot))
                throw new InvalidOperationException($"Save tool did not create expected folder '{moduleRoot}'.");

            string? versionFolder = null;
            if (!string.IsNullOrWhiteSpace(resolvedVersion))
            {
                var candidate = Path.Combine(moduleRoot, resolvedVersion!);
                if (Directory.Exists(candidate)) versionFolder = candidate;
            }

            versionFolder ??= Directory.EnumerateDirectories(moduleRoot).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(versionFolder) || !Directory.Exists(versionFolder))
                throw new InvalidOperationException($"Unable to locate saved version folder under '{moduleRoot}'.");

            var dest = Path.Combine(destinationRoot, name);
            CopyDirectory(versionFolder, dest);
            return new ArtefactModuleEntry(name, isMainModule: false, version: resolvedVersion, path: dest);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    private static bool IsSecretStoreLockedMessage(string message)
        => !string.IsNullOrWhiteSpace(message) &&
           (message.IndexOf("Microsoft.PowerShell.SecretStore", StringComparison.OrdinalIgnoreCase) >= 0 ||
            message.IndexOf("Unlock-SecretStore", StringComparison.OrdinalIgnoreCase) >= 0 ||
            message.IndexOf("A valid password is required", StringComparison.OrdinalIgnoreCase) >= 0);

    private static string SimplifyPsResourceGetFailureMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return string.Empty;

        var trimmed = message.Trim();

        // "Save-PSResource failed (exit X). <reason>" -> "<reason>"
        if (trimmed.StartsWith("Save-PSResource failed", StringComparison.OrdinalIgnoreCase))
        {
            var marker = "). ";
            var idx = trimmed.IndexOf(marker, StringComparison.Ordinal);
            if (idx >= 0 && idx + marker.Length < trimmed.Length)
                return trimmed.Substring(idx + marker.Length).Trim();
        }

        return trimmed;
    }

    private static string? NormalizeVersionArgument(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value!.Trim();
        if (trimmed.Equals("Latest", StringComparison.OrdinalIgnoreCase)) return null;
        if (trimmed.Equals("Auto", StringComparison.OrdinalIgnoreCase)) return null;
        return trimmed;
    }

    private sealed class PackagingInformation
    {
        public string[] ExcludeFromPackage { get; set; } = Array.Empty<string>();
        public string[] IncludeRoot { get; set; } = Array.Empty<string>();
        public string[] IncludePS1 { get; set; } = Array.Empty<string>();
        public string[] IncludeAll { get; set; } = Array.Empty<string>();
    }

    private static PackagingInformation ResolvePackagingInformation(InformationConfiguration? information)
    {
        var info = information ?? new InformationConfiguration();

        var includeRoot = (info.IncludeRoot is { Length: > 0 } ? info.IncludeRoot : DefaultIncludeRoot).ToArray();
        var includePS1 = (info.IncludePS1 is { Length: > 0 } ? info.IncludePS1 : DefaultIncludePS1).ToArray();
        var includeAll = (info.IncludeAll is { Length: > 0 } ? info.IncludeAll : DefaultIncludeAll).ToArray();
        var exclude = (info.ExcludeFromPackage is { Length: > 0 } ? info.ExcludeFromPackage : DefaultExcludeFromPackage).ToArray();

        if (info.IncludeToArray is { Length: > 0 })
        {
            foreach (var entry in info.IncludeToArray.Where(e => e is not null && !string.IsNullOrWhiteSpace(e.Key)))
            {
                if (entry.Values is not { Length: > 0 }) continue;
                if (entry.Key.Equals("IncludeRoot", StringComparison.OrdinalIgnoreCase)) includeRoot = entry.Values;
                if (entry.Key.Equals("IncludePS1", StringComparison.OrdinalIgnoreCase)) includePS1 = entry.Values;
                if (entry.Key.Equals("IncludeAll", StringComparison.OrdinalIgnoreCase)) includeAll = entry.Values;
                if (entry.Key.Equals("ExcludeFromPackage", StringComparison.OrdinalIgnoreCase)) exclude = entry.Values;
            }
        }

        static string[] Normalize(string[] values)
            => (values ?? Array.Empty<string>())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v.Trim())
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

            CopyDirectoryFiltered(dir, Path.Combine(destinationModuleRoot, dirName), include.ExcludeFromPackage ?? Array.Empty<string>(), includeOnlyPs1: false);
        }

        // 3) IncludePS1 directories
        foreach (var dirName in include.IncludePS1)
        {
            if (string.IsNullOrWhiteSpace(dirName)) continue;
            var dir = Path.Combine(src, dirName);
            if (!Directory.Exists(dir)) continue;

            CopyDirectoryFiltered(dir, Path.Combine(destinationModuleRoot, dirName), include.ExcludeFromPackage ?? Array.Empty<string>(), includeOnlyPs1: true);
        }
    }

    private static void CopyDirectoryFiltered(string sourceDir, string destDir, string[] excludeNamePatterns, bool includeOnlyPs1)
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
                if (WildcardAnyMatch(name, excludeNamePatterns)) continue;
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

    private static void CopyDirectory(string sourceDir, string destDir)
    {
        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException($"Directory not found: {sourceDir}");

        if (Directory.Exists(destDir))
            Directory.Delete(destDir, recursive: true);

        Directory.CreateDirectory(destDir);

        foreach (var dir in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = ComputeRelativePath(sourceDir, dir);
            Directory.CreateDirectory(Path.Combine(destDir, rel));
        }
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = ComputeRelativePath(sourceDir, file);
            var outPath = Path.Combine(destDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.Copy(file, outPath, overwrite: true);
        }
    }

    private static void CreateZipFromDirectoryContents(string sourceDir, string zipPath)
    {
        if (File.Exists(zipPath)) File.Delete(zipPath);
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath)!);

        using var fs = File.Create(zipPath);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel = ComputeRelativePath(sourceDir, file).Replace('\\', '/');
            var entry = zip.CreateEntry(rel, CompressionLevel.Optimal);
            using var entryStream = entry.Open();
            using var fileStream = File.OpenRead(file);
            fileStream.CopyTo(entryStream);
        }
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

    private static void ClearDirectoryContentsSafe(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));

        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        if (string.Equals(full.TrimEnd(Path.DirectorySeparatorChar), (root ?? string.Empty).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Refusing to clear root directory contents: {full}");

        if (!Directory.Exists(full)) return;

        foreach (var entry in Directory.EnumerateFileSystemEntries(full))
        {
            try
            {
                if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
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
