namespace PowerForge.Web;

public sealed class ThemeManifest
{
    public string Name { get; set; } = string.Empty;
    public string? Engine { get; set; }
    public string? LayoutsPath { get; set; }
    public string? PartialsPath { get; set; }
    public string? AssetsPath { get; set; }
}
