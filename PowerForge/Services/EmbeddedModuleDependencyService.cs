using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Copies configured module dependencies into a module's internals payload and restores them later by exact path.
/// </summary>
internal sealed class EmbeddedModuleDependencyService
{
    internal const string ManifestFileName = "module-dependencies.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly ILogger _logger;

    public EmbeddedModuleDependencyService(ILogger logger)
    {
        _logger = logger ?? new NullLogger();
    }

    public EmbeddedModuleDependencyManifest Embed(
        string moduleRoot,
        IReadOnlyList<RequiredModuleReference>? modules,
        IModuleDependencyMetadataProvider metadataProvider,
        DeliveryOptionsConfiguration? delivery = null)
    {
        if (string.IsNullOrWhiteSpace(moduleRoot))
            throw new ArgumentException("Module root is required.", nameof(moduleRoot));
        if (metadataProvider is null)
            throw new ArgumentNullException(nameof(metadataProvider));

        var references = NormalizeReferences(modules);
        var internalsRoot = ResolveInternalsRoot(moduleRoot, delivery);
        var modulesRoot = Path.Combine(internalsRoot, "Modules");
        if (Directory.Exists(modulesRoot))
            Directory.Delete(modulesRoot, recursive: true);

        if (references.Length == 0)
            return new EmbeddedModuleDependencyManifest();

        Directory.CreateDirectory(modulesRoot);

        var installedModules = ResolveInstalledModules(references, metadataProvider);
        var entries = new List<EmbeddedModuleDependencyEntry>();

        foreach (var reference in references)
        {
            if (!installedModules.TryGetValue(reference.ModuleName, out var installed) ||
                installed is null ||
                string.IsNullOrWhiteSpace(installed.ModuleBasePath) ||
                !Directory.Exists(installed.ModuleBasePath))
            {
                throw new InvalidOperationException(
                    $"Embedded module dependency '{reference.ModuleName}' is not available locally. Enable InstallMissingModules or install the module before building.");
            }

            ValidateInstalledVersion(reference, installed);
            var version = ResolveVersion(reference, installed);
            var relativePath = string.IsNullOrWhiteSpace(version)
                ? reference.ModuleName
                : Path.Combine(reference.ModuleName, version!);
            var destination = Path.Combine(modulesRoot, relativePath);

            CopyDirectory(installed.ModuleBasePath!, destination, overwrite: true);
            entries.Add(new EmbeddedModuleDependencyEntry
            {
                Name = reference.ModuleName,
                Version = version,
                RelativePath = NormalizeRelativePath(relativePath)
            });

            _logger.Info($"Embedded module dependency '{reference.ModuleName}' at '{NormalizeRelativePath(Path.Combine("Modules", relativePath))}'.");
        }

        var manifest = new EmbeddedModuleDependencyManifest
        {
            ModulesRoot = "Modules",
            Dependencies = entries.ToArray()
        };

        WriteManifest(Path.Combine(modulesRoot, ManifestFileName), manifest);
        return manifest;
    }

