using System.Text.RegularExpressions;
using AngleSharp.Dom;
using HtmlTinkerX;

namespace PowerForge.Web;

public static partial class WebAgentReadiness
{
    private static readonly Regex WebMcpToolNameRegex = new(
        @"^[A-Za-z0-9_-]{1,128}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    internal static void ValidateWebMcpConfiguration(AgentReadinessSpec spec)
    {
        if (!spec.WebMcp)
            return;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tool in spec.WebMcpTools ?? Array.Empty<AgentWebMcpToolSpec>())
        {
            if (tool is null)
                throw new ArgumentException("WebMCP tool entries cannot be null.");
            var toolName = tool.Name ?? string.Empty;
            if (!WebMcpToolNameRegex.IsMatch(toolName))
                throw new ArgumentException($"WebMCP tool name '{tool.Name}' must contain 1-128 letters, digits, underscores, or hyphens.");
            if (string.IsNullOrWhiteSpace(tool.Description))
                throw new ArgumentException($"WebMCP tool '{tool.Name}' requires a description.");
            if (string.IsNullOrWhiteSpace(tool.Kind))
                throw new ArgumentException($"WebMCP tool '{tool.Name}' requires an implementation kind.");
            if (!string.Equals(tool.Kind.Trim(), "site-search", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"WebMCP tool '{tool.Name}' uses unsupported implementation kind '{tool.Kind}'. PowerForge Phase 1 supports 'site-search'.");
            if (!tool.ReadOnly)
                throw new ArgumentException($"WebMCP tool '{tool.Name}' uses the read-only 'site-search' implementation and cannot be declared writable.");

            ValidateWebMcpRoute(tool.Route, toolName);
            var key = toolName + "\n" + NormalizeRoute(tool.Route);
            if (!seen.Add(key))
                throw new ArgumentException($"WebMCP tool '{tool.Name}' is configured more than once for route '{NormalizeRoute(tool.Route)}'.");
        }
    }

    private static void ValidateWebMcpRoute(string? route, string toolName)
    {
        if (string.IsNullOrWhiteSpace(route) || !route.Trim().StartsWith("/", StringComparison.Ordinal))
            throw new ArgumentException($"WebMCP tool '{toolName}' route must be a root-relative site route.");
        if (route.Contains('?', StringComparison.Ordinal) || route.Contains('#', StringComparison.Ordinal))
            throw new ArgumentException($"WebMCP tool '{toolName}' route cannot contain a query string or fragment.");
        if (Uri.TryCreate(route, UriKind.Absolute, out _))
            throw new ArgumentException($"WebMCP tool '{toolName}' route must not be an absolute URL.");

        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(route);
        }
        catch (UriFormatException ex)
        {
            throw new ArgumentException($"WebMCP tool '{toolName}' route contains invalid percent encoding.", ex);
        }

        if (decoded.Contains('\\', StringComparison.Ordinal) ||
            decoded.Split('/', StringSplitOptions.RemoveEmptyEntries).Any(static segment => segment is "." or ".."))
        {
            throw new ArgumentException($"WebMCP tool '{toolName}' route must stay within the site root.");
        }
    }

    private static void AddLocalWebMcpCheck(List<WebAgentReadinessCheck> checks, string siteRoot, AgentReadinessSpec spec)
    {
        var evaluation = EvaluateLocalWebMcp(siteRoot, spec);
        AddCheck(
            checks,
            "webmcp",
            "api-auth-mcp-skill-discovery",
            "WebMCP",
            evaluation.Success ? "pass" : "fail",
            evaluation.Message,
            evaluation.Target);
    }

