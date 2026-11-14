namespace PowerForge.Web;

internal sealed partial class ScribanThemeHelpers
{
    /// <summary>Serializes a string as a quoted JSON literal, safe inside an HTML script element.</summary>
    public string JsonString(string? value) => System.Text.Json.JsonSerializer.Serialize(value);

    /// <summary>Renders the configured schema using page-local metadata supplied by a theme.</summary>
    public string StructuredData(object? metadata) =>
        WebSiteBuilder.RenderStructuredData(_context.Site, _context.Page, _context.Breadcrumbs, metadata, _context.AllItems);
}
