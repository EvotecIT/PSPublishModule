using System.Xml;
using System.Xml.Linq;

namespace PowerForge.Web.Protocols;

internal static class ProtocolXml
{
    internal static XDocument Parse(string xml)
    {
        if (xml is null) throw new ArgumentNullException(nameof(xml));
        using var input = new StringReader(xml);
        using var reader = XmlReader.Create(input, Settings());
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    internal static XDocument Parse(Stream input)
    {
        if (input is null) throw new ArgumentNullException(nameof(input));
        using var reader = XmlReader.Create(input, Settings());
        return XDocument.Load(reader, LoadOptions.PreserveWhitespace);
    }

    private static XmlReaderSettings Settings() => new()
    {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = 64L * 1024 * 1024, CloseInput = false
    };
}
