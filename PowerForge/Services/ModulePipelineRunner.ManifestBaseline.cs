using System;
using System.IO;
using System.Linq;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private sealed class ProjectManifestBaseline
    {
        public ManifestConfiguration Manifest { get; set; } = new();
    }

    private ProjectManifestBaseline? TryReadProjectManifestBaseline(string projectRoot, string moduleName)
    {
        var manifestPath = Path.Combine(projectRoot, moduleName + ".psd1");
        if (!File.Exists(manifestPath))
            return null;

        try
        {
            var manifest = new ManifestConfiguration
            {
                ModuleVersion = ReadTopLevelString(manifestPath, "ModuleVersion") ?? string.Empty,
                CompatiblePSEditions = ReadTopLevelStringArray(manifestPath, "CompatiblePSEditions"),
                Guid = ReadTopLevelString(manifestPath, "GUID") ?? string.Empty,
                Author = ReadTopLevelString(manifestPath, "Author") ?? string.Empty,
                CompanyName = ReadTopLevelString(manifestPath, "CompanyName"),
                Copyright = ReadTopLevelString(manifestPath, "Copyright"),
                Description = ReadTopLevelString(manifestPath, "Description"),
                PowerShellVersion = ReadTopLevelString(manifestPath, "PowerShellVersion") ?? string.Empty,
                Tags = ReadPsDataStringArray(manifestPath, "Tags"),
                IconUri = ReadPsDataSingleString(manifestPath, "IconUri"),
                ProjectUri = ReadPsDataSingleString(manifestPath, "ProjectUri"),
                DotNetFrameworkVersion = ReadTopLevelString(manifestPath, "DotNetFrameworkVersion"),
                LicenseUri = ReadPsDataSingleString(manifestPath, "LicenseUri"),
                Prerelease = ReadTopLevelString(manifestPath, "Prerelease")
                    ?? ReadPsDataSingleString(manifestPath, "Prerelease"),
                FormatsToProcess = ReadTopLevelStringArray(manifestPath, "FormatsToProcess")
            };

            return new ProjectManifestBaseline
            {
                Manifest = manifest
            };
        }
        catch (Exception ex)
        {
            _logger.Warn($"Failed to read existing project manifest for baseline awareness: {manifestPath}. Error: {ex.Message}");
            return null;
        }
    }

    private static string? ReadTopLevelString(string manifestPath, string key)
    {
        if (!ManifestEditor.TryGetTopLevelString(manifestPath, key, out var value))
            return null;

        if (value is null)
            return null;

        var normalized = value.Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static string[] ReadTopLevelStringArray(string manifestPath, string key)
    {
        if (!ManifestEditor.TryGetTopLevelStringArray(manifestPath, key, out var values) || values is null)
            return Array.Empty<string>();

        return NormalizeArray(values);
    }

    private static string[] ReadPsDataStringArray(string manifestPath, string key)
    {
        if (!ManifestEditor.TryGetPsDataStringArray(manifestPath, key, out var values) || values is null)
            return Array.Empty<string>();

        return NormalizeArray(values);
    }

    private static string? ReadPsDataSingleString(string manifestPath, string key)
    {
        var values = ReadPsDataStringArray(manifestPath, key);
        if (values.Length == 0)
            return null;

        return values[0];
    }

    private static string[] NormalizeArray(string[] values)
    {
        return (values ?? Array.Empty<string>())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
