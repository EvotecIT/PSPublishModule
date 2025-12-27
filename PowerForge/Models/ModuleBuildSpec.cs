namespace PowerForge;

/// <summary>
/// Typed specification for staging a module build (copy source to staging and build in staging).
/// </summary>
public sealed class ModuleBuildSpec
{
    /// <summary>Name of the module being built.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Path to the module source folder (repo working directory).</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// Optional staging path. When null/empty, a temporary staging folder is generated.
    /// </summary>
    public string? StagingPath { get; set; }

    /// <summary>
    /// Optional path to a .NET project (.csproj) to publish into the module. When null/empty, binary build is skipped.
    /// </summary>
    public string? CsprojPath { get; set; }

    /// <summary>Base module version used for manifest patching and install resolution.</summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>Build configuration used for publishing (e.g., Release or Debug).</summary>
    public string Configuration { get; set; } = "Release";

    /// <summary>Target frameworks to publish (e.g., net472, net8.0, net10.0).</summary>
    public string[] Frameworks { get; set; } = Array.Empty<string>();

    /// <summary>Author value written to the manifest (when provided).</summary>
    public string? Author { get; set; }

    /// <summary>CompanyName value written to the manifest (when provided).</summary>
    public string? CompanyName { get; set; }

    /// <summary>Description value written to the manifest (when provided).</summary>
    public string? Description { get; set; }

    /// <summary>Tags written to the manifest PrivateData.PSData (when provided).</summary>
    public string[] Tags { get; set; } = Array.Empty<string>();

    /// <summary>IconUri written to the manifest PrivateData.PSData (when provided).</summary>
    public string? IconUri { get; set; }

    /// <summary>ProjectUri written to the manifest PrivateData.PSData (when provided).</summary>
    public string? ProjectUri { get; set; }

    /// <summary>
    /// Directory names excluded from staging copy (matched by directory name, not by path).
    /// </summary>
    public string[] ExcludeDirectories { get; set; } =
    {
        ".git", ".vs", ".vscode", "bin", "obj", "packages", "node_modules", "Artefacts"
    };

    /// <summary>
    /// File names excluded from staging copy (matched by file name, not by path).
    /// </summary>
    public string[] ExcludeFiles { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Optional assembly file names (for example: <c>My.Module.dll</c>) to scan for cmdlets/aliases when updating manifest exports.
    /// When empty, defaults to <c>&lt;Name&gt;.dll</c>.
    /// </summary>
    public string[] ExportAssemblies { get; set; } = Array.Empty<string>();

    /// <summary>
    /// When true, skips binary cmdlet/alias scanning and keeps existing manifest <c>CmdletsToExport</c>/<c>AliasesToExport</c> values.
    /// </summary>
    public bool DisableBinaryCmdletScan { get; set; }

    /// <summary>
    /// When true, keeps the staging directory after a successful build.
    /// </summary>
    public bool KeepStaging { get; set; }
}
