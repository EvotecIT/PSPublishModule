namespace PowerForge;

/// <summary>
/// Reads minimal metadata from a PowerShell module manifest using shared C# parsing helpers.
/// </summary>
public sealed class ModuleManifestMetadataReader
{
    /// <summary>
    /// Reads module name, version, and prerelease metadata from the specified manifest.
    /// </summary>
    /// <param name="manifestPath">Path to the module manifest.</param>
    /// <returns>Resolved module manifest metadata.</returns>
    public ModuleManifestMetadata Read(string manifestPath)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
            throw new ArgumentException("Manifest path is required.", nameof(manifestPath));

        var fullPath = Path.GetFullPath(manifestPath.Trim().Trim('"'));
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Manifest file was not found: {fullPath}", fullPath);

        var content = File.ReadAllText(fullPath);
        var moduleName = Path.GetFileNameWithoutExtension(fullPath) ?? string.Empty;
        var moduleVersion = "0.0.0";
        string? preRelease = null;

        if (ModuleManifestTextParser.TryGetQuotedStringValue(content, "RootModule", out var rootModule) &&
            !string.IsNullOrWhiteSpace(rootModule))
        {
            moduleName = Path.GetFileNameWithoutExtension(rootModule);
        }

        if (ModuleManifestTextParser.TryGetQuotedStringValue(content, "ModuleVersion", out var version) &&
            !string.IsNullOrWhiteSpace(version))
        {
            moduleVersion = version!;
        }

        if (ModuleManifestTextParser.TryGetPsDataStringValue(content, "Prerelease", out var psDataPrerelease) &&
            !string.IsNullOrWhiteSpace(psDataPrerelease))
        {
            preRelease = psDataPrerelease;
        }
        else if (ModuleManifestTextParser.TryGetQuotedStringValue(content, "Prerelease", out var manifestPrerelease) &&
                 !string.IsNullOrWhiteSpace(manifestPrerelease))
        {
            preRelease = manifestPrerelease;
        }

        return new ModuleManifestMetadata(moduleName, moduleVersion, preRelease);
    }
}
