namespace PowerForge.Web.Protocols.StructuredData;

/// <summary>Serializes publisher-supplied JSON-LD documents without loading contexts or interpreting RDF.</summary>
public static class JsonLdWriter
{
    /// <summary>Serializes JSON-compatible dictionaries, arrays and scalar values. Supply dates as ISO strings.</summary>
    public static string Serialize(IReadOnlyDictionary<string, object?> document)
    {
        if (document is null) throw new ArgumentNullException(nameof(document));
        return ProtocolJson.Serialize(document);
    }

    /// <summary>Creates a script element, escaping data that could terminate the surrounding HTML element.</summary>
    public static string WriteScript(IReadOnlyDictionary<string, object?> document) =>
        "<script type=\"application/ld+json\">" + Serialize(document) + "</script>";
}