    public EmbeddedModuleDependencyInstallResult[] Install(
        string dependencyManifestPath,
        string destinationRoot,
        string? rootModuleName = null,
        string? rootModuleVersion = null,
        string? rootModuleBasePath = null,
        IReadOnlyCollection<string>? dependencyNames = null,
        OnExistsOption onExists = OnExistsOption.Merge,
        bool force = false,
        bool listOnly = false)
    {
        if (string.IsNullOrWhiteSpace(destinationRoot))
            throw new ArgumentException("Destination root is required.", nameof(destinationRoot));

        var manifestPath = ResolveManifestPath(dependencyManifestPath);
        var manifest = ReadManifest(manifestPath);
        var filters = NormalizeNameFilter(dependencyNames);
        var destination = Path.GetFullPath(destinationRoot);
        var receiptPath = Path.Combine(destination, ManifestFileName);
        var existingReceipt = File.Exists(receiptPath)
            ? ReadManifest(receiptPath)
            : null;
        var results = new List<EmbeddedModuleDependencyInstallResult>();
        EmbeddedModuleDependencyEntry? rootModule = null;

        if (!string.IsNullOrWhiteSpace(rootModuleBasePath))
            ValidateDestinationOutsideSourceRoot(destination, rootModuleBasePath!);

        if (!listOnly)
            Directory.CreateDirectory(destination);

        if (!string.IsNullOrWhiteSpace(rootModuleName) || !string.IsNullOrWhiteSpace(rootModuleBasePath))
        {
            if (string.IsNullOrWhiteSpace(rootModuleName))
                throw new ArgumentException("Root module name is required when installing a private runtime.", nameof(rootModuleName));
            if (string.IsNullOrWhiteSpace(rootModuleBasePath) || !Directory.Exists(rootModuleBasePath))
                throw new DirectoryNotFoundException($"Root module path not found: {rootModuleBasePath}");

            var rootVersion = string.IsNullOrWhiteSpace(rootModuleVersion) ? "Current" : rootModuleVersion!;
            rootModule = new EmbeddedModuleDependencyEntry
            {
                Name = rootModuleName!,
                Version = rootVersion,
                RelativePath = NormalizeRelativePath(Path.Combine(rootModuleName!, rootVersion))
            };

            var destinationPath = ResolveInstallDestinationPath(destination, rootModule.Name, rootVersion, "root module");
            ValidateDestinationOutsideSourceRoot(destinationPath, rootModuleBasePath!);
            var exists = Directory.Exists(destinationPath);
            var action = ResolveAction(exists, onExists, force);

            if (!listOnly)
            {
                if (exists)
                {
                    if (onExists == OnExistsOption.Stop)
                        throw new IOException($"Destination module folder exists: {destinationPath}");
                    if (onExists == OnExistsOption.Skip)
                    {
                        results.Add(CreateInstallResult(rootModule, rootModuleBasePath!, destinationPath, action, receiptPath, "RootModule"));
                    }
                    else if (onExists == OnExistsOption.Merge)
                    {
                        MergeDirectory(rootModuleBasePath!, destinationPath, overwriteFiles: force);
                        results.Add(CreateInstallResult(rootModule, rootModuleBasePath!, destinationPath, action, receiptPath, "RootModule"));
                    }
                    else
                    {
                        CopyDirectory(rootModuleBasePath!, destinationPath, overwrite: true);
                        results.Add(CreateInstallResult(rootModule, rootModuleBasePath!, destinationPath, action, receiptPath, "RootModule"));
                    }
                }
                else
                {
                    CopyDirectory(rootModuleBasePath!, destinationPath, overwrite: true);
                    results.Add(CreateInstallResult(rootModule, rootModuleBasePath!, destinationPath, action, receiptPath, "RootModule"));
                }
            }
            else
            {
                results.Add(CreateInstallResult(rootModule, rootModuleBasePath!, destinationPath, action, receiptPath, "RootModule"));
            }
        }

        foreach (var entry in FilterEntries(manifest, filters))
        {
            var sourcePath = ResolveEntryPath(manifestPath, entry);
            var destinationPath = ResolveInstallDestinationPath(destination, entry.Name, entry.Version, "dependency");
            var exists = Directory.Exists(destinationPath);
            var action = ResolveAction(exists, onExists, force);

            if (!listOnly)
            {
                if (exists)
                {
                    if (onExists == OnExistsOption.Stop)
                        throw new IOException($"Destination dependency folder exists: {destinationPath}");
                    if (onExists == OnExistsOption.Skip)
                    {
                        results.Add(CreateInstallResult(entry, sourcePath, destinationPath, action, receiptPath, "Dependency"));
                        continue;
                    }
                    if (onExists == OnExistsOption.Merge)
                    {
                        MergeDirectory(sourcePath, destinationPath, overwriteFiles: force);
                        results.Add(CreateInstallResult(entry, sourcePath, destinationPath, action, receiptPath, "Dependency"));
                        continue;
                    }
                }

                CopyDirectory(sourcePath, destinationPath, overwrite: true);
            }

            results.Add(CreateInstallResult(entry, sourcePath, destinationPath, action, receiptPath, "Dependency"));
        }

        if (!listOnly)
            WriteManifest(receiptPath, new EmbeddedModuleDependencyManifest
            {
                RootModule = rootModule ?? existingReceipt?.RootModule,
                Dependencies = BuildReceiptDependencies(existingReceipt, results, filters)
            });

        return results.ToArray();
    }

