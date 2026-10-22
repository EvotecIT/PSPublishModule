namespace PowerForge.Web;

public static partial class WebSiteAuditor
{
    /// <summary>Validates local targets and opt-in owned network destinations through one audit path.</summary>
    private static void AuditPageTargets(AngleSharp.Dom.IDocument doc, string siteRoot, string file,
        string relativePath, WebAuditOptions options, WebOwnedLinkAuditor? ownedLinks,
        Action<string, string, string?, string, string?> AddIssue,
        ref int linkCount, ref int brokenLinkCount, ref int assetCount, ref int missingAssetCount)
    {
        Uri? networkBase = null;
        if (ownedLinks is not null)
        {
            var declaredBase = doc.QuerySelector("base[href]")?.GetAttribute("href")?.Trim();
            if (declaredBase?.StartsWith("//", StringComparison.Ordinal) == true) declaredBase = "https:" + declaredBase;
            if (Uri.TryCreate(declaredBase, UriKind.Absolute, out var candidate) && candidate.Scheme is "http" or "https")
                networkBase = candidate;
        }
        var baseHref = ResolveBaseHref(doc);
        var baseDir = string.IsNullOrWhiteSpace(baseHref)
            ? Path.GetDirectoryName(file) ?? siteRoot
            : Path.GetFullPath(Path.Combine(siteRoot, baseHref.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));

        if (options.CheckLinks)
        {
            foreach (var link in doc.QuerySelectorAll("a[href]"))
            {
                var href = link.GetAttribute("href") ?? string.Empty;
                if (ShouldSkipLink(href)) continue;
                var networkHref = ResolveNetworkAuditTarget(href, networkBase);
                if (WebLinkClassifier.IsExternalLink(networkHref))
                {
                    if (ownedLinks?.Check(networkHref, relativePath, true, out var broken) == true)
                    {
                        linkCount++;
                        if (broken) brokenLinkCount++;
                    }
                    continue;
                }

                if (TryResolveLocalTarget(siteRoot, baseDir, href, out var resolvedPath))
                {
                    linkCount++;
                    if (!File.Exists(resolvedPath))
                    {
                        brokenLinkCount++;
                        AddIssue("error", "link", relativePath, $"broken link '{href}' -> {ToRelative(siteRoot, resolvedPath)}", href);
                    }
                }
            }
        }

        if (options.CheckAssets)
        {
            foreach (var href in GetAssetHrefs(doc))
            {
                if (ShouldSkipLink(href)) continue;
                var networkHref = ResolveNetworkAuditTarget(href, networkBase);
                if (WebLinkClassifier.IsExternalLink(networkHref))
                {
                    if (ownedLinks?.Check(networkHref, relativePath, false, out var missing) == true)
                    {
                        assetCount++;
                        if (missing) missingAssetCount++;
                    }
                    continue;
                }
                if (!TryResolveLocalTarget(siteRoot, baseDir, href, out var resolvedPath)) continue;

                assetCount++;
                if (!File.Exists(resolvedPath))
                {
                    missingAssetCount++;
                    AddIssue("error", "asset", relativePath, $"missing asset '{href}' -> {ToRelative(siteRoot, resolvedPath)}", href);
                }
            }

            foreach (var srcset in GetAssetSrcSets(doc))
            {
                var urls = ParseSrcSet(srcset);
                foreach (var src in urls)
                {
                    if (ShouldSkipLink(src)) continue;
                    var networkSrc = ResolveNetworkAuditTarget(src, networkBase);
                    if (WebLinkClassifier.IsExternalLink(networkSrc))
                    {
                        if (ownedLinks?.Check(networkSrc, relativePath, false, out var missing) == true)
                        {
                            assetCount++;
                            if (missing) missingAssetCount++;
                        }
                        continue;
                    }
                    if (!TryResolveLocalTarget(siteRoot, baseDir, src, out var resolvedPath)) continue;

                    assetCount++;
                    if (!File.Exists(resolvedPath))
                    {
                        missingAssetCount++;
                        AddIssue("error", "asset", relativePath, $"missing asset '{src}' -> {ToRelative(siteRoot, resolvedPath)}", src);
                    }
                }
            }
        }
    }
    private static string ResolveNetworkAuditTarget(string href, Uri? networkBase) =>
        networkBase is not null && Uri.TryCreate(networkBase, href, out var resolved) ? resolved.AbsoluteUri : href.Trim();

}
