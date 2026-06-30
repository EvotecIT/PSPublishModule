namespace PowerForge;

/// <summary>
/// Configuration segment that prepares external files before a module is staged.
/// </summary>
public sealed class ConfigurationExternalAssetSegment : IConfigurationSegment
{
    /// <inheritdoc />
    public string Type => "ExternalAsset";

    /// <summary>External asset bundle configuration.</summary>
    public ExternalAssetConfiguration Configuration { get; set; } = new();
}

/// <summary>
/// Describes a bundle of external files that should be materialized into the module project before staging.
/// </summary>
public sealed class ExternalAssetConfiguration
{
    /// <summary>Enables or disables external asset preparation. Defaults to true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Friendly bundle name written to the generated manifest.</summary>
    public string? Name { get; set; }

    /// <summary>Optional bundle version written to the generated manifest.</summary>
    public string? Version { get; set; }

    /// <summary>Output directory for the downloaded or copied files. Relative paths resolve from the project root.</summary>
    public string? OutputPath { get; set; }

    /// <summary>Optional manifest path. Relative paths resolve from the project root; when omitted, manifest.json is written under OutputPath.</summary>
    public string? ManifestPath { get; set; }

    /// <summary>Optional source URI or project URL written to the generated manifest.</summary>
    public string? Source { get; set; }

    /// <summary>Optional license expression or label written to the generated manifest.</summary>
    public string? License { get; set; }

    /// <summary>When true, existing files are used and missing files fail the build instead of downloading or copying sources.</summary>
    public bool SkipDownload { get; set; }

    /// <summary>Files that make up the external asset bundle.</summary>
    public ExternalAssetFileConfiguration[] Files { get; set; } = Array.Empty<ExternalAssetFileConfiguration>();
}

/// <summary>
/// Describes one file inside an external asset bundle.
/// </summary>
public sealed class ExternalAssetFileConfiguration
{
    /// <summary>Runtime or payload group, such as netcore, netfx, linux-x64, or win-x64.</summary>
    public string Runtime { get; set; } = string.Empty;

    /// <summary>Optional architecture metadata written to the generated manifest.</summary>
    public string? Architecture { get; set; }

    /// <summary>Destination file name when Path is not specified.</summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>Destination path relative to the bundle output directory. Defaults to FileName.</summary>
    public string? Path { get; set; }

    /// <summary>HTTP(S) URI, file URI, rooted local path, or project-relative local path for this file.</summary>
    public string Uri { get; set; } = string.Empty;

    /// <summary>Optional expected SHA256. When provided, mismatches fail the build.</summary>
    public string? Sha256 { get; set; }
}

