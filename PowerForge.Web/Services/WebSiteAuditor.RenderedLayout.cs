using System.Text.Json;
using HtmlTinkerX;

namespace PowerForge.Web;

public static partial class WebSiteAuditor
{
    private static readonly Lazy<string> RenderedLayoutScript = new(() =>
    {
        using var stream = typeof(WebSiteAuditor).Assembly.GetManifestResourceStream("PowerForge.Web.Assets.Audit.layout.js")
            ?? throw new InvalidOperationException("Rendered layout audit asset is missing.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    });

    private static IReadOnlyList<string> FindRenderedLayoutIssues(string url, HtmlBrowserEngine engine, WebAuditOptions options)
    {
        var viewports = options.RenderedViewports.Length == 0 ? new[]
        {
            new WebAuditViewport(), new WebAuditViewport { Width = 390, Height = 844 }
        } : options.RenderedViewports;
        if (viewports.Length > 8) throw new ArgumentException("At most eight rendered viewports are supported.");
        var findings = new List<string>();
        foreach (var viewport in viewports)
        {
            if (viewport.Width <= 0 || viewport.Height <= 0) throw new ArgumentException("Rendered viewports must have positive dimensions.");
            HtmlBrowserSession? session = null;
            try
            {
                session = HtmlBrowser.OpenSessionAsync(url, new HtmlBrowserLaunchOptions
                {
                    Browser = engine, Headless = options.RenderedHeadless,
                    ViewportWidth = viewport.Width, ViewportHeight = viewport.Height,
                    Timeout = options.RenderedTimeoutMs
                }).GetAwaiter().GetResult();
                var script = RenderedLayoutScript.Value + "(" + JsonSerializer.Serialize(options.RenderedLayoutSelectors) + ")";
                var json = HtmlBrowser.EvaluateAsync<string>(session, script).GetAwaiter().GetResult();
                using var result = JsonDocument.Parse(json ?? throw new InvalidOperationException("Rendered layout audit returned no result."));
                foreach (var item in result.RootElement.EnumerateArray())
                    findings.Add($"{viewport.Width}x{viewport.Height}: {item.GetProperty("selector").GetString()} bounds " +
                        $"{item.GetProperty("left")}-{item.GetProperty("right")} exceed the viewport ({item.GetProperty("text").GetString()}).");
            }
            finally
            {
                if (session is not null) HtmlBrowser.CloseSessionAsync(session).GetAwaiter().GetResult();
            }
        }
        return findings;
    }
}