    private static WebMcpEvaluation EvaluateLocalWebMcp(string siteRoot, AgentReadinessSpec spec)
    {
        if (!spec.WebMcp)
            return new WebMcpEvaluation(false, "WebMCP is disabled.", siteRoot);
        if (spec.WebMcpTools is null || spec.WebMcpTools.Length == 0)
            return new WebMcpEvaluation(false, "WebMCP is enabled but no route-scoped tools are configured.", siteRoot);

        foreach (var tool in spec.WebMcpTools)
        {
            var route = NormalizeRoute(tool.Route);
            var htmlPath = ResolveWebMcpHtmlPath(siteRoot, route);
            if (!File.Exists(htmlPath))
                return new WebMcpEvaluation(false, $"WebMCP tool '{tool.Name}' route '{route}' was not rendered.", htmlPath);

            var html = File.ReadAllText(htmlPath);
            if (!TryParseWebMcpPage(html, tool, route, out var scripts, out var parseMessage))
                return new WebMcpEvaluation(false, $"WebMCP tool '{tool.Name}' at '{route}' is not ready: {parseMessage}", htmlPath);

            if (!ScriptsContainCanonicalSiteSearchRuntime(siteRoot, route, scripts))
            {
                return new WebMcpEvaluation(
                    false,
                    $"WebMCP tool '{tool.Name}' at '{route}' does not load the canonical PowerForge site-search runtime from the same origin.",
                    htmlPath);
            }
        }

        var routes = string.Join(", ", spec.WebMcpTools.Select(static tool => $"{tool.Name} at {NormalizeRoute(tool.Route)}"));
        return new WebMcpEvaluation(true, $"Current imperative WebMCP registration is present for {routes}.", siteRoot);
    }

    private static async Task AddRemoteWebMcpCheckAsync(
        List<WebAgentReadinessCheck> checks,
        HttpClient http,
        string baseUrl,
        AgentReadinessSpec spec,
        HttpTextResult homepage,
        CancellationToken cancellationToken)
    {
        if (spec.WebMcpTools is null || spec.WebMcpTools.Length == 0)
        {
            AddCheck(checks, "webmcp", "api-auth-mcp-skill-discovery", "WebMCP", "fail",
                "WebMCP is enabled but no route-scoped tools are configured.", baseUrl);
            return;
        }

        foreach (var tool in spec.WebMcpTools)
        {
            var route = NormalizeRoute(tool.Route);
            var routeUrl = CombineUrl(baseUrl, route);
            var page = route == "/"
                ? homepage
                : await TryGetTextAsync(http, routeUrl, null, cancellationToken).ConfigureAwait(false);
            if (!page.Success)
            {
                AddCheck(checks, "webmcp", "api-auth-mcp-skill-discovery", "WebMCP", "fail",
                    $"WebMCP tool '{tool.Name}' route '{route}' could not be inspected: {page.Message}", routeUrl);
                return;
            }

            var requestedPageUri = new Uri(routeUrl, UriKind.Absolute);
            var finalPageUri = page.Response?.RequestMessage?.RequestUri;
            if (finalPageUri is null || !HasSameOrigin(requestedPageUri, finalPageUri))
            {
                AddCheck(checks, "webmcp", "api-auth-mcp-skill-discovery", "WebMCP", "fail",
                    $"WebMCP tool '{tool.Name}' route '{route}' redirected outside its configured origin.", routeUrl);
                return;
            }

            if (!TryParseWebMcpPage(page.Text, tool, route, out var scripts, out var parseMessage))
            {
                AddCheck(checks, "webmcp", "api-auth-mcp-skill-discovery", "WebMCP", "fail",
                    $"WebMCP tool '{tool.Name}' at '{route}' is not ready: {parseMessage}", routeUrl);
                return;
            }

            if (!await RemoteScriptsContainCanonicalSiteSearchRuntimeAsync(http, routeUrl, scripts, cancellationToken).ConfigureAwait(false))
            {
                AddCheck(checks, "webmcp", "api-auth-mcp-skill-discovery", "WebMCP", "fail",
                    $"WebMCP tool '{tool.Name}' at '{route}' does not load the canonical PowerForge site-search runtime from the same origin.", routeUrl);
                return;
            }
        }

        var configured = string.Join(", ", spec.WebMcpTools.Select(static tool => $"{tool.Name} at {NormalizeRoute(tool.Route)}"));
        AddCheck(checks, "webmcp", "api-auth-mcp-skill-discovery", "WebMCP", "pass",
            $"Current imperative WebMCP registration is present for {configured}.", baseUrl);
    }

