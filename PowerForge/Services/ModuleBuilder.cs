using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace PowerForge;

/// <summary>
/// Orchestrates building a PowerShell module purely from C# services.
/// </summary>
public sealed class ModuleBuilder
{
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new module builder that logs progress via <paramref name="logger"/>.
    /// </summary>
    public ModuleBuilder(ILogger logger) => _logger = logger;

    /// <summary>
    /// Options controlling module build behavior.
    /// </summary>
    public sealed class Options
    {
        /// <summary>Path to the staged module root folder (contains PSD1/PSM1).</summary>
        public string ProjectRoot { get; set; } = string.Empty;
        /// <summary>Name of the module being built (used for PSD1 naming and exports).</summary>
        public string ModuleName { get; set; } = string.Empty;
        /// <summary>
        /// Optional path to a .NET project (.csproj) that should be published into the module Lib folder.
        /// When empty, binary publishing is skipped (script-only build).
        /// </summary>
        public string CsprojPath { get; set; } = string.Empty;
        /// <summary>Build configuration used for publishing (e.g., Release).</summary>
        public string Configuration { get; set; } = "Release";
        /// <summary>Target frameworks to publish (e.g., net472, net8.0, net10.0).</summary>
        public IReadOnlyList<string> Frameworks { get; set; } = Array.Empty<string>();
        /// <summary>Base module version to write to the manifest before install resolution.</summary>
        public string ModuleVersion { get; set; } = "1.0.0";
        /// <summary>Installation strategy controlling versioned install behavior.</summary>
        public InstallationStrategy Strategy { get; set; } = InstallationStrategy.AutoRevision;
        /// <summary>Number of installed versions to keep after install.</summary>
        public int KeepVersions { get; set; } = 3;
        /// <summary>Destination module roots to install to. When empty, defaults are used.</summary>
        public IReadOnlyList<string> InstallRoots { get; set; } = Array.Empty<string>();
        /// <summary>Author value written to the manifest.</summary>
        public string? Author { get; set; }
        /// <summary>CompanyName value written to the manifest.</summary>
        public string? CompanyName { get; set; }
        /// <summary>Description value written to the manifest.</summary>
        public string? Description { get; set; }
        /// <summary>CompatiblePSEditions written to the manifest.</summary>
        public IReadOnlyList<string> CompatiblePSEditions { get; set; } = new[] { "Desktop", "Core" };
        /// <summary>Tags written to the manifest PrivateData.PSData.</summary>
        public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();
        /// <summary>IconUri written to the manifest PrivateData.PSData.</summary>
        public string? IconUri { get; set; }
        /// <summary>ProjectUri written to the manifest PrivateData.PSData.</summary>
        public string? ProjectUri { get; set; }
        /// <summary>
        /// Optional assembly file names (for example: <c>My.Module.dll</c>) to scan for cmdlets/aliases when updating manifest exports.
        /// When empty, defaults to <c>&lt;ModuleName&gt;.dll</c>.
        /// </summary>
        public IReadOnlyList<string> ExportAssemblies { get; set; } = Array.Empty<string>();
        /// <summary>
        /// When true, skips binary cmdlet/alias scanning and keeps existing manifest <c>CmdletsToExport</c>/<c>AliasesToExport</c> values.
        /// </summary>
        public bool DisableBinaryCmdletScan { get; set; }
    }

