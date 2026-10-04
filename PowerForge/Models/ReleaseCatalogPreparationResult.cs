namespace PowerForge;

/// <summary>Catalog inputs prepared from existing installer bytes. This is not an installation or publication receipt.</summary>
internal sealed class ReleaseCatalogPreparationResult
{
    public string[] WingetManifestPaths { get; set; } = Array.Empty<string>();
    public string DesktopPackagesPath { get; set; } = string.Empty;
    public DotNetPublishMsiPackageMetadata[] Installers { get; set; } = Array.Empty<DotNetPublishMsiPackageMetadata>();
}