    private static bool TryParseWebMcpPage(
        string html,
        AgentWebMcpToolSpec tool,
        string route,
        out IElement[] markedScripts,
        out string message)
    {
        markedScripts = Array.Empty<IElement>();
        if (string.IsNullOrWhiteSpace(html))
        {
            message = "the route returned empty HTML.";
            return false;
        }

        try
        {
            var document = HtmlParser.ParseWithAngleSharp(html);
            var surface = document.QuerySelector("[data-webmcp-site-search]");
            if (surface is null)
            {
                message = "no data-webmcp-site-search surface is present.";
                return false;
            }
            if (!string.Equals(surface.GetAttribute("data-webmcp-tool-name"), tool.Name, StringComparison.Ordinal))
            {
                message = $"the active site-search surface does not declare '{tool.Name}'.";
                return false;
            }
            if (string.IsNullOrWhiteSpace(surface.GetAttribute("data-webmcp-tool-description")))
            {
                message = $"the active site-search surface for '{tool.Name}' has no description.";
                return false;
            }

            var indexPath = surface.GetAttribute("data-webmcp-search-index");
            var pageUri = new Uri(new Uri("https://powerforge.invalid"), route);
            if (string.IsNullOrWhiteSpace(indexPath) ||
                !Uri.TryCreate(pageUri, indexPath, out var indexUri) ||
                !HasSameOrigin(pageUri, indexUri))
            {
                message = $"the active site-search surface for '{tool.Name}' has no same-origin search index.";
                return false;
            }

            markedScripts = document.QuerySelectorAll("script[data-powerforge-webmcp]").ToArray();
            if (markedScripts.Length == 0)
            {
                message = "no script is explicitly marked with data-powerforge-webmcp.";
                return false;
            }

            message = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            message = $"the route HTML could not be parsed ({ex.GetType().Name}).";
            return false;
        }
    }

    private static bool ScriptsContainCanonicalSiteSearchRuntime(string siteRoot, string route, IEnumerable<IElement> scripts)
    {
        var pageUri = new Uri(new Uri("https://powerforge.invalid"), route);
        var canonicalRuntime = WebSiteBuilder.GetWebMcpSiteSearchAssetContent();
        foreach (var script in scripts)
        {
            var source = script.GetAttribute("src");
            if (string.IsNullOrWhiteSpace(source))
                continue;

            if (!Uri.TryCreate(pageUri, source, out var assetUri) ||
                !HasSameOrigin(pageUri, assetUri))
            {
                continue;
            }

            var assetPath = ResolveSitePath(siteRoot, assetUri.AbsolutePath);
            if (File.Exists(assetPath) && string.Equals(File.ReadAllText(assetPath), canonicalRuntime, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static async Task<bool> RemoteScriptsContainCanonicalSiteSearchRuntimeAsync(
        HttpClient http,
        string routeUrl,
        IEnumerable<IElement> scripts,
        CancellationToken cancellationToken)
    {
        var pageUri = new Uri(routeUrl, UriKind.Absolute);
        var canonicalRuntime = WebSiteBuilder.GetWebMcpSiteSearchAssetContent();
        foreach (var script in scripts)
        {
            var source = script.GetAttribute("src");
            if (string.IsNullOrWhiteSpace(source))
                continue;

            if (!Uri.TryCreate(pageUri, source, out var assetUri) || !HasSameOrigin(pageUri, assetUri))
                continue;

            var asset = await TryGetTextAsync(http, assetUri.AbsoluteUri, null, cancellationToken).ConfigureAwait(false);
            var finalAssetUri = asset.Response?.RequestMessage?.RequestUri;
            if (asset.Success &&
                finalAssetUri is not null &&
                HasSameOrigin(pageUri, finalAssetUri) &&
                string.Equals(asset.Text, canonicalRuntime, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasSameOrigin(Uri left, Uri right) =>
        string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase) &&
        left.Port == right.Port;

    private static string ResolveWebMcpHtmlPath(string siteRoot, string route)
    {
        var path = route.TrimStart('/');
        if (string.IsNullOrWhiteSpace(path))
            path = "index.html";
        else if (route.EndsWith("/", StringComparison.Ordinal))
            path += "index.html";
        else if (!path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) &&
                 !path.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
            path += "/index.html";

        return ResolveSitePath(siteRoot, path);
    }

    private sealed record WebMcpEvaluation(bool Success, string Message, string Target);
}
