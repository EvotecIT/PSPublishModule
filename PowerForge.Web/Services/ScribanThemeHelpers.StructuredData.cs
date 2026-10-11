namespace PowerForge.Web;

internal sealed partial class ScribanThemeHelpers
{
    /// <summary>Renders the configured schema using page-local metadata supplied by a theme.</summary>
    public string StructuredData(object? metadata) =>
        WebSiteBuilder.RenderStructuredData(_context.Site, _context.Page, _context.Breadcrumbs, metadata);
}