    /// <summary>
    /// Builds the module layout in-place under <see cref="Options.ProjectRoot"/> without installing it.
    /// </summary>
    /// <param name="opts">Build options.</param>
    public void BuildInPlace(Options opts)
    {
        if (string.IsNullOrWhiteSpace(opts.ProjectRoot) || !Directory.Exists(opts.ProjectRoot))
            throw new DirectoryNotFoundException($"Project root not found: {opts.ProjectRoot}");
        if (string.IsNullOrWhiteSpace(opts.ModuleName))
            throw new ArgumentException("ModuleName is required", nameof(opts.ModuleName));

        var hasCsproj = !string.IsNullOrWhiteSpace(opts.CsprojPath);
        if (hasCsproj && !File.Exists(opts.CsprojPath))
            throw new FileNotFoundException($"Project file not found: {opts.CsprojPath}");

        if (hasCsproj)
        {
            var frameworks = opts.Frameworks.Count > 0 ? opts.Frameworks : new[] { "net472", "net8.0" };

            var libRoot = Path.Combine(opts.ProjectRoot, "Lib");
            var coreDir = Path.Combine(libRoot, "Core");
            var defDir  = Path.Combine(libRoot, "Default");
            if (Directory.Exists(libRoot)) Directory.Delete(libRoot, recursive: true);
            Directory.CreateDirectory(coreDir);
            Directory.CreateDirectory(defDir);

            // 1) Build libraries (dotnet publish) per framework and copy to Lib/<Core|Default>
            var publisher = new DotnetPublisher(_logger);
            var exportAssemblyFileNames = ResolveExportAssemblyFileNames(opts.ModuleName, opts.ExportAssemblies);

            // Publish into an isolated temp folder to avoid file locking issues when the build host has already loaded
            // assemblies from the repo's bin/obj outputs.
            var artifactsRoot = Path.Combine(Path.GetTempPath(), "PowerForge", "dotnet", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(artifactsRoot);
            try
            {
                var publishes = publisher.Publish(opts.CsprojPath, opts.Configuration, frameworks, opts.ModuleVersion, artifactsRoot);

                var usedTargets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var tfm in frameworks)
                {
                    if (!publishes.TryGetValue(tfm, out var src)) continue;

                    var target = IsCore(tfm) ? coreDir : defDir;

                    // We currently support one "Core" and one "Default" payload per build output.
                    // If multiple TFMs map to the same target folder (e.g., net10.0 + net8.0 -> Core),
                    // clear the previous payload to avoid mixing dependencies across TFMs.
                    if (!usedTargets.Add(target))
                    {
                        _logger.Warn($"Multiple frameworks map to '{Path.GetFileName(target)}'. Clearing '{target}' before copying '{tfm}' to avoid mixed outputs.");
                        try
                        {
                            if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
                        }
                        catch { /* best effort */ }
                        Directory.CreateDirectory(target);
                    }

                    CopyPublishOutputBinaries(src, target, tfm, exportAssemblyFileNames);
                }
            }
            finally
            {
                if (!_logger.IsVerbose)
                {
                    try
                    {
                        if (Directory.Exists(artifactsRoot))
                            Directory.Delete(artifactsRoot, recursive: true);
                    }
                    catch { /* best effort */ }
                }
            }
        }
        else
        {
            _logger.Verbose($"No CsprojPath specified for {opts.ModuleName}; skipping binary publish step.");
        }

        // 2) Manifest generation
        var psd1 = Path.Combine(opts.ProjectRoot, $"{opts.ModuleName}.psd1");
        // Prefer a script RootModule for compatibility; load binary via NestedModules
        var rootModule = $"{opts.ModuleName}.psm1";
        if (File.Exists(psd1))
        {
            // Preserve existing manifest metadata (GUID, RequiredModules, etc.) and patch only the key fields.
            ManifestEditor.TrySetTopLevelModuleVersion(psd1, opts.ModuleVersion);
            ManifestEditor.TrySetTopLevelString(psd1, "RootModule", rootModule);
            if (!string.IsNullOrWhiteSpace(opts.Author)) ManifestEditor.TrySetTopLevelString(psd1, "Author", opts.Author!);
            if (!string.IsNullOrWhiteSpace(opts.CompanyName)) ManifestEditor.TrySetTopLevelString(psd1, "CompanyName", opts.CompanyName!);
            if (!string.IsNullOrWhiteSpace(opts.Description)) ManifestEditor.TrySetTopLevelString(psd1, "Description", opts.Description!);
            if (opts.CompatiblePSEditions.Count > 0)
                ManifestEditor.TrySetTopLevelStringArray(psd1, "CompatiblePSEditions", opts.CompatiblePSEditions.ToArray());
        }
        else
        {
            ManifestWriter.Generate(
                path: psd1,
                moduleName: opts.ModuleName,
                moduleVersion: opts.ModuleVersion,
                author: opts.Author,
                companyName: opts.CompanyName,
                description: opts.Description,
                compatiblePSEditions: opts.CompatiblePSEditions.ToArray(),
                rootModule: rootModule,
                scriptsToProcess: Array.Empty<string>());
        }

        if (opts.Tags.Count > 0) BuildServices.SetPsDataStringArray(psd1, "Tags", opts.Tags.ToArray());
        if (!string.IsNullOrWhiteSpace(opts.IconUri)) BuildServices.SetPsDataString(psd1, "IconUri", opts.IconUri!);
        if (!string.IsNullOrWhiteSpace(opts.ProjectUri)) BuildServices.SetPsDataString(psd1, "ProjectUri", opts.ProjectUri!);

        // 3) Exports
        IEnumerable<string>? functionsToSet = null;
        var publicFolder = Path.Combine(opts.ProjectRoot, "Public");
        if (Directory.Exists(publicFolder))
        {
            string[] scripts;
            try { scripts = Directory.EnumerateFiles(publicFolder, "*.ps1", SearchOption.AllDirectories).ToArray(); }
            catch { scripts = Array.Empty<string>(); }
            functionsToSet = ExportDetector.DetectScriptFunctions(scripts);
        }

        IEnumerable<string>? cmdletsToSet = null;
        IEnumerable<string>? aliasesToSet = null;
        if (!opts.DisableBinaryCmdletScan)
        {
            var exportDlls = ResolveExportAssemblies(opts.ProjectRoot, opts.ModuleName, opts.ExportAssemblies);
            if (exportDlls.Length == 0)
            {
                _logger.Warn($"No export assemblies found for '{opts.ModuleName}' under staging; keeping existing CmdletsToExport/AliasesToExport.");
            }
            else
            {
                cmdletsToSet = ExportDetector.DetectBinaryCmdlets(exportDlls);
                aliasesToSet = ExportDetector.DetectBinaryAliases(exportDlls);
            }
        }

        BuildServices.SetManifestExports(psd1, functions: functionsToSet, cmdlets: cmdletsToSet, aliases: aliasesToSet);
    }

