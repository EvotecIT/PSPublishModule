using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

/// <summary>
/// Reads basic module information from a project directory by inspecting its manifest (.psd1).
/// </summary>
public sealed class ModuleInformationReader
{
    private static readonly ModuleManifestMetadataReader ManifestMetadataReader = new();

    /// <summary>
    /// Finds and reads the single module manifest under <paramref name="projectPath"/>.
    /// </summary>
    /// <exception cref="DirectoryNotFoundException">Thrown when <paramref name="projectPath"/> does not exist.</exception>
    /// <exception cref="FileNotFoundException">Thrown when no manifest was found.</exception>
    /// <exception cref="InvalidOperationException">Thrown when multiple manifests were found.</exception>
    public ModuleInformation Read(string projectPath)
    {
        var root = Path.GetFullPath(projectPath.Trim().Trim('"'));
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Path '{root}' does not exist or is not a directory");

        var manifests = FindPsd1Candidates(root).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (manifests.Length == 0)
            throw new FileNotFoundException($"Path '{root}' doesn't contain PSD1 files");

        if (manifests.Length != 1)
        {
            var foundFiles = string.Join(", ", manifests);
            throw new InvalidOperationException($"More than one PSD1 file detected in '{root}': {foundFiles}");
        }

        var manifestPath = manifests.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(manifestPath))
            throw new InvalidOperationException($"Unable to determine PSD1 file for '{root}'.");
        string? manifestText = null;

        try
        {
            manifestText = File.ReadAllText(manifestPath);
        }
        catch
        {
            // ignore
        }

        var moduleName = Path.GetFileNameWithoutExtension(manifestPath) ?? string.Empty;
        string? moduleVersion = null;
        string? rootModule = null;
        string? powerShellVersion = null;
        string? preRelease = null;
        Guid? guid = null;
        RequiredModuleReference[] requiredModules = Array.Empty<RequiredModuleReference>();

        try
        {
            var metadata = ManifestMetadataReader.Read(manifestPath);
            moduleName = metadata.ModuleName;
            moduleVersion = metadata.ModuleVersion;
            preRelease = metadata.PreRelease;

            var manifestContent = manifestText ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(manifestContent))
            {
                if (ModuleManifestTextParser.TryGetQuotedStringValue(manifestContent, "ModuleVersion", out var resolvedModuleVersion))
                    moduleVersion = resolvedModuleVersion;

                if (ModuleManifestTextParser.TryGetQuotedStringValue(manifestContent, "RootModule", out var resolvedRootModule))
                    rootModule = resolvedRootModule;

                if (ModuleManifestTextParser.TryGetQuotedStringValue(manifestContent, "PowerShellVersion", out var resolvedPowerShellVersion))
                    powerShellVersion = resolvedPowerShellVersion;

                if (ModuleManifestTextParser.TryGetQuotedStringValue(manifestContent, "GUID", out var guidString) &&
                    System.Guid.TryParse(guidString, out var parsedGuid))
                {
                    guid = parsedGuid;
                }

                if (ModuleManifestTextParser.TryGetRequiredModules(manifestContent, out RequiredModuleReference[]? resolvedRequiredModules) &&
                    resolvedRequiredModules is not null)
                {
                    requiredModules = resolvedRequiredModules;
                }
            }
        }
        catch
        {
            // Best-effort: keep partial data if some reads fail.
        }

        return new ModuleInformation(
            moduleName: moduleName,
            manifestPath: manifestPath,
            projectPath: root,
            moduleVersion: moduleVersion,
            rootModule: rootModule,
            powerShellVersion: powerShellVersion,
            preRelease: preRelease,
            guid: guid,
            requiredModules: requiredModules,
            manifestText: manifestText);
    }

    private static IEnumerable<string> FindPsd1Candidates(string root)
    {
        IEnumerable<string> top = Array.Empty<string>();
        try { top = Directory.EnumerateFiles(root, "*.psd1", SearchOption.TopDirectoryOnly); } catch { }
        foreach (var f in top) yield return f;

        IEnumerable<string> subdirs = Array.Empty<string>();
        try { subdirs = Directory.EnumerateDirectories(root); } catch { yield break; }

        foreach (var dir in subdirs)
        {
            IEnumerable<string> inner = Array.Empty<string>();
            try { inner = Directory.EnumerateFiles(dir, "*.psd1", SearchOption.TopDirectoryOnly); } catch { continue; }
            foreach (var f in inner) yield return f;
        }
    }
}
