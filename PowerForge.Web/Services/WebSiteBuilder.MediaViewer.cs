namespace PowerForge.Web;

public static partial class WebSiteBuilder
{
    private const string MediaViewerCssRoute = "/assets/powerforge/media-viewer.v1.css";
    private const string MediaViewerJsRoute = "/assets/powerforge/media-viewer.v1.js";

    private static void EnsureMediaViewerAssets(SiteSpec spec, string outputRoot)
    {
        if (spec.MediaViewer?.Enabled != true) return;
        foreach (var route in new[] { MediaViewerCssRoute, MediaViewerJsRoute })
        {
            var path = Path.Combine(outputRoot, route.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
            var resource = "PowerForge.Web.Assets.Media." + Path.GetFileName(route);
            using var stream = typeof(WebSiteBuilder).Assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Missing media viewer resource: {resource}");
            using var reader = new StreamReader(stream);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            WriteAllTextIfChanged(path, reader.ReadToEnd());
        }
    }

    private static string RenderMediaViewerScript(MediaViewerSpec spec, string route)
    {
        var source = System.Web.HttpUtility.HtmlEncode(ResolveRouteRelativeAssetHref(MediaViewerJsRoute, route));
        var selector = System.Web.HttpUtility.HtmlEncode(spec.Selector);
        return $"<script src=\"{source}\" data-pf-media-selector=\"{selector}\" defer data-cfasync=\"false\"></script>";
    }
}
