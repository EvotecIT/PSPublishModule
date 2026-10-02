using System.Xml.Linq;

namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    // Partial declarations can emit multiple entries with the same documentation ID.
    // Preserve the first summary and accumulate complementary documentation once.
    private static void MergeDocumentationMember(
        XElement target, XElement incoming, string name, string xmlPath, List<string> warnings)
    {
        foreach (var element in incoming.Elements())
        {
            var peers = target.Elements(element.Name).ToArray();
            if (peers.Any(peer => XNode.DeepEquals(peer, element))) continue;

            var singleton = element.Name.LocalName is "summary" or "remarks" or "returns" or "value" or "inheritdoc";
            var key = element.Attribute("name")?.Value ?? element.Attribute("cref")?.Value;
            var existing = singleton ? peers.FirstOrDefault() : key is null ? null :
                peers.FirstOrDefault(peer => (peer.Attribute("name")?.Value ?? peer.Attribute("cref")?.Value) == key);
            if (existing is null)
            {
                target.Add(new XElement(element));
            }
            else if (string.IsNullOrWhiteSpace(existing.Value))
            {
                existing.ReplaceWith(new XElement(element));
            }
            else if (element.Name.LocalName == "summary" && existing.Value.Trim() != element.Value.Trim())
            {
                warnings.Add($"API docs: conflicting summaries for '{name}' in '{Path.GetFileName(xmlPath)}'; using the first declaration.");
            }
        }
    }
}
