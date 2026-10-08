namespace PowerForge;

/// <summary>Catalog inputs prepared from existing installer bytes. This is not an installation or publication receipt.</summary>
internal sealed class ReleaseCatalogPreparationResult
{
    public string PackageIdentifier { get; set; } = string.Empty;
    public string PackageVersion { get; set; } = string.Empty;
    public CatalogInstaller[] Artifacts { get; set; } = Array.Empty<CatalogInstaller>();
    public string[] WingetManifestPaths { get; set; } = Array.Empty<string>();
    public string DesktopPackagesPath { get; set; } = string.Empty;
    public DotNetPublishMsiPackageMetadata[] Installers { get; set; } = Array.Empty<DotNetPublishMsiPackageMetadata>();
}
