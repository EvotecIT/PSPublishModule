using OfficeIMO.Markdown;
using System.Net;
using System.Text;

namespace PowerForge.Web;

/// <summary>Maps Markdown navigation links when documentation is imported under different site routes.</summary>
public static class MarkdownLinkMapper
{
    /// <summary>
    /// Replaces configured root-relative directory prefixes in Markdown links, including used reference definitions.
    /// The longest matching prefix wins. Other source text, line endings, code and unrelated destinations are preserved.
    /// </summary>
    /// <param name="markdown">The original Markdown source.</param>
    /// <param name="mappings">Source and destination URL prefixes, each starting and ending with a slash.</param>
    /// <returns>The source with mapped link destinations, or the unchanged source when no links match.</returns>
    public static string Rewrite(string markdown, IReadOnlyDictionary<string, string> mappings)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(mappings);
        foreach (var mapping in mappings)
        {
            ValidatePrefix(mapping.Key);
            ValidatePrefix(mapping.Value);
        }
        if (mappings.Count == 0 || markdown.Length == 0)
            return markdown;

        var prefixes = mappings.OrderByDescending(static mapping => mapping.Key.Length).ToArray();
        // The reader removes a leading BOM when normalizing; retain it outside the parsed source.
        var sourceOffset = markdown[0] == '\uFEFF' ? 1 : 0;
        var parsed = MarkdownReader.ParseWithSyntaxTree(markdown[sourceOffset..], new MarkdownReaderOptions
        {
            PreserveTrivia = true,
            DefinitionLists = false
        });
        var visitor = new LinkVisitor(parsed, prefixes);
        visitor.Visit(parsed.Document);
        if (visitor.Replacements.Count == 0)
            return markdown;

        var output = new StringBuilder(markdown);
        foreach (var replacement in visitor.Replacements.Values.OrderByDescending(static value => value.Start))
        {
            output.Remove(replacement.Start + sourceOffset, replacement.Length);
            output.Insert(replacement.Start + sourceOffset, replacement.Text);
        }
        return output.ToString();
    }

    private static void ValidatePrefix(string prefix)
    {
        if (string.IsNullOrEmpty(prefix) || !prefix.StartsWith('/') || !prefix.EndsWith('/') ||
            prefix.StartsWith("//", StringComparison.Ordinal) || prefix.Any(static ch =>
                char.IsWhiteSpace(ch) || ch is '\\' or '?' or '#'))
            throw new ArgumentException("Markdown link mappings require root-relative directory prefixes starting and ending with '/', without query or fragment components.");
    }

    private sealed class LinkVisitor(MarkdownParseResult parsed, KeyValuePair<string, string>[] prefixes) : MarkdownVisitor
    {
        internal Dictionary<int, Replacement> Replacements { get; } = new();

        protected override void VisitLinkInline(LinkInline link)
        {
            MapDestination(link.Url, link.UrlSourceSpan);
            base.VisitLinkInline(link);
        }

        protected override void VisitImageLinkInline(ImageLinkInline image)
        {
            MapDestination(image.LinkUrl, image.LinkUrlSourceSpan);
            base.VisitImageLinkInline(image);
        }

        protected override void VisitImageBlock(ImageBlock image)
        {
            MapDestination(image.LinkUrl, image.LinkUrlSourceSpan);
            base.VisitImageBlock(image);
        }

        private void MapDestination(string? url, MarkdownSourceSpan? span)
        {
            if (string.IsNullOrEmpty(url) || url.StartsWith("//", StringComparison.Ordinal))
                return;
            foreach (var prefix in prefixes)
            {
                if (!url.StartsWith(prefix.Key, StringComparison.Ordinal))
                    continue;
                if (!span.HasValue || !parsed.TryCreateOriginalSourceSlice(span.Value, out var slice))
                    throw new InvalidOperationException($"Cannot safely map Markdown link destination '{url}'.");

                // Keep literal suffixes verbatim. For an encoded prefix, serialize the decoded destination
                // through the owning Markdown escaper and HTML encoder so entities are not decoded twice.
                var destination = slice.Text.StartsWith(prefix.Key, StringComparison.Ordinal)
                    ? EscapeDestination(prefix.Value) + slice.Text[prefix.Key.Length..]
                    : EscapeDestination(prefix.Value + url[prefix.Key.Length..]);

                Replacements[slice.StartOffset] = new Replacement(
                    slice.StartOffset, slice.EndOffsetInclusive - slice.StartOffset + 1,
                    destination);
                break;
            }
        }

        private static string EscapeDestination(string destination) =>
            MarkdownEscaper.EscapeLinkUrl(WebUtility.HtmlEncode(destination).Replace(" ", "&#32;", StringComparison.Ordinal));
    }

    private sealed record Replacement(int Start, int Length, string Text);
}
