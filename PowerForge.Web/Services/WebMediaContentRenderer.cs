using System.Text;
using System.Web;
using HtmlAgilityPack;
using HtmlTinkerX;

namespace PowerForge.Web;

/// <summary>Enhances explicitly scoped content while preserving authored markup and link destinations.</summary>
internal static class WebMediaContentRenderer
{
    internal static string Render(string html)
    {
        if (!html.Contains("data-pf-media-scope", StringComparison.OrdinalIgnoreCase)) return html;
        var document = HtmlParser.ParseWithHtmlAgilityPack(html);
        var edits = new List<(int Start, int Length, string Value)>();
        var language = document.DocumentNode.SelectSingleNode("//html")?.GetAttributeValue("lang", "en").Trim().Split('-')[0].ToLowerInvariant();
        var labels = language switch {
            "pl" => ("Powiększ obraz", "Zobacz obrazy"),
            "fr" => ("Agrandir l’image", "Voir les images"),
            "de" => ("Bild vergrößern", "Bilder ansehen"),
            "es" => ("Ampliar imagen", "Ver imágenes"),
            _ => ("Enlarge image", "View images")
        };
        var scopes = document.DocumentNode.Descendants().Where(node => node.Attributes["data-pf-media-scope"] is not null).ToArray();
        int sequence = 0;
        foreach (var scope in scopes)
        {
            string mode = scope.GetAttributeValue("data-pf-media-scope", "");
            if (mode is not ("content" or "single" or "previews") || scope.Ancestors().Any(node => node.GetAttributeValue("data-pf-media-scope", "") == "off")) continue;
            string group = "pf-content-" + ++sequence;
            var entries = new List<string>();
            foreach (var image in scope.Descendants("img"))
            {
                if (image.Attributes["data-pf-media-preview-source"] is not null) continue;
                if (image.Ancestors().FirstOrDefault(node => node.Attributes["data-pf-media-scope"] is not null) != scope) continue;
                if (image.AncestorsAndSelf().Any(node => node.GetAttributeValue("data-pf-media", "") == "off" ||
                    node.Attributes["hidden"] is not null || node.GetAttributeValue("aria-hidden", "") == "true" ||
                    node.Name is "button" or "pre" or "code" or "template")) continue;
                var target = image.ParentNode.Name == "picture" ? image.ParentNode : image;
                string src = HttpUtility.HtmlDecode(image.GetAttributeValue("src", "")).Trim();
                if (src.Length == 0)
                {
                    // Only a fallback link is chosen here; the browser still selects the displayed currentSrc.
                    var sourceSets = new[] { image }.Concat(target == image ? Array.Empty<HtmlNode>() : target.Elements("source"));
                    src = sourceSets.Select(node => HttpUtility.HtmlDecode(node.GetAttributeValue("srcset", "")))
                        .SelectMany(value => WebSourceSetUrls.Ranges(value).Select(range => value.Substring(range.Start, range.Length)))
                        .FirstOrDefault(SafeImageUrl) ?? "";
                }
                string alt = HttpUtility.HtmlDecode(image.GetAttributeValue("alt", "")).Trim();
                if (alt.Length == 0 || !SafeImageUrl(src)) continue;
                if (int.TryParse(image.GetAttributeValue("width", ""), out int width) && width < 96 ||
                    int.TryParse(image.GetAttributeValue("height", ""), out int height) && height < 64) continue;
                // Exclusions are explicit source URLs supplied by the content owner, not filename guesses.
                if (scope.GetAttributeValue("data-pf-media-exclude", "").Split('|').Contains(src, StringComparer.Ordinal)) continue;
                var existing = image.Ancestors().FirstOrDefault(node => node.Name == "a");
                if (existing is not null && (mode != "previews" || existing.Attributes["download"] is not null || existing.Attributes["data-pf-media"] is not null || existing.Descendants("img").Count() != 1)) continue;
                if (existing is null && target.ParentNode.Name == "p" && !string.IsNullOrWhiteSpace(HttpUtility.HtmlDecode(target.ParentNode.InnerText))) continue;
                string id = "pf-content-image-" + sequence + "-" + entries.Count;
                while (document.GetElementbyId(id) is not null) id += "-preview";
                string caption = alt;
                var heading = mode == "previews" ? null : scope.Descendants().LastOrDefault(node =>
                    node.OuterStartIndex < image.OuterStartIndex && node.Name is "h2" or "h3" or "h4" &&
                    node.Ancestors().FirstOrDefault(parent => parent.Attributes["data-pf-media-scope"] is not null) == scope);
                if (heading is not null) caption = HttpUtility.HtmlDecode(heading.InnerText).Trim() + " · " + alt;
                string attributes = $" id=\"{id}\" data-pf-media data-pf-media-context data-pf-media-caption=\"{Encode(caption)}\"";
                if (mode != "single") attributes += $" data-pf-media-group=\"{group}\"";
                if (existing is null)
                {
                    if (!ValidRange(target, html)) continue;
                    int length = SourceEnd(target, html) - target.OuterStartIndex;
                    string original = html.Substring(target.OuterStartIndex, length);
                    edits.Add((target.OuterStartIndex, length,
                        $"<a class=\"pf-media-content-image\" href=\"{Encode(src)}\"{attributes} aria-label=\"{Encode(labels.Item1 + ": " + alt)}\">{original}</a>"));
                }
                else
                {
                    if (!ValidRange(image, html) || !ValidRange(existing, html)) continue;
                    edits.Add((image.OuterStartIndex + 4, 0, " data-pf-media-preview-source"));
                    string imageId = image.GetAttributeValue("id", "");
                    if (imageId.Length == 0)
                    {
                        imageId = id + "-source";
                        while (document.GetElementbyId(imageId) is not null) imageId += "-preview";
                        edits.Add((image.OuterStartIndex + 4, 0, $" id=\"{imageId}\""));
                    }
                    edits.Add((SourceEnd(existing, html), 0,
                        $"<a class=\"pf-media-preview-action\" href=\"{Encode(src)}\"{attributes} data-pf-media-for=\"{Encode(imageId)}\">{Encode(labels.Item1)}</a>"));
                }
                entries.Add(id);
            }
            if (entries.Count > 1 && mode != "single")
            {
                int start = scope.InnerStartIndex;
                if (start > 0 && start < html.Length)
                    edits.Add((start, 0, $"<div class=\"pf-media-content-toolbar\"><a href=\"#{entries[0]}\" data-pf-media-open-group=\"{group}\">{Encode(labels.Item2)}{(mode == "previews" ? "" : " (" + entries.Count + ")")}</a></div>"));
            }
        }
        var result = new StringBuilder(html);
        foreach (var edit in edits.OrderByDescending(edit => edit.Start)) result.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Value);
        return result.ToString();
    }

    private static string Encode(string value) => HttpUtility.HtmlAttributeEncode(value);
    private static int SourceEnd(HtmlNode node, string html) => node.EndNode != node && node.EndNode.OuterStartIndex >= 0
        ? html.IndexOf('>', node.EndNode.OuterStartIndex) + 1
        : node.OuterStartIndex + node.OuterLength;
    private static bool ValidRange(HtmlNode node, string html) => node.OuterStartIndex >= 0 && SourceEnd(node, html) > node.OuterStartIndex && SourceEnd(node, html) <= html.Length;
    private static bool SafeImageUrl(string value) => value.Length > 0 && !value.StartsWith('#') &&
        !value.Any(char.IsControl) && !value.Contains('\\') &&
        (Uri.TryCreate(value, UriKind.Relative, out _) ||
         Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https");
}
