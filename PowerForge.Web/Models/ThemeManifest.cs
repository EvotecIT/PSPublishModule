using System.Text.Json.Serialization;

namespace PowerForge.Web;

/// <summary>Theme manifest metadata and configuration.</summary>
public sealed class ThemeManifest
{
    /// <summary>Theme name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Theme schema version. Use 2 for the portable v2 contract.</summary>
    public int? SchemaVersion { get; set; }
    /// <summary>Theme contract version. Use 2 for the portable v2 contract.</summary>
    public int? ContractVersion { get; set; }
    /// <summary>Optional theme version.</summary>
    public string? Version { get; set; }
    /// <summary>Optional author name.</summary>
    public string? Author { get; set; }
    /// <summary>Template engine identifier.</summary>
    public string? Engine { get; set; }
    /// <summary>Optional base theme name.</summary>
    public string? Extends { get; set; }
    /// <summary>Default layout name.</summary>
    public string? DefaultLayout { get; set; }
    /// <summary>Relative path to layouts folder.</summary>
    public string? LayoutsPath { get; set; }
    /// <summary>Relative path to partials folder.</summary>
    public string? PartialsPath { get; set; }
    /// <summary>Relative path to assets folder.</summary>
    public string? AssetsPath { get; set; }
    /// <summary>Relative path to scripts folder.</summary>
    public string? ScriptsPath { get; set; }
    /// <summary>Named layout mapping.</summary>
    public Dictionary<string, string>? Layouts { get; set; }
    /// <summary>Named partial mapping.</summary>
    public Dictionary<string, string>? Partials { get; set; }
    /// <summary>Named slot mapping used by layouts to inject reusable sections.</summary>
    public Dictionary<string, string>? Slots { get; set; }
    /// <summary>Asset registry configuration.</summary>
    public AssetRegistrySpec? Assets { get; set; }
    /// <summary>Theme token values.</summary>
    public Dictionary<string, object?>? Tokens { get; set; }

    /// <summary>Theme-supported features (for example: docs, apiDocs, blog, search).</summary>
    public string[] Features { get; set; } = Array.Empty<string>();

    /// <summary>Resolved base theme manifest.</summary>
    [JsonIgnore]
    public ThemeManifest? Base { get; set; }

    /// <summary>Resolved base theme root path.</summary>
    [JsonIgnore]
    public string? BaseRoot { get; set; }
}
