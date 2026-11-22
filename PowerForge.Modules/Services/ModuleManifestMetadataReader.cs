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
        => ReadCore(manifestPath, inspect: false);

    internal ModuleManifestMetadata Inspect(string manifestPath)
        => ReadCore(manifestPath, inspect: true);

    private static ModuleManifestMetadata ReadCore(string manifestPath, bool inspect)
    {
        if (string.IsNullOrWhiteSpace(manifestPath))
            throw new ArgumentException("Manifest path is required.", nameof(manifestPath));

        var fullPath = Path.GetFullPath(manifestPath.Trim().Trim('"'));
        if (inspect && !string.Equals(Path.GetExtension(fullPath), ".psd1", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Expected a PowerShell module manifest (.psd1).", nameof(manifestPath));
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Manifest file was not found: {fullPath}", fullPath);

        var content = File.ReadAllText(fullPath);
        var moduleName = Path.GetFileNameWithoutExtension(fullPath) ?? string.Empty;
        var hasVersion = ModuleManifestTextParser.TryGetQuotedStringValue(content, "ModuleVersion", out var version) &&
                         !string.IsNullOrWhiteSpace(version);
        if (inspect && (!hasVersion || !Version.TryParse(version, out _)))
            throw new ArgumentException("The manifest must declare a numeric ModuleVersion.", nameof(manifestPath));

        var moduleVersion = hasVersion ? version! : "0.0.0";
        string? preRelease = null;

        if (!inspect && ModuleManifestTextParser.TryGetQuotedStringValue(content, "RootModule", out var rootModule) &&
            !string.IsNullOrWhiteSpace(rootModule))
        {
            moduleName = Path.GetFileNameWithoutExtension(rootModule);
        }

        try
        {
            if (ModuleManifestTextParser.TryGetPsDataStringValue(content, "Prerelease", out var psDataPrerelease) &&
                !string.IsNullOrWhiteSpace(psDataPrerelease))
            {
                preRelease = psDataPrerelease;
            }
        }
        catch
        {
            // Best-effort: a nested parser issue should not hide the core manifest metadata.
        }

        if (preRelease is null &&
            ModuleManifestTextParser.TryGetQuotedStringValue(content, "Prerelease", out var manifestPrerelease) &&
            !string.IsNullOrWhiteSpace(manifestPrerelease))
        {
            preRelease = manifestPrerelease;
        }

        return new ModuleManifestMetadata(moduleName, moduleVersion, preRelease);
    }
}
