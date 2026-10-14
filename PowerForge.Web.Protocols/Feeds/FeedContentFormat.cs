namespace PowerForge.Web.Protocols.Feeds;

/// <summary>Classifies feed content media types consistently for reading and writing.</summary>
internal static class FeedContentFormat
{
    internal static bool IsXmlMediaType(string? contentType)
    {
        string mediaType = (contentType ?? string.Empty).Split(';')[0].Trim();
        return mediaType.EndsWith("/xml", StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsHtmlContent(string? contentType)
    {
        // An unspecified type keeps the writer's existing HTML default.
        string mediaType = (contentType ?? "html").Split(';')[0].Trim();
        return string.Equals(mediaType, "html", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mediaType, "xhtml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mediaType, "application/xhtml+xml", StringComparison.OrdinalIgnoreCase);
    }
}
