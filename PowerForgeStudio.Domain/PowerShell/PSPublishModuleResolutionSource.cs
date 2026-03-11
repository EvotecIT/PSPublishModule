namespace PowerForgeStudio.Domain.PowerShell;

public enum PSPublishModuleResolutionSource
{
    EnvironmentOverride = 0,
    RepositoryManifest = 1,
    InstalledModule = 2,
    FallbackPath = 3
}

