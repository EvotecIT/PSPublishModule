using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
        /// <summary>Path to the .NET project that should be published into the module Lib folder.</summary>
        public string CsprojPath { get; set; } = string.Empty;
        /// <summary>Build configuration used for publishing (e.g., Release).</summary>
        public string Configuration { get; set; } = "Release";
        /// <summary>Target frameworks to publish (e.g., net472, net8.0).</summary>
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
        if (string.IsNullOrWhiteSpace(opts.CsprojPath) || !File.Exists(opts.CsprojPath))
            throw new FileNotFoundException($"Project file not found: {opts.CsprojPath}");

        var libRoot = Path.Combine(opts.ProjectRoot, "Lib");
        var coreDir = Path.Combine(libRoot, "Core");
        var defDir  = Path.Combine(libRoot, "Default");
        if (Directory.Exists(libRoot)) Directory.Delete(libRoot, recursive: true);
        Directory.CreateDirectory(coreDir);
        Directory.CreateDirectory(defDir);

        // 1) Build libraries (dotnet publish) per framework and copy to Lib/<Core|Default>
        var publisher = new DotnetPublisher(_logger);
        var publishes = publisher.Publish(opts.CsprojPath, opts.Configuration, opts.Frameworks, opts.ModuleVersion);
        foreach (var kv in publishes)
        {
            var tfm = kv.Key;
            var src = kv.Value;
            var target = IsCore(tfm) ? coreDir : defDir;
            CopyFiltered(src, target, static p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".so", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase));
        }

        // 2) Manifest generation
        var psd1 = Path.Combine(opts.ProjectRoot, $"{opts.ModuleName}.psd1");
        // Prefer a script RootModule for compatibility; load binary via NestedModules
        var rootModule = $"{opts.ModuleName}.psm1";
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

        if (opts.Tags.Count > 0) BuildServices.SetPsDataStringArray(psd1, "Tags", opts.Tags.ToArray());
        if (!string.IsNullOrWhiteSpace(opts.IconUri)) BuildServices.SetPsDataString(psd1, "IconUri", opts.IconUri!);
        if (!string.IsNullOrWhiteSpace(opts.ProjectUri)) BuildServices.SetPsDataString(psd1, "ProjectUri", opts.ProjectUri!);

        // 3) Exports
        var moduleDlls = Directory.EnumerateFiles(opts.ProjectRoot, $"{opts.ModuleName}.dll", SearchOption.AllDirectories).ToArray();
        if (moduleDlls.Length == 0)
            _logger.Warn($"No '{opts.ModuleName}.dll' found under staging; binary cmdlet export detection will be skipped.");
        var exports = BuildServices.ComputeExports(publicFolderPath: Path.Combine(opts.ProjectRoot, "Public"), assemblies: moduleDlls);
        BuildServices.SetManifestExports(psd1, functions: exports.Functions, cmdlets: exports.Cmdlets, aliases: exports.Aliases);
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
        => tfm.StartsWith("netcoreapp", StringComparison.OrdinalIgnoreCase) || tfm.StartsWith("net5", StringComparison.OrdinalIgnoreCase) || tfm.StartsWith("net6", StringComparison.OrdinalIgnoreCase) || tfm.StartsWith("net7", StringComparison.OrdinalIgnoreCase) || tfm.StartsWith("net8", StringComparison.OrdinalIgnoreCase) || tfm.StartsWith("net9", StringComparison.OrdinalIgnoreCase);

    private static void CopyFiltered(string sourceDir, string destDir, Func<string, bool> filePredicate)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var rel  = ComputeRelativePath(sourceDir, file);
            var dest = Path.Combine(destDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            if (filePredicate(file)) File.Copy(file, dest, overwrite: true);    
        }
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
        => path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? path : path + Path.DirectorySeparatorChar;
}
