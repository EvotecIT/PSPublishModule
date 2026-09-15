namespace PowerForge.Web;

internal static partial class ReleaseHubRenderer
{
    private static string NamespaceReleaseBodyHtml(string html, string? releaseTag)
    {
        if (string.IsNullOrWhiteSpace(html))
            return string.Empty;

        var prefix = Slugify(releaseTag);
        if (string.IsNullOrWhiteSpace(prefix))
            return html;

        // Both Markdown and authored HTML use the renderer's assigned IDs.
        // Slugifying an existing ID would lose Unicode, case, or punctuation.
        var document = HtmlTinkerX.HtmlParser.ParseWithAngleSharp(MarkdownRenderer.InjectHeadingIds(html));
        var headings = document.QuerySelectorAll("h1[id], h2[id], h3[id], h4[id], h5[id], h6[id]");
        var headingSet = headings.ToHashSet();
        var usedIds = document.QuerySelectorAll("[id]")
            .Where(element => !headingSet.Contains(element))
            .Select(element => element.Id ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);
        var remappedHeadingIds = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var heading in headings)
        {
            var originalId = heading.Id;
            if (string.IsNullOrWhiteSpace(originalId))
                continue;

            var baseId = $"{prefix}-{originalId}";
            var namespacedId = baseId;
            var suffix = 2;
            while (!usedIds.Add(namespacedId))
                namespacedId = $"{baseId}-{suffix++}";

            // Always prefix once here, even if an authored ID starts with the
            // release tag. Otherwise distinct original IDs can collapse.
            remappedHeadingIds.TryAdd(originalId, namespacedId);
            heading.Id = namespacedId;
        }

        foreach (var link in document.QuerySelectorAll("a[href]"))
        {
            var href = link.GetAttribute("href") ?? string.Empty;
            if (!href.StartsWith('#'))
                continue;

            var target = Uri.UnescapeDataString(href[1..]);
            if (remappedHeadingIds.TryGetValue(target, out var namespacedTarget))
                link.SetAttribute("href", "#" + Uri.EscapeDataString(namespacedTarget));
        }

        return document.Body?.InnerHtml ?? html;
    }
}