    public static string ResolveManifestPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));

        var full = Path.GetFullPath(path);
        if (File.Exists(full))
            return full;

        if (!Directory.Exists(full))
            throw new DirectoryNotFoundException($"Path not found: {full}");

        var candidates = new[]
        {
            Path.Combine(full, ManifestFileName),
            Path.Combine(full, "Modules", ManifestFileName),
            Path.Combine(full, "Internals", "Modules", ManifestFileName)
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException($"Embedded module dependency manifest '{ManifestFileName}' was not found under '{full}'.");
    }

    public static EmbeddedModuleDependencyManifest ReadManifest(string manifestPath)
    {
        var resolved = ResolveManifestPath(manifestPath);
        var json = File.ReadAllText(resolved);
        var manifest = JsonSerializer.Deserialize<EmbeddedModuleDependencyManifest>(json, JsonOptions);
        if (manifest is null)
            throw new InvalidOperationException($"Unable to read embedded module dependency manifest '{resolved}'.");

        manifest.Dependencies ??= Array.Empty<EmbeddedModuleDependencyEntry>();
        return manifest;
    }

    public static string FindManifestForModuleBase(string moduleBase)
        => ResolveManifestPath(Path.Combine(moduleBase, "Internals", "Modules"));

    public static string ResolveInternalsModulesRoot(string moduleRoot, string internalsPath)
    {
        if (string.IsNullOrWhiteSpace(moduleRoot))
            throw new ArgumentException("Module root is required.", nameof(moduleRoot));
        if (string.IsNullOrWhiteSpace(internalsPath))
            throw new ArgumentException("Internals path is required.", nameof(internalsPath));

        var relative = internalsPath.Trim();
        if (Path.IsPathRooted(relative))
            throw new InvalidOperationException("Delivery.InternalsPath must be relative when resolving embedded module dependencies.");

        var root = Path.GetFullPath(moduleRoot);
        var internalsRoot = Path.GetFullPath(Path.Combine(root, relative));
        if (!IsPathUnderRoot(root, internalsRoot))
            throw new InvalidOperationException("Delivery.InternalsPath must stay inside the module root.");

        return Path.Combine(internalsRoot, "Modules");
    }

    public static EmbeddedModuleDependencyEntry[] FilterEntries(
        EmbeddedModuleDependencyManifest manifest,
        IReadOnlyCollection<string>? dependencyNames)
    {
        var filters = NormalizeNameFilter(dependencyNames);
        var entries = ValidateDependencyEntries(manifest)
            .Where(static entry => !string.IsNullOrWhiteSpace(entry.Name) && !string.IsNullOrWhiteSpace(entry.RelativePath))
            .ToArray();
        if (filters.Count == 0)
            return entries;

        var matched = new HashSet<string>(
            entries
                .Where(entry => filters.Contains(entry.Name))
                .Select(static entry => entry.Name),
            StringComparer.OrdinalIgnoreCase);
        var missing = filters
            .Where(filter => !matched.Contains(filter))
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missing.Length > 0)
            throw new InvalidOperationException($"Embedded module dependency filter did not match manifest entries: {string.Join(", ", missing)}.");

        return entries
            .Where(entry => filters.Contains(entry.Name))
            .ToArray();
    }

    public static string ResolveEntryPath(string manifestPath, EmbeddedModuleDependencyEntry entry)
    {
        if (entry is null) throw new ArgumentNullException(nameof(entry));
        if (string.IsNullOrWhiteSpace(entry.RelativePath))
            throw new InvalidOperationException($"Embedded module dependency '{entry.Name}' does not have a relative path.");

        var root = Path.GetFullPath(Path.GetDirectoryName(ResolveManifestPath(manifestPath))!);
        var relativePath = FromPortableRelativePath(entry.RelativePath);
        if (Path.IsPathRooted(relativePath))
            throw new InvalidOperationException($"Embedded module dependency '{entry.Name}' path must be relative.");

        var candidate = Path.GetFullPath(Path.Combine(root, relativePath));
        if (!IsPathUnderRoot(root, candidate))
            throw new InvalidOperationException($"Embedded module dependency '{entry.Name}' path escapes the dependency manifest root.");

        return candidate;
    }

    public static string ResolveInstallDestinationPath(string destinationRoot, string name, string? version, string kind)
    {
        if (string.IsNullOrWhiteSpace(destinationRoot))
            throw new ArgumentException("Destination root is required.", nameof(destinationRoot));
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException($"Embedded module {kind} name is required.");

        var root = Path.GetFullPath(destinationRoot);
        var versionFolder = string.IsNullOrWhiteSpace(version) ? "Current" : version!.Trim();
        var candidate = Path.GetFullPath(Path.Combine(root, name.Trim(), versionFolder));
        if (!IsPathUnderRoot(root, candidate))
            throw new InvalidOperationException($"Embedded module {kind} destination escapes the requested destination root.");

        return candidate;
    }

    public static EmbeddedModuleDependencyEntry ResolveRootModuleEntry(EmbeddedModuleDependencyManifest manifest, string moduleName)
    {
        if (manifest is null) throw new ArgumentNullException(nameof(manifest));
        if (string.IsNullOrWhiteSpace(moduleName))
            throw new ArgumentException("Module name is required.", nameof(moduleName));

        if (manifest.RootModule is null ||
            string.IsNullOrWhiteSpace(manifest.RootModule.Name) ||
            string.IsNullOrWhiteSpace(manifest.RootModule.RelativePath))
        {
            throw new InvalidOperationException(
                $"Dependency receipt does not contain a root module entry for '{moduleName}'. Reinstall with Install-ModuleDependency -Name {moduleName} -Path <path>.");
        }

        if (!string.Equals(manifest.RootModule.Name, moduleName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Dependency receipt root module is '{manifest.RootModule.Name}', not '{moduleName}'.");

        return manifest.RootModule;
    }

    public static EmbeddedModuleDependencyEntry ResolveRootModuleEntry(EmbeddedModuleDependencyManifest manifest)
    {
        if (manifest is null) throw new ArgumentNullException(nameof(manifest));

        if (manifest.RootModule is null ||
            string.IsNullOrWhiteSpace(manifest.RootModule.Name) ||
            string.IsNullOrWhiteSpace(manifest.RootModule.RelativePath))
        {
            throw new InvalidOperationException("Dependency receipt does not contain a root module entry.");
        }

        return manifest.RootModule;
    }

    public static string ResolveModuleImportPath(string modulePath, string moduleName)
    {
        if (string.IsNullOrWhiteSpace(modulePath) || !Directory.Exists(modulePath))
            throw new DirectoryNotFoundException($"Embedded dependency path not found: {modulePath}");

        var exactManifest = Path.Combine(modulePath, $"{moduleName}.psd1");
        if (File.Exists(exactManifest))
            return exactManifest;

        var anyManifest = Directory.GetFiles(modulePath, "*.psd1", SearchOption.TopDirectoryOnly)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(anyManifest))
            return anyManifest!;

        return modulePath;
    }

    private static EmbeddedModuleDependencyInstallResult CreateInstallResult(
        EmbeddedModuleDependencyEntry entry,
        string sourcePath,
        string destinationPath,
        string action,
        string receiptPath,
        string kind)
        => new()
        {
            Name = entry.Name,
            Version = entry.Version,
            SourcePath = sourcePath,
            DestinationPath = destinationPath,
            Action = action,
            Kind = kind,
            ReceiptPath = Path.Combine(Path.GetDirectoryName(receiptPath)!, ManifestFileName)
        };

    private static EmbeddedModuleDependencyEntry[] BuildReceiptDependencies(
        EmbeddedModuleDependencyManifest? existingReceipt,
        IReadOnlyList<EmbeddedModuleDependencyInstallResult> results,
        IReadOnlyCollection<string> filters)
    {
        var updated = (results ?? Array.Empty<EmbeddedModuleDependencyInstallResult>())
            .Where(static result => !string.Equals(result.Kind, "RootModule", StringComparison.OrdinalIgnoreCase))
            .Where(static result => !string.IsNullOrWhiteSpace(result.Name))
            .Select(static result => new EmbeddedModuleDependencyEntry
            {
                Name = result.Name,
                Version = result.Version,
                RelativePath = NormalizeRelativePath(Path.Combine(result.Name, string.IsNullOrWhiteSpace(result.Version) ? "Current" : result.Version!))
            })
            .ToArray();

        if (filters.Count == 0 || existingReceipt?.Dependencies is not { Length: > 0 })
            return updated;

        var entries = existingReceipt.Dependencies
            .Where(static entry => entry is not null && !string.IsNullOrWhiteSpace(entry.Name) && !string.IsNullOrWhiteSpace(entry.RelativePath))
            .Select(static entry => new EmbeddedModuleDependencyEntry
            {
                Name = entry.Name,
                Version = entry.Version,
                RelativePath = entry.RelativePath
            })
            .ToList();
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < entries.Count; i++)
            index[entries[i].Name] = i;

        foreach (var entry in updated)
        {
            if (index.TryGetValue(entry.Name, out var existingIndex))
            {
                entries[existingIndex] = entry;
            }
            else
            {
                index[entry.Name] = entries.Count;
                entries.Add(entry);
            }
        }

        return entries.ToArray();
    }

    private static RequiredModuleReference[] NormalizeReferences(IReadOnlyList<RequiredModuleReference>? modules)
    {
        var normalized = new List<RequiredModuleReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var i = (modules?.Count ?? 0) - 1; i >= 0; i--)
        {
            var module = modules![i];
            if (module is null || string.IsNullOrWhiteSpace(module.ModuleName))
                continue;

            if (!seen.Add(module.ModuleName.Trim()))
                continue;

            normalized.Add(module);
        }

        normalized.Reverse();
        return normalized.ToArray();
    }

    private static HashSet<string> NormalizeNameFilter(IReadOnlyCollection<string>? names)
        => new((names ?? Array.Empty<string>())
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Select(static name => name.Trim()), StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, InstalledModuleMetadata> ResolveInstalledModules(
        IReadOnlyList<RequiredModuleReference> references,
        IModuleDependencyMetadataProvider metadataProvider)
        => metadataProvider is IModuleDependencyVersionedMetadataProvider versionedProvider
            ? versionedProvider.GetInstalledModules(references)
            : metadataProvider.GetLatestInstalledModules(references.Select(static module => module.ModuleName).ToArray());

    private static string ResolveVersion(RequiredModuleReference reference, InstalledModuleMetadata installed)
        => installed.Version ?? reference.RequiredVersion ?? reference.ModuleVersion ?? "Current";

    private static void ValidateInstalledVersion(RequiredModuleReference reference, InstalledModuleMetadata installed)
    {
        if (!string.IsNullOrWhiteSpace(reference.Guid) &&
            !reference.Guid!.Trim().Equals("Auto", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(installed.Guid?.Trim(), reference.Guid.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Embedded module dependency '{reference.ModuleName}' resolved GUID '{installed.Guid ?? "<none>"}', but GUID '{reference.Guid}' was requested.");
        }

        if (string.IsNullOrWhiteSpace(installed.Version))
            return;

        if (!string.IsNullOrWhiteSpace(reference.RequiredVersion) &&
            !string.Equals(installed.Version, reference.RequiredVersion, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Embedded module dependency '{reference.ModuleName}' resolved version {installed.Version}, but RequiredVersion {reference.RequiredVersion} was requested.");
        }

        if (!string.IsNullOrWhiteSpace(reference.ModuleVersion) &&
            TryParseVersion(installed.Version, out var installedVersion) &&
            TryParseVersion(reference.ModuleVersion, out var minimumVersion) &&
            installedVersion < minimumVersion)
        {
            throw new InvalidOperationException(
                $"Embedded module dependency '{reference.ModuleName}' resolved version {installed.Version}, but minimum version {reference.ModuleVersion} was requested.");
        }

        if (!string.IsNullOrWhiteSpace(reference.MaximumVersion) &&
            TryParseVersion(installed.Version, out installedVersion) &&
            TryParseVersion(reference.MaximumVersion, out var maximumVersion) &&
            installedVersion > maximumVersion)
        {
            throw new InvalidOperationException(
                $"Embedded module dependency '{reference.ModuleName}' resolved version {installed.Version}, but maximum version {reference.MaximumVersion} was requested.");
        }
    }

    private static bool TryParseVersion(string? value, out Version version)
        => Version.TryParse(value, out version!);

    private static string ResolveInternalsRoot(string moduleRoot, DeliveryOptionsConfiguration? delivery)
    {
        var relative = string.IsNullOrWhiteSpace(delivery?.InternalsPath)
            ? "Internals"
            : delivery!.InternalsPath.Trim();

        return Path.GetDirectoryName(ResolveInternalsModulesRoot(moduleRoot, relative))!;
    }

    private static EmbeddedModuleDependencyEntry[] ValidateDependencyEntries(EmbeddedModuleDependencyManifest manifest)
    {
        var valid = new List<EmbeddedModuleDependencyEntry>();
        foreach (var entry in manifest.Dependencies ?? Array.Empty<EmbeddedModuleDependencyEntry>())
        {
            if (entry is null)
                continue;

            var hasName = !string.IsNullOrWhiteSpace(entry.Name);
            var hasRelativePath = !string.IsNullOrWhiteSpace(entry.RelativePath);
            if (hasName != hasRelativePath)
            {
                var name = hasName ? entry.Name : "<unnamed>";
                throw new InvalidOperationException($"Embedded module dependency '{name}' has an incomplete manifest entry.");
            }

            if (hasName)
                valid.Add(entry);
        }

        return valid.ToArray();
    }

    private static bool IsPathUnderRoot(string root, string candidate)
    {
        var comparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidate);
        return normalizedCandidate.StartsWith(normalizedRoot, comparison);
    }

    private static void ValidateDestinationOutsideSourceRoot(string destinationRoot, string sourceRoot)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot))
            return;

        var source = Path.GetFullPath(sourceRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var destination = Path.GetFullPath(destinationRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var comparison = Path.DirectorySeparatorChar == '\\'
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (string.Equals(source, destination, comparison) ||
            destination.StartsWith(source + Path.DirectorySeparatorChar, comparison))
        {
            throw new InvalidOperationException("Private runtime destination must not be inside the source module path.");
        }
    }

    private static void WriteManifest(string manifestPath, EmbeddedModuleDependencyManifest manifest)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, JsonOptions));
    }

    private static string ResolveAction(bool exists, OnExistsOption onExists, bool force)
    {
        if (!exists) return "Copy";
        return onExists switch
        {
            OnExistsOption.Overwrite => "Overwrite",
            OnExistsOption.Merge => force ? "MergeOverwrite" : "Merge",
            OnExistsOption.Skip => "Skip",
            OnExistsOption.Stop => "Stop",
            _ => "Keep"
        };
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory, bool overwrite)
    {
        if (!Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException($"Directory not found: {sourceDirectory}");

        if (Directory.Exists(destinationDirectory))
        {
            if (!overwrite)
                throw new IOException($"Destination directory exists: {destinationDirectory}");
            Directory.Delete(destinationDirectory, recursive: true);
        }

        Directory.CreateDirectory(destinationDirectory);
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = ComputeRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = ComputeRelativePath(sourceDirectory, file);
            var target = Path.Combine(destinationDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void MergeDirectory(string sourceDirectory, string destinationDirectory, bool overwriteFiles)
    {
        if (!Directory.Exists(sourceDirectory))
            throw new DirectoryNotFoundException($"Directory not found: {sourceDirectory}");

        Directory.CreateDirectory(destinationDirectory);
        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = ComputeRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = ComputeRelativePath(sourceDirectory, file);
            var target = Path.Combine(destinationDirectory, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (!overwriteFiles && File.Exists(target))
                continue;

            File.Copy(file, target, overwrite: overwriteFiles);
        }
    }

    private static string ComputeRelativePath(string baseDirectory, string fullPath)
    {
        try
        {
            var baseUri = new Uri(AppendDirectorySeparator(Path.GetFullPath(baseDirectory)));
            var pathUri = new Uri(Path.GetFullPath(fullPath));
            return Uri.UnescapeDataString(baseUri.MakeRelativeUri(pathUri).ToString())
                .Replace('/', Path.DirectorySeparatorChar);
        }
        catch
        {
            return Path.GetFileName(fullPath) ?? fullPath;
        }
    }

    private static string AppendDirectorySeparator(string path)
        => path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;

    private static string NormalizeRelativePath(string relativePath)
        => relativePath.Replace('\\', '/');

    private static string FromPortableRelativePath(string relativePath)
        => relativePath.Replace('/', Path.DirectorySeparatorChar);
}

/// <summary>
/// Manifest stored next to embedded module dependency payloads.
/// </summary>
public sealed class EmbeddedModuleDependencyManifest
{
    /// <summary>Manifest schema version.</summary>
    public string Schema { get; set; } = "1.0";

    /// <summary>Relative folder that contains dependency payloads.</summary>
    public string ModulesRoot { get; set; } = "Modules";

    /// <summary>Root module entry for a private runtime receipt.</summary>
    public EmbeddedModuleDependencyEntry? RootModule { get; set; }

    /// <summary>Embedded dependency entries.</summary>
    public EmbeddedModuleDependencyEntry[] Dependencies { get; set; } = Array.Empty<EmbeddedModuleDependencyEntry>();
}

/// <summary>
/// Single embedded dependency payload entry.
/// </summary>
public sealed class EmbeddedModuleDependencyEntry
{
    /// <summary>Dependency module name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Dependency module version, when known.</summary>
    public string? Version { get; set; }

    /// <summary>Relative path from the dependency manifest folder to the module payload.</summary>
    public string RelativePath { get; set; } = string.Empty;
}

/// <summary>
/// Result returned when installing embedded module dependencies to an explicit path.
/// </summary>
public sealed class EmbeddedModuleDependencyInstallResult
{
    /// <summary>Dependency module name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Dependency module version, when known.</summary>
    public string? Version { get; set; }

    /// <summary>Source payload path.</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>Destination payload path.</summary>
    public string DestinationPath { get; set; } = string.Empty;

    /// <summary>Planned or performed action.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Entry kind, such as RootModule or Dependency.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>Dependency receipt path.</summary>
    public string ReceiptPath { get; set; } = string.Empty;
}
