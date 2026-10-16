using System.Net;
using HtmlAgilityPack;

namespace PowerForge.Web;

public static partial class WebAssetOptimizer
{
    private static void RewriteHtmlImageTags(
        string siteRoot, string[] htmlFiles, Dictionary<string, ImageRewritePlan> rewritePlans,
        WebAssetOptimizerOptions options, WebOptimizeResult result, Action<string>? onUpdated)
    {
        foreach (var htmlFile in htmlFiles)
        {
            var html = File.ReadAllText(htmlFile);
            if (string.IsNullOrWhiteSpace(html)) continue;
            var documentUri = CreateSiteDocumentUri(ToRelative(siteRoot, htmlFile));
            var documentBase = ResolveDocumentBaseUri(html, documentUri);
            var hintedInFile = 0;
            var rewritten = WebHtmlAttributeRewriter.RewriteElements(html, node =>
            {
                var source = node.Name.Equals("source", StringComparison.OrdinalIgnoreCase);
                var image = node.Name.Equals("img", StringComparison.OrdinalIgnoreCase);
                if (!image && !source) return null;
                if (source && (!options.ImagePreferNextGen || node.Attributes["type"] is not null ||
                    !node.Ancestors("picture").Any())) return null;
                var sourceAttribute = source ? "srcset" : "src";
                var srcValue = WebUtility.HtmlDecode(node.GetAttributeValue(sourceAttribute, "")).Trim();
                // Existing source sets with descriptors or multiple candidates remain authored.
                if (source && (srcValue.Contains(',') || srcValue.Any(char.IsWhiteSpace))) return null;
                if (!TryResolveImageReference(documentBase, srcValue, out var resolvedRelative) ||
                    !rewritePlans.TryGetValue(resolvedRelative, out var plan)) return null;

                var updates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var preferred = BuildImageUrl(documentBase!, srcValue, plan.PreferredRelativePath);
                if (options.ImagePreferNextGen && !string.Equals(preferred, srcValue, StringComparison.Ordinal))
                    updates[sourceAttribute] = preferred;

                if (plan.ResponsiveVariants.Count > 0 && (source || node.Attributes["srcset"] is null))
                {
                    var entries = plan.ResponsiveVariants.OrderBy(variant => variant.Width ?? int.MaxValue)
                        .Where(variant => variant.Width.HasValue)
                        .Select(variant => $"{BuildImageUrl(documentBase!, srcValue, variant.RelativePath)} {variant.Width}w")
                        .ToList();
                    if (entries.Count > 0)
                    {
                        if (plan.OriginalWidth > 0) entries.Add($"{preferred} {plan.OriginalWidth}w");
                        updates["srcset"] = string.Join(", ", entries);
                        if (node.Attributes["sizes"] is null) updates["sizes"] = "100vw";
                    }
                }
                if (image && options.EnhanceImageTags)
                {
                    void Hint(string name, string value)
                    {
                        if (node.Attributes[name] is not null) return;
                        updates[name] = value;
                        hintedInFile++;
                    }
                    if (plan.OriginalWidth > 0) Hint("width", plan.OriginalWidth.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    if (plan.OriginalHeight > 0) Hint("height", plan.OriginalHeight.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    Hint("loading", "lazy");
                    Hint("decoding", "async");
                }
                return updates;
            });
            if (string.Equals(rewritten, html, StringComparison.Ordinal)) continue;
            File.WriteAllText(htmlFile, rewritten);
            result.ImageHtmlRewriteCount++;
            result.ImageHintedCount += hintedInFile;
            onUpdated?.Invoke(htmlFile);
        }
    }

    private static bool TryResolveImageReference(Uri? documentBase, string sourceUrl, out string relativePath)
    {
        relativePath = string.Empty;
        if (documentBase is null || string.IsNullOrWhiteSpace(sourceUrl) ||
            sourceUrl.StartsWith('#') || sourceUrl.StartsWith("//", StringComparison.Ordinal) ||
            (!sourceUrl.StartsWith('/') && Uri.TryCreate(sourceUrl, UriKind.Absolute, out _)) ||
            !Uri.TryCreate(documentBase, sourceUrl, out var resolved) || !HasAssetRewriteOrigin(resolved)) return false;
        relativePath = DecodeUrlPathForLookup(resolved.AbsolutePath.TrimStart('/')) ?? string.Empty;
        return relativePath.Length > 0;
    }

    private static string BuildImageUrl(Uri documentBase, string originalUrl, string targetRelativePath)
    {
        var originalPath = SplitUrlPath(originalUrl, out var suffix);
        var target = new Uri(AssetRewriteOrigin, EncodeUrlPath(targetRelativePath.Replace('\\', '/').TrimStart('/')));
        if (originalPath.StartsWith('/')) return target.AbsolutePath + suffix;
        var relative = new Uri(documentBase, ".").MakeRelativeUri(target).ToString();
        if (originalPath.StartsWith("./", StringComparison.Ordinal) && !relative.StartsWith('.')) relative = "./" + relative;
        return relative + suffix;
    }
}
