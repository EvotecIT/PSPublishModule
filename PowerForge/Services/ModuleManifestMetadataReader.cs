using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Reads minimal metadata from a PowerShell module manifest using shared C# parsing helpers.
/// </summary>
public sealed class ModuleManifestMetadataReader
{
    private static readonly Regex RootModuleRegex = new(@"(?im)^\s*RootModule\s*=\s*['""](?<value>[^'""]+)['""]", RegexOptions.Compiled);
    private static readonly Regex ModuleVersionRegex = new(@"(?im)^\s*ModuleVersion\s*=\s*['""](?<value>[^'""]+)['""]", RegexOptions.Compiled);
    private static readonly Regex PreReleaseRegex = new(@"(?im)^\s*Prerelease\s*=\s*['""](?<value>[^'""]+)['""]", RegexOptions.Compiled);

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

        var moduleName = Path.GetFileNameWithoutExtension(fullPath) ?? string.Empty;
        var moduleVersion = "0.0.0";
        string? preRelease = null;
        var content = File.ReadAllText(fullPath);

        var rootModuleMatch = RootModuleRegex.Match(content);
        if (rootModuleMatch.Success)
            moduleName = Path.GetFileNameWithoutExtension(rootModuleMatch.Groups["value"].Value);

        var versionMatch = ModuleVersionRegex.Match(content);
        if (versionMatch.Success)
            moduleVersion = versionMatch.Groups["value"].Value;

        var preReleaseMatch = PreReleaseRegex.Match(content);
        if (preReleaseMatch.Success)
            preRelease = preReleaseMatch.Groups["value"].Value;

        return new ModuleManifestMetadata(moduleName, moduleVersion, preRelease);
    }
}
