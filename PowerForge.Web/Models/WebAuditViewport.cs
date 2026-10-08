namespace PowerForge.Web;

/// <summary>A viewport used to qualify rendered page layout.</summary>
public sealed class WebAuditViewport
{
    /// <summary>Viewport width in CSS pixels.</summary>
    public int Width { get; set; } = 1280;
    /// <summary>Viewport height in CSS pixels.</summary>
    public int Height { get; set; } = 800;
}
