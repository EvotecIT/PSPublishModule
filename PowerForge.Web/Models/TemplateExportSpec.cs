namespace PowerForge.Web;

/// <summary>Renders a theme layout using an existing page's content, data, and navigation context.</summary>
public sealed class TemplateExportSpec
{
    /// <summary>Unique lowercase name used for the file under <c>_powerforge/fragments/{name}.html</c>.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Canonical route of a non-draft page included in this build, for example <c>/docs/</c>.</summary>
    public string SourceRoute { get; set; } = string.Empty;
    /// <summary>Theme layout to render instead of the source page's normal layout.</summary>
    public string Layout { get; set; } = string.Empty;
}