    /// <summary>
    /// Builds the module into <see cref="Options.ProjectRoot"/> and installs it using versioned install.
    /// </summary>
    /// <param name="opts">Build options.</param>
    /// <returns>Installation result including resolved version and installed paths.</returns>
    public ModuleInstallerResult Build(Options opts)
    {
        BuildInPlace(opts);
        return BuildServices.InstallVersioned(
            stagingPath: opts.ProjectRoot,
            moduleName: opts.ModuleName,
            moduleVersion: opts.ModuleVersion,
            strategy: opts.Strategy,
            keepVersions: opts.KeepVersions,
            roots: opts.InstallRoots,
            updateManifestToResolvedVersion: true);
    }

    private static bool IsCore(string tfm)
    {
        if (string.IsNullOrWhiteSpace(tfm)) return false;

        var value = tfm.Trim();
        if (value.StartsWith("netcoreapp", StringComparison.OrdinalIgnoreCase)) return true;
        if (value.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase)) return true;

        // net472/net48/etc are .NET Framework TFMs and should be treated as "Default".
        // Modern TFMs (net5.0+ and future) include a dot and should go to "Core".
        if (!value.StartsWith("net", StringComparison.OrdinalIgnoreCase)) return false;
        if (!value.Contains('.')) return false;

        var suffix = value.Substring(3);
        int digits = 0;
        while (digits < suffix.Length && char.IsDigit(suffix[digits])) digits++;
        if (digits == 0) return false;

