using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

/// <summary>
/// High-level build pipeline that stages a module into a temporary folder, builds it in-place, and optionally installs it.
/// </summary>
public sealed class ModuleBuildPipeline
{
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new pipeline that logs progress via <paramref name="logger"/>.
    /// </summary>
    public ModuleBuildPipeline(ILogger logger) => _logger = logger;

    /// <summary>
    /// Copies the module from <see cref="ModuleBuildSpec.SourcePath"/> into staging and builds it there.
    /// </summary>
    /// <param name="spec">Build specification.</param>
    /// <returns>Build result including staging path and computed exports.</returns>
    public ModuleBuildResult BuildToStaging(ModuleBuildSpec spec)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));        
        if (string.IsNullOrWhiteSpace(spec.Name)) throw new ArgumentException("Name is required.", nameof(spec));
        if (string.IsNullOrWhiteSpace(spec.SourcePath)) throw new ArgumentException("SourcePath is required.", nameof(spec));

        var source = Path.GetFullPath(spec.SourcePath);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException($"Source directory not found: {source}");

        spec.Version = ResolveModuleVersionFromManifestIfAuto(
            version: spec.Version,
            manifestPath: Path.Combine(source, $"{spec.Name}.psd1"),
            fallbackVersion: "1.0.0");

        var staging = string.IsNullOrWhiteSpace(spec.StagingPath)
            ? Path.Combine(Path.GetTempPath(), "PowerForge", "build", $"{spec.Name}_{Guid.NewGuid():N}")
            : Path.GetFullPath(spec.StagingPath);

        if (IsChildPath(staging, source))
            throw new InvalidOperationException($"Staging path must not be under SourcePath. SourcePath='{source}', StagingPath='{staging}'.");

        if (Directory.Exists(staging) && Directory.EnumerateFileSystemEntries(staging).Any())
            throw new InvalidOperationException($"Staging directory already exists and is not empty: {staging}");

        Directory.CreateDirectory(staging);

        var excluded = new HashSet<string>((spec.ExcludeDirectories ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);

        var excludedFiles = new HashSet<string>((spec.ExcludeFiles ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim()), StringComparer.OrdinalIgnoreCase);

        _logger.Info($"Staging module '{spec.Name}' from '{source}' to '{staging}'");
        CopyDirectoryFiltered(source, staging, excluded, excludedFiles);

        var builder = new ModuleBuilder(_logger);
        var tfms = spec.Frameworks is { Length: > 0 } ? spec.Frameworks : new[] { "net472", "net8.0" };
        builder.BuildInPlace(new ModuleBuilder.Options
        {
            ProjectRoot = staging,
            ModuleName = spec.Name,
            CsprojPath = string.IsNullOrWhiteSpace(spec.CsprojPath) ? string.Empty : Path.GetFullPath(spec.CsprojPath),
            ModuleVersion = spec.Version,
            Configuration = string.IsNullOrWhiteSpace(spec.Configuration) ? "Release" : spec.Configuration,
            Frameworks = tfms,
            Author = spec.Author,
            CompanyName = spec.CompanyName,
            Description = spec.Description,
            Tags = spec.Tags ?? Array.Empty<string>(),
            IconUri = spec.IconUri,
            ProjectUri = spec.ProjectUri,
            ExportAssemblies = spec.ExportAssemblies ?? Array.Empty<string>(),
            DisableBinaryCmdletScan = spec.DisableBinaryCmdletScan,
        });

        var psd1 = Path.Combine(staging, $"{spec.Name}.psd1");
        if (!File.Exists(psd1))
            throw new FileNotFoundException($"Manifest not found after build: {psd1}");

        var exports = ReadExportsFromManifest(psd1);
        return new ModuleBuildResult(staging, psd1, exports);
    }

    /// <summary>
    /// Installs a staged module to versioned roots, resolving the final version first.
    /// </summary>
    /// <param name="spec">Install specification.</param>
    /// <param name="updateManifestToResolvedVersion">When true, patches the PSD1 ModuleVersion to the resolved version.</param>
    /// <returns>Installer result including resolved version and installed paths.</returns>
    public ModuleInstallerResult InstallFromStaging(ModuleInstallSpec spec, bool updateManifestToResolvedVersion = true)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));        
        if (string.IsNullOrWhiteSpace(spec.Name)) throw new ArgumentException("Name is required.", nameof(spec));
        if (string.IsNullOrWhiteSpace(spec.StagingPath)) throw new ArgumentException("StagingPath is required.", nameof(spec));

        var staging = Path.GetFullPath(spec.StagingPath);
        if (!Directory.Exists(staging)) throw new DirectoryNotFoundException($"Staging directory not found: {staging}");

        spec.Version = ResolveModuleVersionFromManifestIfAuto(
            version: spec.Version,
            manifestPath: Path.Combine(staging, $"{spec.Name}.psd1"),
            fallbackVersion: null);

        var resolved = ModuleInstaller.ResolveTargetVersion(spec.Roots, spec.Name, spec.Version, spec.Strategy);
        if (updateManifestToResolvedVersion)
        {
            try { ManifestEditor.TrySetTopLevelModuleVersion(Path.Combine(staging, $"{spec.Name}.psd1"), resolved); }
            catch { /* best effort */ }
        }

        var installer = new ModuleInstaller(_logger);
        var options = new ModuleInstallerOptions(spec.Roots, InstallationStrategy.Exact, spec.KeepVersions);
        return installer.InstallFromStaging(staging, spec.Name, resolved, options);
    }

    private string ResolveModuleVersionFromManifestIfAuto(string? version, string manifestPath, string? fallbackVersion)
    {
        if (!IsAutoVersion(version)) return version ?? string.Empty;

        try
        {
            if (File.Exists(manifestPath) &&
                ManifestEditor.TryGetTopLevelString(manifestPath, "ModuleVersion", out var v) &&
                !string.IsNullOrWhiteSpace(v))
            {
                _logger.Verbose($"Resolved ModuleVersion from manifest: {manifestPath} -> {v}");
                return v!;
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to read ModuleVersion from manifest: {manifestPath}. Error: {ex.Message}");
        }

        if (!string.IsNullOrWhiteSpace(fallbackVersion))
        {
            _logger.Warn($"Version was 'auto' but ModuleVersion could not be read from: {manifestPath}. Falling back to {fallbackVersion}.");
            return fallbackVersion!;
        }

        throw new InvalidOperationException($"Version was 'auto' but ModuleVersion could not be read from: {manifestPath}. Provide Version explicitly.");
    }

    private static bool IsAutoVersion(string? value)
        => string.IsNullOrWhiteSpace(value) ||
           value!.Trim().Equals("Auto", StringComparison.OrdinalIgnoreCase);

    private static void CopyDirectoryFiltered(string sourceDir, string destDir, ISet<string> excludedDirectoryNames, ISet<string> excludedFileNames)
    {
        var sourceFull = Path.GetFullPath(sourceDir);
        var destFull = Path.GetFullPath(destDir);

        var stack = new Stack<string>();
        stack.Push(sourceFull);

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            var rel = ComputeRelativePath(sourceFull, current);
            var targetDir = string.IsNullOrEmpty(rel) || rel == "." ? destFull : Path.Combine(destFull, rel);
            Directory.CreateDirectory(targetDir);

            foreach (var file in Directory.EnumerateFiles(current, "*", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(file);
                if (!string.IsNullOrEmpty(fileName) && excludedFileNames.Contains(fileName)) continue;
                var destFile = Path.Combine(targetDir, fileName);
                File.Copy(file, destFile, overwrite: true);
            }

            foreach (var dir in Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(dir);
                if (!string.IsNullOrEmpty(name) && excludedDirectoryNames.Contains(name)) continue;
                stack.Push(dir);
            }
        }
    }

    private static string ComputeRelativePath(string baseDir, string fullPath)
    {
        try
        {
            var baseFull = AppendDirectorySeparatorChar(Path.GetFullPath(baseDir));
            var full = Path.GetFullPath(fullPath);
            var pathForUri = Directory.Exists(full) ? AppendDirectorySeparatorChar(full) : full;

            var baseUri = new Uri(baseFull);
            var pathUri = new Uri(pathForUri);
            var rel = Uri.UnescapeDataString(baseUri.MakeRelativeUri(pathUri).ToString());
            return rel.Replace('/', Path.DirectorySeparatorChar);
        }
        catch { return Path.GetFileName(fullPath) ?? fullPath; }
    }

    private static string AppendDirectorySeparatorChar(string path)
        => path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? path
            : path + Path.DirectorySeparatorChar;

    private static bool IsChildPath(string candidateChildPath, string parentPath)
    {
        var parent = Path.GetFullPath(parentPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var child = Path.GetFullPath(candidateChildPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return child.StartsWith(parent, StringComparison.OrdinalIgnoreCase);
    }

    private static ExportSet ReadExportsFromManifest(string psd1Path)
        => new(ReadStringOrArray(psd1Path, "FunctionsToExport"),
            ReadStringOrArray(psd1Path, "CmdletsToExport"),
            ReadStringOrArray(psd1Path, "AliasesToExport"));

    private static string[] ReadStringOrArray(string psd1Path, string key)
    {
        if (ManifestEditor.TryGetTopLevelStringArray(psd1Path, key, out var values) && values is not null)
            return values;
        if (ManifestEditor.TryGetTopLevelString(psd1Path, key, out var value) && !string.IsNullOrWhiteSpace(value))
            return new[] { value! };
        return Array.Empty<string>();
    }
}
