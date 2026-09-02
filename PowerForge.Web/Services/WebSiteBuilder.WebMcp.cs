namespace PowerForge.Web;

public static partial class WebSiteBuilder
{
    internal const string WebMcpSiteSearchAssetRoute = "/assets/powerforge/webmcp-site-search.v1.js";
    private const string WebMcpSiteSearchResourceName = "PowerForge.Web.Assets.WebMcp.site-search.v1.js";
    private static readonly Lazy<string> WebMcpSiteSearchAssetContent = new(ReadWebMcpSiteSearchAsset);

    private static AgentWebMcpToolSpec[] ResolveWebMcpSiteSearchTools(SiteSpec spec)
    {
        var readiness = spec.AgentReadiness;
        if (readiness?.Enabled != true || !readiness.WebMcp)
            return Array.Empty<AgentWebMcpToolSpec>();

        WebAgentReadiness.ValidateWebMcpConfiguration(readiness);
        return (readiness.WebMcpTools ?? Array.Empty<AgentWebMcpToolSpec>())
            .Where(static tool => string.Equals(tool.Kind.Trim(), "site-search", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    internal static string EnsureWebMcpSiteSearchAsset(string outputRoot)
    {
        var target = GetWebMcpSiteSearchAssetPath(outputRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        WriteAllTextIfChanged(target, GetWebMcpSiteSearchAssetContent());
        return target;
    }

    private static string GetWebMcpSiteSearchAssetPath(string outputRoot) =>
        Path.Combine(outputRoot, WebMcpSiteSearchAssetRoute.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

    internal static string GetWebMcpSiteSearchAssetContent() => WebMcpSiteSearchAssetContent.Value;

    private static string ReadWebMcpSiteSearchAsset()
    {
        var assembly = typeof(WebSiteBuilder).Assembly;
        using var stream = assembly.GetManifestResourceStream(WebMcpSiteSearchResourceName)
            ?? throw new InvalidOperationException($"Embedded WebMCP runtime '{WebMcpSiteSearchResourceName}' was not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
