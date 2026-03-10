namespace ReleaseOpsStudio.Orchestrator.Catalog;

public sealed class ReleaseCatalogScanOptions
{
    public string RootPath { get; set; } = string.Empty;

    public bool IncludeImmediateChildBuildFolders { get; set; } = true;
}
