namespace PowerForge.Web;

internal static class WebAssetCssStrategy
{
    public static string Normalize(string? cssStrategy)
    {
        if (string.IsNullOrWhiteSpace(cssStrategy))
            return "blocking";

        return cssStrategy.Trim().ToLowerInvariant() switch
        {
            "sync" => "blocking",
            "inline" => "blocking",
            "render-blocking" => "blocking",
            "renderblocking" => "blocking",
            "preload" => "preload",
            "async" => "async",
            "async-print" => "async",
            "print" => "async",
            _ => "blocking"
        };
    }
}
