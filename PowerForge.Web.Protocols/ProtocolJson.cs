namespace PowerForge.Web.Protocols;

/// <summary>Uses the platform JSON serializer; no JSON engine is shipped with this library.</summary>
internal static class ProtocolJson
{
    internal static string Serialize(object value, bool indented = false)
    {
#if NETFRAMEWORK
        // Framework provides this serializer in-box. Modern targets use System.Text.Json in-box.
        // The Framework default of 2 MiB rejects valid 10,000-URL IndexNow batches.
        var json = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Serialize(value);
#else
        var json = System.Text.Json.JsonSerializer.Serialize(value,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = indented });
#endif
        // JSON is also embedded in script elements: a publisher string must not close the element.
        return json.Replace("<", "\\u003C").Replace(">", "\\u003E").Replace("&", "\\u0026")
            .Replace("'", "\\u0027").Replace("\u2028", "\\u2028").Replace("\u2029", "\\u2029");
    }
}
