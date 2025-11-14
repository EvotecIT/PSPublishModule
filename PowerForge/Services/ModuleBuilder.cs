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
    public ModuleBuilder(ILogger logger) => _logger = logger;

    public sealed class Options
    {
        public string ProjectRoot { get; init; } = string.Empty;            // e.g., Module folder
        public string ModuleName  { get; init; } = string.Empty;            // e.g., PSPublishModule
        public string CsprojPath  { get; init; } = string.Empty;            // path to csproj to publish
        public string Configuration { get; init; } = "Release";
        public IReadOnlyList<string> Frameworks { get; init; } = Array.Empty<string>(); // e.g., net472, net8.0
        public string ModuleVersion { get; init; } = "1.0.0";
        public InstallationStrategy Strategy { get; init; } = InstallationStrategy.AutoRevision;
        public int KeepVersions { get; init; } = 3;
        public IReadOnlyList<string> InstallRoots { get; init; } = Array.Empty<string>();
        public string? Author { get; init; }
        public string? CompanyName { get; init; }
        public string? Description { get; init; }
        public IReadOnlyList<string> CompatiblePSEditions { get; init; } = new[] { "Desktop", "Core" };
        public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();
        public string? IconUri { get; init; }
        public string? ProjectUri { get; init; }
    }

    public ModuleInstallerResult Build(Options opts)
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

        // Ensure NestedModules includes the binary
        ManifestEditor.TrySetTopLevelStringArray(psd1, "NestedModules", new[] { $"{opts.ModuleName}.dll" });

        if (opts.Tags.Count > 0) BuildServices.SetPsDataStringArray(psd1, "Tags", opts.Tags.ToArray());
        if (!string.IsNullOrWhiteSpace(opts.IconUri)) BuildServices.SetPsDataString(psd1, "IconUri", opts.IconUri!);
        if (!string.IsNullOrWhiteSpace(opts.ProjectUri)) BuildServices.SetPsDataString(psd1, "ProjectUri", opts.ProjectUri!);

        // 3) Exports: only binary cmdlets and aliases; functions from scripts are none for binary module
        var dlls = Directory.EnumerateFiles(opts.ProjectRoot, "*.dll", SearchOption.AllDirectories).ToArray();
        var exports = BuildServices.ComputeExports(publicFolderPath: Path.Combine(opts.ProjectRoot, "Public"), assemblies: dlls);
        BuildServices.SetManifestExports(psd1, functions: Array.Empty<string>(), cmdlets: exports.Cmdlets, aliases: exports.Aliases);

        // 4) Install versioned
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
            var rel  = Path.GetRelativePath(sourceDir, file);
            var dest = Path.Combine(destDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            if (filePredicate(file)) File.Copy(file, dest, overwrite: true);
        }
    }
}
