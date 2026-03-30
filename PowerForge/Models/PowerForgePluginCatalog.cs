namespace PowerForge;

internal sealed class PowerForgePluginCatalogSpec
{
    public string? ProjectRoot { get; set; }

    public string Configuration { get; set; } = "Release";

    public PowerForgePluginCatalogEntry[] Catalog { get; set; } = Array.Empty<PowerForgePluginCatalogEntry>();
}

internal sealed class PowerForgePluginCatalogEntry
{
    public string Id { get; set; } = string.Empty;

    public string ProjectPath { get; set; } = string.Empty;

    public string[] Groups { get; set; } = Array.Empty<string>();

    public string? Framework { get; set; }

    public string? PackageId { get; set; }

    public string? AssemblyName { get; set; }

    public Dictionary<string, string>? MsBuildProperties { get; set; }

    public PowerForgePluginManifestOptions? Manifest { get; set; }
}

internal sealed class PowerForgePluginManifestOptions
{
    public bool Enabled { get; set; } = true;

    public bool IncludeStandardProperties { get; set; } = true;

    public string FileName { get; set; } = "plugin.manifest.json";

    public string? EntryAssembly { get; set; }

    public string? EntryType { get; set; }

    public string? EntryTypeMatchBaseType { get; set; }

    public Dictionary<string, string>? Properties { get; set; }
}

internal sealed class PowerForgePluginCatalogRequest
{
    public string[] Groups { get; set; } = Array.Empty<string>();

    public string? Configuration { get; set; }

    public string? PreferredFramework { get; set; }

    public string? OutputRoot { get; set; }

    public bool? IncludeSymbols { get; set; }
}

internal sealed class PowerForgePluginPackageRequest
{
    public string[] Groups { get; set; } = Array.Empty<string>();

    public string? Configuration { get; set; }

    public string? OutputRoot { get; set; }

    public bool NoBuild { get; set; }

    public bool IncludeSymbols { get; set; }

    public string? PackageVersion { get; set; }

    public string? VersionSuffix { get; set; }

    public bool PushPackages { get; set; }

    public string? PushSource { get; set; }

    public string? ApiKey { get; set; }

    public bool SkipDuplicate { get; set; } = true;
}

internal sealed class PowerForgePluginFolderExportPlan
{
    public string ProjectRoot { get; set; } = string.Empty;

    public string Configuration { get; set; } = "Release";

    public string OutputRoot { get; set; } = string.Empty;

    public bool IncludeSymbols { get; set; }

    public string[] SelectedGroups { get; set; } = Array.Empty<string>();

    public PowerForgePluginFolderExportEntryPlan[] Entries { get; set; } = Array.Empty<PowerForgePluginFolderExportEntryPlan>();
}

internal sealed class PowerForgePluginFolderExportEntryPlan
{
    public string Id { get; set; } = string.Empty;

    public string ProjectPath { get; set; } = string.Empty;

    public string[] Groups { get; set; } = Array.Empty<string>();

    public string Framework { get; set; } = string.Empty;

    public string PackageId { get; set; } = string.Empty;

    public string AssemblyName { get; set; } = string.Empty;

    public string OutputPath { get; set; } = string.Empty;

    public Dictionary<string, string> MsBuildProperties { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public PowerForgePluginManifestOptions? Manifest { get; set; }
}

internal sealed class PowerForgePluginFolderExportResult
{
    public bool Success { get; set; }

    public string? ErrorMessage { get; set; }

    public PowerForgePluginFolderExportEntryResult[] Entries { get; set; } = Array.Empty<PowerForgePluginFolderExportEntryResult>();
}

internal sealed class PowerForgePluginFolderExportEntryResult
{
    public string Id { get; set; } = string.Empty;

    public string ProjectPath { get; set; } = string.Empty;

    public string Framework { get; set; } = string.Empty;

    public string PackageId { get; set; } = string.Empty;

    public string AssemblyName { get; set; } = string.Empty;

    public string OutputPath { get; set; } = string.Empty;

    public string? ManifestPath { get; set; }

    public int Files { get; set; }

    public long TotalBytes { get; set; }
}

internal sealed class PowerForgePluginPackagePlan
{
    public string ProjectRoot { get; set; } = string.Empty;

    public string Configuration { get; set; } = "Release";

    public string OutputRoot { get; set; } = string.Empty;

    public bool NoBuild { get; set; }

    public bool IncludeSymbols { get; set; }

    public string? PackageVersion { get; set; }

    public string? VersionSuffix { get; set; }

    public bool PushPackages { get; set; }

    public string? PushSource { get; set; }

    public bool SkipDuplicate { get; set; }

    public string[] SelectedGroups { get; set; } = Array.Empty<string>();

    public PowerForgePluginPackageEntryPlan[] Entries { get; set; } = Array.Empty<PowerForgePluginPackageEntryPlan>();
}

internal sealed class PowerForgePluginPackageEntryPlan
{
    public string Id { get; set; } = string.Empty;

    public string ProjectPath { get; set; } = string.Empty;

    public string[] Groups { get; set; } = Array.Empty<string>();

    public string PackageId { get; set; } = string.Empty;

    public string OutputRoot { get; set; } = string.Empty;

    public Dictionary<string, string> MsBuildProperties { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

internal sealed class PowerForgePluginPackageResult
{
    public bool Success { get; set; }

    public string? ErrorMessage { get; set; }

    public PowerForgePluginPackageEntryResult[] Entries { get; set; } = Array.Empty<PowerForgePluginPackageEntryResult>();
}

internal sealed class PowerForgePluginPackageEntryResult
{
    public string Id { get; set; } = string.Empty;

    public string ProjectPath { get; set; } = string.Empty;

    public string PackageId { get; set; } = string.Empty;

    public string OutputRoot { get; set; } = string.Empty;

    public string[] PackagePaths { get; set; } = Array.Empty<string>();

    public string[] SymbolPackagePaths { get; set; } = Array.Empty<string>();

    public DotNetNuGetPushResult[] PushResults { get; set; } = Array.Empty<DotNetNuGetPushResult>();
}
