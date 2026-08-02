using System.Xml;

namespace PowerForge.Web;

internal static partial class WebVisualStoryAnimatedArtifactValidator
{
    private readonly record struct SvgFragmentReference(string Id, bool MustBePath);

    private static void AddSmilTargetReference(
        XmlReader reader,
        ICollection<SvgFragmentReference> references,
        bool mustBePath)
    {
        var reference = reader.GetAttribute("href") ??
                        reader.GetAttribute("href", "http://www.w3.org/1999/xlink");
        if (reference is not null && reference.StartsWith("#", StringComparison.Ordinal))
        {
            references.Add(new SvgFragmentReference(reference[1..], mustBePath));
        }
    }

    private static void ValidateSmilFragmentReferences(
        IReadOnlyCollection<SvgElementIdentity> elements,
        IReadOnlyCollection<SvgFragmentReference> references,
        string displayPath)
    {
        foreach (var reference in references)
        {
            var resolved = elements.Any(element =>
                string.Equals(element.Id, reference.Id, StringComparison.Ordinal) &&
                (!reference.MustBePath || string.Equals(element.LocalName, "path", StringComparison.Ordinal)));
            if (!resolved)
            {
                throw new InvalidOperationException(
                    $"Visual-story SVG animation references a missing local fragment: #{reference.Id} ({displayPath})");
            }
        }
    }
}
