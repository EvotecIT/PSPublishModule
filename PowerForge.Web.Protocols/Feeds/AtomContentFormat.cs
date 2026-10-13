namespace PowerForge.Web.Protocols.Feeds;

/// <summary>Classifies inline Atom XML media types consistently for reading and writing.</summary>
internal static class AtomContentFormat
{
    internal static bool IsXmlMediaType(string? contentType)
    {
        string mediaType = (contentType ?? string.Empty).Split(';')[0].Trim();
        return mediaType.EndsWith("/xml", StringComparison.OrdinalIgnoreCase)
            || mediaType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase);
    }
}
