using System.Text.Json.Serialization;

namespace PowerForge.Web;

public sealed class ThemeManifest
{
    public string Name { get; set; } = string.Empty;
    public string? Version { get; set; }
    public string? Author { get; set; }
    public string? Engine { get; set; }
    public string? Extends { get; set; }
    public string? DefaultLayout { get; set; }
    public string? LayoutsPath { get; set; }
    public string? PartialsPath { get; set; }
    public string? AssetsPath { get; set; }
    public Dictionary<string, string>? Layouts { get; set; }
    public Dictionary<string, string>? Partials { get; set; }
    public AssetRegistrySpec? Assets { get; set; }
    public Dictionary<string, object?>? Tokens { get; set; }

    [JsonIgnore]
    public ThemeManifest? Base { get; set; }

    [JsonIgnore]
    public string? BaseRoot { get; set; }
}
