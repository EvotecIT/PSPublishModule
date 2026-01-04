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
        var moduleName = Path.GetFileNameWithoutExtension(manifestPath) ?? string.Empty;

        string? moduleVersion = null;
        string? rootModule = null;
        string? powerShellVersion = null;
        Guid? guid = null;
        ManifestEditor.RequiredModule[] requiredModules = Array.Empty<ManifestEditor.RequiredModule>();
        string? manifestText = null;

        try
        {
            if (ManifestEditor.TryGetTopLevelString(manifestPath, "ModuleVersion", out var mv))
                moduleVersion = mv;
            if (ManifestEditor.TryGetTopLevelString(manifestPath, "RootModule", out var rm))
                rootModule = rm;
            if (ManifestEditor.TryGetTopLevelString(manifestPath, "PowerShellVersion", out var psv))
                powerShellVersion = psv;
            if (ManifestEditor.TryGetTopLevelString(manifestPath, "GUID", out var guidString))
            {
                if (System.Guid.TryParse(guidString, out var g))
                    guid = g;
            }

            if (ManifestEditor.TryGetRequiredModules(manifestPath, out var req) && req is not null)
                requiredModules = req;
        }
        catch
        {
            // Best-effort: keep partial data if some reads fail.
        }

        try
        {
            manifestText = File.ReadAllText(manifestPath);
        }
        catch
        {
            // ignore
        }

        return new ModuleInformation(
            moduleName: moduleName,
            manifestPath: manifestPath,
            projectPath: root,
            moduleVersion: moduleVersion,
            rootModule: rootModule,
            powerShellVersion: powerShellVersion,
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
