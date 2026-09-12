namespace PowerForge.Web;

/// <summary>Enables a local, accessible image viewer without replacing ordinary image links.</summary>
public sealed class MediaViewerSpec
{
    /// <summary>Emits the viewer's shared script and stylesheet. Disabled by default.</summary>
    public bool Enabled { get; set; }
    /// <summary>CSS selector for image links or buttons to enhance. Use data-pf-media-group to group images.</summary>
    public string Selector { get; set; } = "[data-pf-media]";
}
