namespace PowerForge;

/// <summary>Product metadata for a portable Windows application distributed through Scoop.</summary>
internal sealed class PowerForgeReleaseScoopOptions
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Homepage { get; set; } = string.Empty;
    public string License { get; set; } = string.Empty;
    public string Executable { get; set; } = string.Empty;
    public string ShortcutName { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string[] Runtimes { get; set; } = [];
    /// <summary>HTTPS archive URL containing Scoop's $version token and PowerForge's {Runtime} token.</summary>
    public string UrlTemplate { get; set; } = string.Empty;
}
