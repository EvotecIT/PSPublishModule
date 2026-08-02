using System.Xml;

namespace PowerForge.Web;

internal static partial class WebVisualStoryAnimatedArtifactValidator
{
    private static void ValidatePassiveSvgContent(XmlReader reader, string displayPath)
    {
        if (string.Equals(reader.LocalName, "script", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(reader.LocalName, "foreignObject", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Visual-story SVG artifacts cannot contain executable or embedded active content: {displayPath}");
        }

        if (IsSmilAnimationElement(reader.LocalName))
        {
            var targetAttribute = (reader.GetAttribute("attributeName") ?? string.Empty).Trim();
            if (targetAttribute.StartsWith("on", StringComparison.OrdinalIgnoreCase) ||
                targetAttribute.Equals("href", StringComparison.OrdinalIgnoreCase) ||
                targetAttribute.Equals("xlink:href", StringComparison.OrdinalIgnoreCase) ||
                targetAttribute.Equals("src", StringComparison.OrdinalIgnoreCase) ||
                targetAttribute.Equals("style", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Visual-story SVG artifacts cannot animate active or resource-bearing attributes: {displayPath}");
            }
        }

        if (!reader.HasAttributes)
            return;

        while (reader.MoveToNextAttribute())
        {
            if (reader.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Visual-story SVG artifacts cannot contain event-handler attributes: {displayPath}");
            }
        }
        reader.MoveToElement();
    }

    private static bool IsSmilAnimationElement(string localName)
        => localName.Equals("animate", StringComparison.OrdinalIgnoreCase) ||
           localName.Equals("set", StringComparison.OrdinalIgnoreCase) ||
           localName.Equals("animateTransform", StringComparison.OrdinalIgnoreCase) ||
           localName.Equals("animateMotion", StringComparison.OrdinalIgnoreCase);
}
