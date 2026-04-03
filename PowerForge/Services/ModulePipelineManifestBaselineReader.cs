using System.IO;
using System.Linq;

namespace PowerForge;

internal sealed class ModulePipelineManifestBaseline
{
    public ManifestConfiguration Manifest { get; set; } = new();
    public RequiredModuleReference[] RequiredModules { get; set; } = Array.Empty<RequiredModuleReference>();
    public string[] ExternalModuleDependencies { get; set; } = Array.Empty<string>();
}

internal static class ModulePipelineManifestBaselineReader
{
    internal static ModulePipelineManifestBaseline? TryRead(string projectRoot, string moduleName, ILogger logger)
    {
        var manifestPath = Path.Combine(projectRoot, moduleName + ".psd1");
        if (!File.Exists(manifestPath))
            return null;

        try
        {
            var manifest = new ManifestConfiguration
            {
                ModuleVersion = ModuleManifestValueReader.ReadTopLevelString(manifestPath, "ModuleVersion") ?? string.Empty,
                CompatiblePSEditions = NormalizeArray(ModuleManifestValueReader.ReadTopLevelStringOrArray(manifestPath, "CompatiblePSEditions")),
                Guid = ModuleManifestValueReader.ReadTopLevelString(manifestPath, "GUID") ?? string.Empty,
                Author = ModuleManifestValueReader.ReadTopLevelString(manifestPath, "Author") ?? string.Empty,
                CompanyName = ModuleManifestValueReader.ReadTopLevelString(manifestPath, "CompanyName"),
                Copyright = ModuleManifestValueReader.ReadTopLevelString(manifestPath, "Copyright"),
                Description = ModuleManifestValueReader.ReadTopLevelString(manifestPath, "Description"),
                PowerShellVersion = ModuleManifestValueReader.ReadTopLevelString(manifestPath, "PowerShellVersion") ?? string.Empty,
                Tags = NormalizeArray(ModuleManifestValueReader.ReadPsDataStringOrArray(manifestPath, "Tags")),
                IconUri = ReadPsDataSingleString(manifestPath, "IconUri"),
                ProjectUri = ReadPsDataSingleString(manifestPath, "ProjectUri"),
                DotNetFrameworkVersion = ModuleManifestValueReader.ReadTopLevelString(manifestPath, "DotNetFrameworkVersion"),
                LicenseUri = ReadPsDataSingleString(manifestPath, "LicenseUri"),
                Prerelease = ModuleManifestValueReader.ReadTopLevelString(manifestPath, "Prerelease")
                    ?? ReadPsDataSingleString(manifestPath, "Prerelease"),
                FormatsToProcess = NormalizeArray(ModuleManifestValueReader.ReadTopLevelStringOrArray(manifestPath, "FormatsToProcess"))
            };

            return new ModulePipelineManifestBaseline
            {
                Manifest = manifest,
                RequiredModules = NormalizeRequiredModules(ModuleManifestValueReader.ReadRequiredModules(manifestPath)),
                ExternalModuleDependencies = NormalizeArray(ModuleManifestValueReader.ReadPsDataStringOrArray(manifestPath, "ExternalModuleDependencies"))
            };
        }
        catch (Exception ex)
        {
            logger.Warn($"Failed to read existing project manifest for baseline awareness: {manifestPath}. Error: {ex.Message}");
            return null;
        }
    }

    private static string? ReadPsDataSingleString(string manifestPath, string key)
    {
        var values = NormalizeArray(ModuleManifestValueReader.ReadPsDataStringOrArray(manifestPath, key));
        return values.Length == 0 ? null : values[0];
    }

    private static string[] NormalizeArray(string[] values)
    {
        return (values ?? Array.Empty<string>())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static RequiredModuleReference[] NormalizeRequiredModules(RequiredModuleReference[] values)
    {
        return (values ?? Array.Empty<RequiredModuleReference>())
            .Where(v => v is not null && !string.IsNullOrWhiteSpace(v.ModuleName))
            .Select(v =>
            {
                var entry = v!;
                return new RequiredModuleReference(
                    moduleName: entry.ModuleName.Trim(),
                    moduleVersion: NormalizeNullable(entry.ModuleVersion),
                    requiredVersion: NormalizeNullable(entry.RequiredVersion),
                    maximumVersion: NormalizeNullable(entry.MaximumVersion),
                    guid: NormalizeNullable(entry.Guid));
            })
            .ToArray();
    }

    private static string? NormalizeNullable(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value!.Trim();
    }
}