        if (!int.TryParse(suffix.Substring(0, digits), out var major)) return false;
        return major >= 5;
    }

    private sealed class PublishCopyPlan
    {
        public HashSet<string> RootFileNames { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> RuntimeTargetRelativePaths { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly HashSet<string> AlwaysExcludedRootFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "System.Management.Automation.dll",
        "System.Management.dll",
    };

    private void CopyPublishOutputBinaries(string publishDir, string targetDir, string tfm, ISet<string> exportAssemblyFileNames)
    {
        var plan = CreateCopyPlan(publishDir, tfm);
        var copied = 0;

        foreach (var fileName in plan.RootFileNames)
        {
            var source = Path.Combine(publishDir, fileName);
            if (!File.Exists(source)) continue;

            var dest = Path.Combine(targetDir, fileName);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(source, dest, overwrite: true);
            copied++;
        }

        foreach (var rel in plan.RuntimeTargetRelativePaths)
        {
            var source = Path.Combine(publishDir, rel);
            if (!File.Exists(source)) continue;

            var dest = Path.Combine(targetDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(source, dest, overwrite: true);
            copied++;
        }

        foreach (var fileName in exportAssemblyFileNames)
        {
            if (string.IsNullOrWhiteSpace(fileName)) continue;

            var xmlFileName = Path.ChangeExtension(fileName, ".xml");
            var xmlSource = Path.Combine(publishDir, xmlFileName);
            if (!File.Exists(xmlSource)) continue;

            var xmlDest = Path.Combine(targetDir, xmlFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(xmlDest)!);
            File.Copy(xmlSource, xmlDest, overwrite: true);
            copied++;
        }

        _logger.Verbose($"Copied {copied} binaries for {tfm} from '{publishDir}' to '{targetDir}'.");
    }

    private static ISet<string> ResolveExportAssemblyFileNames(string moduleName, IReadOnlyList<string>? exportAssemblies)
    {
        var fileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var specified = (exportAssemblies ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().Trim('"'))
            .ToArray();

        var entries = specified.Length > 0 ? specified : new[] { moduleName + ".dll" };
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            var name = entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? entry : entry + ".dll";
            name = Path.GetFileName(name);
            if (string.IsNullOrWhiteSpace(name)) continue;
            fileNames.Add(name);
        }

        return fileNames;
    }

    private PublishCopyPlan CreateCopyPlan(string publishDir, string tfm)
    {
        var plan = TryCreateCopyPlanFromDeps(publishDir, tfm);
        if (plan is not null) return plan;

        // Fallback: copy only top-level binaries, excluding PowerShell host assemblies.
        var fallback = new PublishCopyPlan();
        foreach (var file in Directory.EnumerateFiles(publishDir, "*", SearchOption.TopDirectoryOnly))
        {
            var fileName = Path.GetFileName(file);
            if (!IsBinaryFileName(fileName)) continue;
            if (AlwaysExcludedRootFiles.Contains(fileName)) continue;
            fallback.RootFileNames.Add(fileName);
        }

        return fallback;
    }

    private PublishCopyPlan? TryCreateCopyPlanFromDeps(string publishDir, string tfm)
    {
        string? depsPath = null;
        try
        {
            depsPath = Directory.EnumerateFiles(publishDir, "*.deps.json", SearchOption.TopDirectoryOnly).FirstOrDefault();
        }
        catch { /* ignore */ }

        if (string.IsNullOrWhiteSpace(depsPath) || !File.Exists(depsPath)) return null;

        try
        {
            using var stream = File.OpenRead(depsPath);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            var plan = new PublishCopyPlan();
            if (!root.TryGetProperty("targets", out var targets) || targets.ValueKind != JsonValueKind.Object)
                return plan;

            var excluded = ComputeExcludedLibraries(targets);

            foreach (var target in targets.EnumerateObject())
            {
                if (target.Value.ValueKind != JsonValueKind.Object) continue;

                foreach (var lib in target.Value.EnumerateObject())
                {
                    if (excluded.Contains(lib.Name)) continue;

                    AddRuntimeAssetFileNames(plan.RootFileNames, lib.Value, "runtime");
                    AddRuntimeAssetFileNames(plan.RootFileNames, lib.Value, "native");
                    AddRuntimeTargetPaths(plan.RuntimeTargetRelativePaths, lib.Value);
                }
            }

            foreach (var excludedRoot in AlwaysExcludedRootFiles)
                plan.RootFileNames.Remove(excludedRoot);

            if (_logger.IsVerbose)
                _logger.Verbose($"Copy plan for {tfm}: {plan.RootFileNames.Count} root binaries, {plan.RuntimeTargetRelativePaths.Count} runtime target binaries (deps: {Path.GetFileName(depsPath)}).");

            return plan;
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to parse deps.json for {tfm} ({depsPath}): {ex.Message}. Falling back to copying top-level binaries.");
            return null;
        }
    }

    private static bool IsPowerShellRuntimeLibraryId(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        return id.Equals("System.Management.Automation", StringComparison.OrdinalIgnoreCase) ||
               id.Equals("Microsoft.PowerShell.5.ReferenceAssemblies", StringComparison.OrdinalIgnoreCase) ||
               id.StartsWith("Microsoft.PowerShell.", StringComparison.OrdinalIgnoreCase) ||
               id.StartsWith("Microsoft.Management.Infrastructure", StringComparison.OrdinalIgnoreCase) ||
               id.StartsWith("Microsoft.WSMan", StringComparison.OrdinalIgnoreCase);
    }

    private static HashSet<string> ComputeExcludedLibraries(JsonElement targets)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var dependencyMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var target in targets.EnumerateObject())
        {
            if (target.Value.ValueKind != JsonValueKind.Object) continue;

            foreach (var lib in target.Value.EnumerateObject())
            {
                var key = lib.Name;
                var slash = key.IndexOf('/');
                var id = slash > 0 ? key.Substring(0, slash) : key;
                if (IsPowerShellRuntimeLibraryId(id))
                    roots.Add(key);

                if (dependencyMap.ContainsKey(key)) continue;
                if (!lib.Value.TryGetProperty("dependencies", out var depsObj) || depsObj.ValueKind != JsonValueKind.Object)
                    continue;

                var deps = new List<string>();
                foreach (var dep in depsObj.EnumerateObject())
                {
                    if (dep.Value.ValueKind != JsonValueKind.String) continue;

                    var version = dep.Value.GetString();
                    if (string.IsNullOrWhiteSpace(version)) continue;

                    deps.Add(dep.Name + "/" + version);
                }

                if (deps.Count > 0)
                    dependencyMap[key] = deps.ToArray();
            }
        }

        var excluded = new HashSet<string>(roots, StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(roots);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!dependencyMap.TryGetValue(current, out var deps)) continue;

            foreach (var depKey in deps)
            {
                if (excluded.Add(depKey)) queue.Enqueue(depKey);
            }
        }

        return excluded;
    }

    private static void AddRuntimeAssetFileNames(HashSet<string> rootFileNames, JsonElement libEntry, string propertyName)
    {
        if (!libEntry.TryGetProperty(propertyName, out var assets) || assets.ValueKind != JsonValueKind.Object)
            return;

        foreach (var asset in assets.EnumerateObject())
        {
            var fileName = Path.GetFileName(asset.Name);
            if (!IsBinaryFileName(fileName)) continue;
            rootFileNames.Add(fileName);
        }
    }

    private static void AddRuntimeTargetPaths(HashSet<string> relativePaths, JsonElement libEntry)
    {
        if (!libEntry.TryGetProperty("runtimeTargets", out var assets) || assets.ValueKind != JsonValueKind.Object)
            return;

        foreach (var asset in assets.EnumerateObject())
        {
            var relative = asset.Name.Replace('/', Path.DirectorySeparatorChar);
            var fileName = Path.GetFileName(relative);
            if (!IsBinaryFileName(fileName)) continue;
            relativePaths.Add(relative);
        }
    }

    private static bool IsBinaryFileName(string fileNameOrPath)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrPath)) return false;

        var ext = Path.GetExtension(fileNameOrPath);
        return ext.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".so", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".dylib", StringComparison.OrdinalIgnoreCase);
    }

    private static string[] ResolveExportAssemblies(string projectRoot, string moduleName, IReadOnlyList<string>? exportAssemblies)
    {
        var list = new List<string>();

        var specified = (exportAssemblies ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().Trim('"'))
            .ToArray();

        var patterns = specified.Length > 0 ? specified : new[] { moduleName + ".dll" };
        foreach (var p in patterns)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            var name = p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? p : p + ".dll";

            try
            {
                if (Path.IsPathRooted(name))
                {
                    if (File.Exists(name)) list.Add(Path.GetFullPath(name));
                    continue;
                }

                if (name.Contains(Path.DirectorySeparatorChar) || name.Contains(Path.AltDirectorySeparatorChar))
                {
                    var rel = Path.GetFullPath(Path.Combine(projectRoot, name));
                    if (File.Exists(rel)) list.Add(rel);
                    continue;
                }

                list.AddRange(Directory.EnumerateFiles(projectRoot, name, SearchOption.AllDirectories));
            }
            catch { }
        }

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}
