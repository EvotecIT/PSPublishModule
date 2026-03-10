namespace ReleaseOpsStudio.Domain.PowerShell;

public sealed record PSPublishModuleResolution(
    PSPublishModuleResolutionSource Source,
    string ManifestPath,
    string? ModuleVersion,
    bool IsUsable,
    string? Warning = null)
{
    public string SourceDisplay => Source switch
    {
        PSPublishModuleResolutionSource.EnvironmentOverride => "Environment override",
        PSPublishModuleResolutionSource.RepositoryManifest => "Repository manifest",
        PSPublishModuleResolutionSource.InstalledModule => "Installed module",
        PSPublishModuleResolutionSource.FallbackPath => "Fallback path",
        _ => "Unknown source"
    };

    public string VersionDisplay => string.IsNullOrWhiteSpace(ModuleVersion) ? "Version unknown" : ModuleVersion!;

    public string StatusDisplay => !IsUsable
        ? "Blocked"
        : string.IsNullOrWhiteSpace(Warning)
            ? "Ready"
            : "Watch";
}
