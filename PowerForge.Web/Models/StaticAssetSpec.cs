namespace PowerForge.Web;

/// <summary>Defines a static asset copy mapping.</summary>
public sealed class StaticAssetSpec
{
    /// <summary>Source file or directory.</summary>
    public string Source { get; set; } = string.Empty;
    /// <summary>Optional destination override.</summary>
    public string? Destination { get; set; }
}
