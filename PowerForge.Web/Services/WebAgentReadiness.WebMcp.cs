using System.IO.Compression;
using System.Text;
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

        var seenTools = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenSiteSearchRoutes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
            var kind = tool.Kind.Trim();
            if (!string.Equals(kind, "site-search", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(kind, "page-tool", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"WebMCP tool '{tool.Name}' uses unsupported implementation kind '{tool.Kind}'. PowerForge supports 'site-search' and 'page-tool'.");
            }
            if (string.Equals(kind, "site-search", StringComparison.OrdinalIgnoreCase) && !tool.ReadOnly)
                throw new ArgumentException($"WebMCP tool '{tool.Name}' uses the read-only 'site-search' implementation and cannot be declared writable.");

            ValidateWebMcpRoute(tool.Route, toolName);
            var normalizedRoute = NormalizeWebMcpDocumentRoute(NormalizeWebMcpRoute(tool.Route));
            if (!seenTools.Add($"{normalizedRoute}\n{toolName}"))
                throw new ArgumentException($"WebMCP tool '{tool.Name}' is configured more than once for route '{normalizedRoute}'.");
            if (string.Equals(kind, "site-search", StringComparison.OrdinalIgnoreCase) &&
                !seenSiteSearchRoutes.Add(normalizedRoute))
            {
                throw new ArgumentException($"Only one WebMCP site-search tool can be configured for route '{normalizedRoute}'.");
            }
        }
    }

    private static void ValidateWebMcpRoute(string? route, string toolName)
    {
        var trimmedRoute = route?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedRoute) ||
            !trimmedRoute.StartsWith("/", StringComparison.Ordinal) ||
            trimmedRoute.StartsWith("//", StringComparison.Ordinal))
            throw new ArgumentException($"WebMCP tool '{toolName}' route must be a root-relative site route.");
        if (trimmedRoute.Contains('?', StringComparison.Ordinal) || trimmedRoute.Contains('#', StringComparison.Ordinal))
            throw new ArgumentException($"WebMCP tool '{toolName}' route cannot contain a query string or fragment.");

        string decoded;
        try
        {
            decoded = Uri.UnescapeDataString(trimmedRoute);
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

    private static void AddLocalWebMcpCheck(
        List<WebAgentReadinessCheck> checks,
        string siteRoot,
        string? baseUrl,
        AgentReadinessSpec spec)
    {
        var evaluation = EvaluateLocalWebMcp(siteRoot, baseUrl, spec);
        AddCheck(
            checks,
            "webmcp",
            "api-auth-mcp-skill-discovery",
            "WebMCP",
            evaluation.Success ? "pass" : "fail",
            evaluation.Message,
            evaluation.Target);
    }

    private static WebMcpEvaluation EvaluateLocalWebMcp(string siteRoot, string? baseUrl, AgentReadinessSpec spec)
    {
        if (!spec.WebMcp)
            return new WebMcpEvaluation(false, "WebMCP is disabled.", siteRoot);
        if (spec.WebMcpTools is null || spec.WebMcpTools.Length == 0)
            return new WebMcpEvaluation(false, "WebMCP is enabled but no route-scoped tools are configured.", siteRoot);

        var siteBaseUri = ResolveLocalWebMcpSiteBaseUri(baseUrl);
        foreach (var tool in spec.WebMcpTools)
        {
            var route = NormalizeWebMcpRoute(tool.Route);
            var htmlPath = ResolveWebMcpHtmlPath(siteRoot, route);
            if (!File.Exists(htmlPath))
                return new WebMcpEvaluation(false, $"WebMCP tool '{tool.Name}' route '{route}' was not rendered.", htmlPath);

            var html = File.ReadAllText(htmlPath);
            var pageUri = new Uri(siteBaseUri, NormalizeWebMcpDocumentRoute(route).TrimStart('/'));
            if (IsPageTool(tool))
            {
                if (!TryParsePageWebMcpTool(html, tool, pageUri, out var pageScripts, out var pageDocumentBaseUri, out var pageParseMessage))
                    return new WebMcpEvaluation(false, $"WebMCP tool '{tool.Name}' at '{route}' is not ready: {pageParseMessage}", htmlPath);
                if (!ScriptsResolveInsideRenderedSite(siteRoot, siteBaseUri, pageUri, pageDocumentBaseUri, pageScripts))
                {
                    return new WebMcpEvaluation(
                        false,
                        $"WebMCP page tool '{tool.Name}' at '{route}' does not load a marked product adapter from the same rendered site.",
                        htmlPath);
                }
                continue;
            }

            if (!TryParseWebMcpPage(html, tool, pageUri, out var scripts, out var documentBaseUri, out var indexUri, out var parseMessage))
                return new WebMcpEvaluation(false, $"WebMCP tool '{tool.Name}' at '{route}' is not ready: {parseMessage}", htmlPath);

            var indexMessage = "the referenced resource is outside the rendered site.";
            var indexPathResolved = TryResolveLocalWebMcpResourcePath(siteRoot, siteBaseUri, indexUri, out var indexPath);
            if (!indexPathResolved || !TryValidateLocalSearchIndex(indexPath, out indexMessage))
            {
                return new WebMcpEvaluation(
                    false,
                    $"WebMCP tool '{tool.Name}' at '{route}' does not reference a usable JSON-array search index inside the rendered site: {indexMessage}",
                    indexPath ?? htmlPath);
            }

            if (!ScriptsContainCanonicalSiteSearchRuntime(siteRoot, siteBaseUri, pageUri, documentBaseUri, scripts))
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
            var route = NormalizeWebMcpRoute(tool.Route);
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

            if (IsPageTool(tool))
            {
                if (!TryParsePageWebMcpTool(page.Text, tool, finalPageUri, out var pageScripts, out var pageDocumentBaseUri, out var pageParseMessage))
                {
                    AddCheck(checks, "webmcp", "api-auth-mcp-skill-discovery", "WebMCP", "fail",
                        $"WebMCP tool '{tool.Name}' at '{route}' is not ready: {pageParseMessage}", routeUrl);
                    return;
                }
                if (!await RemoteScriptsResolveSameOriginAsync(http, finalPageUri, pageDocumentBaseUri, pageScripts, cancellationToken).ConfigureAwait(false))
                {
                    AddCheck(checks, "webmcp", "api-auth-mcp-skill-discovery", "WebMCP", "fail",
                        $"WebMCP page tool '{tool.Name}' at '{route}' does not load a marked product adapter from the same origin.", routeUrl);
                    return;
                }
                continue;
            }

            if (!TryParseWebMcpPage(page.Text, tool, finalPageUri, out var scripts, out var documentBaseUri, out var indexUri, out var parseMessage))
            {
                AddCheck(checks, "webmcp", "api-auth-mcp-skill-discovery", "WebMCP", "fail",
                    $"WebMCP tool '{tool.Name}' at '{route}' is not ready: {parseMessage}", routeUrl);
                return;
            }

            var index = await TryGetBoundedSearchIndexAsync(http, indexUri.AbsoluteUri, cancellationToken).ConfigureAwait(false);
            var finalIndexUri = index.FinalUri;
            var entryCount = 0;
            var decodedBytes = 0;
            var indexMessage = index.Message;
            var validIndex = index.Success && WebSearchIndexPolicy.TryValidateJsonArray(
                index.Text,
                out entryCount,
                out decodedBytes,
                out indexMessage);
            if (!index.Success ||
                finalIndexUri is null ||
                !HasSameOrigin(finalPageUri, finalIndexUri) ||
                !validIndex)
            {
                AddCheck(checks, "webmcp", "api-auth-mcp-skill-discovery", "WebMCP", "fail",
                    $"WebMCP tool '{tool.Name}' at '{route}' does not reference a usable same-origin search index: {index.Message}; {indexMessage}", indexUri.AbsoluteUri);
                return;
            }

            var compressed = index.HasContentEncoding;
            if (decodedBytes >= WebSearchIndexPolicy.CompressionRecommendationBytes && !compressed)
            {
                AddCheck(
                    checks,
                    $"webmcp-index-compression-{tool.Name}",
                    "performance",
                    $"WebMCP search index compression ({tool.Name})",
                    "warn",
                    $"The {decodedBytes}-byte decoded search index is delivered without Content-Encoding. Enable Brotli or gzip for JSON responses.",
                    indexUri.AbsoluteUri);
            }
            else
            {
                AddCheck(
                    checks,
                    $"webmcp-index-delivery-{tool.Name}",
                    "performance",
                    $"WebMCP search index delivery ({tool.Name})",
                    "pass",
                    $"The search index contains {entryCount} entries and {decodedBytes} decoded bytes{(compressed ? " with HTTP compression" : string.Empty)}.",
                    indexUri.AbsoluteUri);
            }

            if (!await RemoteScriptsContainCanonicalSiteSearchRuntimeAsync(http, finalPageUri, documentBaseUri, scripts, cancellationToken).ConfigureAwait(false))
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

    private static bool IsPageTool(AgentWebMcpToolSpec tool) =>
        string.Equals(tool.Kind.Trim(), "page-tool", StringComparison.OrdinalIgnoreCase);

    private static bool TryParsePageWebMcpTool(
        string html,
        AgentWebMcpToolSpec tool,
        Uri pageUri,
        out IElement[] markedScripts,
        out Uri documentBaseUri,
        out string message)
    {
        markedScripts = Array.Empty<IElement>();
        documentBaseUri = pageUri;
        if (string.IsNullOrWhiteSpace(html))
        {
            message = "the route returned empty HTML.";
            return false;
        }

        try
        {
            var document = HtmlParser.ParseWithAngleSharp(html);
            documentBaseUri = ResolveWebMcpDocumentBase(document, pageUri);
            var surfaces = document.QuerySelectorAll("[data-webmcp-page-tool]")
                .Where(surface => string.Equals(surface.GetAttribute("data-webmcp-tool-name"), tool.Name, StringComparison.Ordinal))
                .ToArray();
            if (surfaces.Length != 1)
            {
                message = $"the route must contain exactly one data-webmcp-page-tool surface for '{tool.Name}'.";
                return false;
            }

            var surface = surfaces[0];
            if (!string.Equals(surface.GetAttribute("data-webmcp-tool-description"), tool.Description, StringComparison.Ordinal))
            {
                message = $"the page-tool surface for '{tool.Name}' does not declare its configured description.";
                return false;
            }
            var expectedReadOnly = tool.ReadOnly ? "true" : "false";
            if (!string.Equals(surface.GetAttribute("data-webmcp-read-only"), expectedReadOnly, StringComparison.OrdinalIgnoreCase))
            {
                message = $"the page-tool surface for '{tool.Name}' does not declare data-webmcp-read-only='{expectedReadOnly}'.";
                return false;
            }

            var markedCandidates = document.QuerySelectorAll("script[data-powerforge-webmcp]")
                .Where(script => string.Equals(script.GetAttribute("data-webmcp-tool-name"), tool.Name, StringComparison.Ordinal))
                .ToArray();
            var documentElements = document.All.ToArray();
            var surfaceIndex = Array.IndexOf(documentElements, surface);
            markedScripts = markedCandidates
                .Where(script => CanExecuteAfterSurface(script, documentElements, surfaceIndex))
                .ToArray();
            if (markedScripts.Length == 0)
            {
                message = $"no product adapter marked for WebMCP tool '{tool.Name}' can execute after its page-tool surface exists.";
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

    private static bool TryParseWebMcpPage(
        string html,
        AgentWebMcpToolSpec tool,
        Uri pageUri,
        out IElement[] markedScripts,
        out Uri documentBaseUri,
        out Uri indexUri,
        out string message)
    {
        markedScripts = Array.Empty<IElement>();
        documentBaseUri = pageUri;
        indexUri = null!;
        if (string.IsNullOrWhiteSpace(html))
        {
            message = "the route returned empty HTML.";
            return false;
        }

        try
        {
            var document = HtmlParser.ParseWithAngleSharp(html);
            documentBaseUri = ResolveWebMcpDocumentBase(document, pageUri);
            var surfaces = document.QuerySelectorAll("[data-webmcp-site-search]").ToArray();
            if (surfaces.Length == 0)
            {
                message = "no data-webmcp-site-search surface is present.";
                return false;
            }
            if (surfaces.Length != 1)
            {
                message = "the route must contain exactly one active data-webmcp-site-search surface.";
                return false;
            }
            var surface = surfaces[0];
            if (!string.Equals(surface.GetAttribute("data-webmcp-tool-name"), tool.Name, StringComparison.Ordinal))
            {
                message = $"the active site-search surface does not declare '{tool.Name}'.";
                return false;
            }
            if (!string.Equals(surface.GetAttribute("data-webmcp-tool-description"), tool.Description, StringComparison.Ordinal))
            {
                message = $"the active site-search surface for '{tool.Name}' does not declare its configured description.";
                return false;
            }

            var indexPath = surface.GetAttribute("data-webmcp-search-index");
            if (string.IsNullOrWhiteSpace(indexPath) ||
                !Uri.TryCreate(documentBaseUri, indexPath, out var resolvedIndexUri) ||
                resolvedIndexUri is null ||
                !HasSameOrigin(pageUri, resolvedIndexUri))
            {
                message = $"the active site-search surface for '{tool.Name}' has no same-origin search index.";
                return false;
            }
            indexUri = resolvedIndexUri;

            var markedCandidates = document.QuerySelectorAll("script[data-powerforge-webmcp]").ToArray();
            if (markedCandidates.Length == 0)
            {
                message = "no script is explicitly marked with data-powerforge-webmcp.";
                return false;
            }

            var documentElements = document.All.ToArray();
            var surfaceIndex = Array.IndexOf(documentElements, surface);
            markedScripts = markedCandidates
                .Where(script => CanExecuteAfterSurface(script, documentElements, surfaceIndex))
                .ToArray();
            if (markedScripts.Length == 0)
            {
                message = "the marked WebMCP runtime can execute before its site-search surface exists; load it after the surface or use defer/module semantics.";
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

    private static bool CanExecuteAfterSurface(IElement script, IElement[] documentElements, int surfaceIndex)
    {
        if (!IsExecutableWebMcpScript(script))
            return false;

        var scriptIndex = Array.IndexOf(documentElements, script);
        if (surfaceIndex >= 0 && scriptIndex > surfaceIndex)
            return true;

        if (script.HasAttribute("async"))
            return false;

        var type = script.GetAttribute("type")?.Trim();
        return script.HasAttribute("defer") || string.Equals(type, "module", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExecutableWebMcpScript(IElement script)
    {
        if (script.HasAttribute("nomodule") ||
            !string.IsNullOrWhiteSpace(script.GetAttribute("integrity")))
            return false;

        var type = script.GetAttribute("type")?.Trim();
        if (string.IsNullOrEmpty(type))
            return true;
        if (string.Equals(type, "module", StringComparison.OrdinalIgnoreCase))
            return true;

        var separator = type.IndexOf(';');
        var mimeType = (separator >= 0 ? type[..separator] : type).Trim();
        return mimeType.ToLowerInvariant() switch
        {
            "text/javascript" or
            "application/javascript" or
            "text/ecmascript" or
            "application/ecmascript" or
            "application/x-javascript" => true,
            _ => false
        };
    }

    private static Uri ResolveWebMcpDocumentBase(IDocument document, Uri pageUri)
    {
        foreach (var element in document.QuerySelectorAll("base[href]"))
        {
            var href = element.GetAttribute("href");
            if (!string.IsNullOrWhiteSpace(href) && Uri.TryCreate(pageUri, href, out var resolved) && resolved is not null)
                return resolved;
        }

        return pageUri;
    }

    private static bool ScriptsContainCanonicalSiteSearchRuntime(
        string siteRoot,
        Uri siteBaseUri,
        Uri pageUri,
        Uri documentBaseUri,
        IEnumerable<IElement> scripts)
    {
        var canonicalRuntime = WebSiteBuilder.GetWebMcpSiteSearchAssetContent();
        foreach (var script in scripts)
        {
            var source = script.GetAttribute("src");
            if (string.IsNullOrWhiteSpace(source))
                continue;

            if (!Uri.TryCreate(documentBaseUri, source, out var assetUri) ||
                !HasSameOrigin(pageUri, assetUri))
            {
                continue;
            }

            if (TryResolveLocalWebMcpResourcePath(siteRoot, siteBaseUri, assetUri, out var assetPath) &&
                File.Exists(assetPath) &&
                MatchesCanonicalWebMcpRuntime(File.ReadAllText(assetPath), canonicalRuntime))
                return true;
        }

        return false;
    }

    private static async Task<bool> RemoteScriptsContainCanonicalSiteSearchRuntimeAsync(
        HttpClient http,
        Uri pageUri,
        Uri documentBaseUri,
        IEnumerable<IElement> scripts,
        CancellationToken cancellationToken)
    {
        var canonicalRuntime = WebSiteBuilder.GetWebMcpSiteSearchAssetContent();
        foreach (var script in scripts)
        {
            var source = script.GetAttribute("src");
            if (string.IsNullOrWhiteSpace(source))
                continue;

            if (!Uri.TryCreate(documentBaseUri, source, out var assetUri) || !HasSameOrigin(pageUri, assetUri))
                continue;

            var asset = await TryGetTextAsync(http, assetUri.AbsoluteUri, null, cancellationToken).ConfigureAwait(false);
            var finalAssetUri = asset.Response?.RequestMessage?.RequestUri;
            if (asset.Success &&
                finalAssetUri is not null &&
                HasSameOrigin(pageUri, finalAssetUri) &&
                MatchesCanonicalWebMcpRuntime(asset.Text, canonicalRuntime))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesCanonicalWebMcpRuntime(string candidate, string canonical) =>
        string.Equals(
            candidate.Replace("\r\n", "\n", StringComparison.Ordinal),
            canonical.Replace("\r\n", "\n", StringComparison.Ordinal),
            StringComparison.Ordinal);

    private static bool ScriptsResolveInsideRenderedSite(
        string siteRoot,
        Uri siteBaseUri,
        Uri pageUri,
        Uri documentBaseUri,
        IEnumerable<IElement> scripts)
    {
        foreach (var script in scripts)
        {
            var source = script.GetAttribute("src");
            if (string.IsNullOrWhiteSpace(source) ||
                !Uri.TryCreate(documentBaseUri, source, out var assetUri) ||
                !HasSameOrigin(pageUri, assetUri))
            {
                continue;
            }

            if (TryResolveLocalWebMcpResourcePath(siteRoot, siteBaseUri, assetUri, out var assetPath) &&
                File.Exists(assetPath) &&
                new FileInfo(assetPath).Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static async Task<bool> RemoteScriptsResolveSameOriginAsync(
        HttpClient http,
        Uri pageUri,
        Uri documentBaseUri,
        IEnumerable<IElement> scripts,
        CancellationToken cancellationToken)
    {
        foreach (var script in scripts)
        {
            var source = script.GetAttribute("src");
            if (string.IsNullOrWhiteSpace(source) ||
                !Uri.TryCreate(documentBaseUri, source, out var assetUri) ||
                !HasSameOrigin(pageUri, assetUri))
            {
                continue;
            }

            var asset = await TryGetTextAsync(http, assetUri.AbsoluteUri, null, cancellationToken).ConfigureAwait(false);
            var finalAssetUri = asset.Response?.RequestMessage?.RequestUri;
            if (asset.Success &&
                finalAssetUri is not null &&
                HasSameOrigin(pageUri, finalAssetUri) &&
                !string.IsNullOrWhiteSpace(asset.Text))
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

    internal static string NormalizeWebMcpRoute(string? route) => NormalizeRoute(route ?? string.Empty);

    private static string NormalizeWebMcpDocumentRoute(string route)
    {
        const string indexDocument = "/index.html";
        if (route.EndsWith(indexDocument, StringComparison.OrdinalIgnoreCase))
            return route[..^"index.html".Length];

        if (route.EndsWith("/", StringComparison.Ordinal) ||
            route.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
            route.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
        {
            return route;
        }

        return route + "/";
    }

    internal static string ResolveWebMcpHtmlPath(string siteRoot, string route)
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

    private static Uri ResolveLocalWebMcpSiteBaseUri(string? baseUrl)
    {
        var normalized = NormalizeBaseUrl(baseUrl);
        if (!string.IsNullOrWhiteSpace(normalized) &&
            TryCreateAbsoluteHttpUri(normalized + "/", out var configured))
        {
            return configured;
        }

        return new Uri("https://powerforge.invalid/", UriKind.Absolute);
    }

    private static bool TryResolveLocalWebMcpResourcePath(
        string siteRoot,
        Uri siteBaseUri,
        Uri resourceUri,
        out string? resourcePath)
    {
        resourcePath = null;
        if (!HasSameOrigin(siteBaseUri, resourceUri))
            return false;

        var basePath = siteBaseUri.AbsolutePath;
        if (!basePath.EndsWith("/", StringComparison.Ordinal))
            basePath += "/";
        var absolutePath = resourceUri.AbsolutePath;
        if (basePath != "/" && !absolutePath.StartsWith(basePath, StringComparison.Ordinal))
            return false;

        var relativePath = basePath == "/"
            ? absolutePath.TrimStart('/')
            : absolutePath[basePath.Length..];
        resourcePath = ResolveSitePath(siteRoot, relativePath);
        return true;
    }

    private static bool TryValidateLocalSearchIndex(string? path, out string message)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            message = "the referenced file does not exist.";
            return false;
        }

        var length = new FileInfo(path).Length;
        if (length > WebSearchIndexPolicy.MaximumDecodedBytes)
        {
            message = $"the search index is {length} bytes; the WebMCP limit is {WebSearchIndexPolicy.MaximumDecodedBytes} bytes.";
            return false;
        }

        try
        {
            return WebSearchIndexPolicy.TryValidateJsonArray(
                File.ReadAllText(path),
                out _,
                out _,
                out message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            message = $"the search index could not be read ({ex.GetType().Name}).";
            return false;
        }
    }

    private static async Task<WebMcpIndexResult> TryGetBoundedSearchIndexAsync(
        HttpClient http,
        string url,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.AcceptEncoding.ParseAdd("br, gzip, deflate");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            var finalUri = response.RequestMessage?.RequestUri;
            var hasContentEncoding = response.Content.Headers.ContentEncoding.Count > 0;
            if (!response.IsSuccessStatusCode)
                return new WebMcpIndexResult(false, $"HTTP {(int)response.StatusCode}", string.Empty, finalUri, hasContentEncoding);

            if (response.Content.Headers.ContentLength is > WebSearchIndexPolicy.MaximumDecodedBytes &&
                response.Content.Headers.ContentEncoding.Count == 0)
            {
                return new WebMcpIndexResult(
                    false,
                    $"the response Content-Length exceeds {WebSearchIndexPolicy.MaximumDecodedBytes} bytes",
                    string.Empty,
                    finalUri,
                    hasContentEncoding);
            }

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var decoded = WrapSearchIndexStream(source, response.Content.Headers.ContentEncoding);
            using var buffer = new MemoryStream();
            var chunk = new byte[81_920];
            while (true)
            {
                var read = await decoded.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken).ConfigureAwait(false);
                if (read == 0)
                    break;
                if (buffer.Length + read > WebSearchIndexPolicy.MaximumDecodedBytes)
                {
                    return new WebMcpIndexResult(
                        false,
                        $"the decoded response exceeds {WebSearchIndexPolicy.MaximumDecodedBytes} bytes",
                        string.Empty,
                        finalUri,
                        hasContentEncoding);
                }
                buffer.Write(chunk, 0, read);
            }

            var text = new UTF8Encoding(false, true).GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
            return new WebMcpIndexResult(true, $"HTTP {(int)response.StatusCode}", text, finalUri, hasContentEncoding);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new WebMcpIndexResult(false, ex.Message, string.Empty, null, false);
        }
    }

    private static Stream WrapSearchIndexStream(Stream source, ICollection<string> contentEncodings)
    {
        if (contentEncodings.Count == 0)
            return source;
        if (contentEncodings.Count != 1)
            throw new InvalidDataException("Search index responses with multiple Content-Encoding values are not supported.");

        return contentEncodings.Single().Trim().ToLowerInvariant() switch
        {
            "br" => new BrotliStream(source, CompressionMode.Decompress, leaveOpen: false),
            "gzip" => new GZipStream(source, CompressionMode.Decompress, leaveOpen: false),
            "deflate" => new DeflateStream(source, CompressionMode.Decompress, leaveOpen: false),
            var encoding => throw new InvalidDataException($"Unsupported search index Content-Encoding '{encoding}'.")
        };
    }

    private sealed record WebMcpEvaluation(bool Success, string Message, string Target);
    private sealed record WebMcpIndexResult(
        bool Success,
        string Message,
        string Text,
        Uri? FinalUri,
        bool HasContentEncoding);
}
