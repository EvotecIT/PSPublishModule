using System.Diagnostics;

namespace PowerForge.Web;

internal static class WebAssetCssStrategy
{
    public static string Normalize(string? cssStrategy)
    {
        if (string.IsNullOrWhiteSpace(cssStrategy))
            return "blocking";

        var original = cssStrategy.Trim();
        return original.ToLowerInvariant() switch
        {
            "sync" => "blocking",
            "inline" => "blocking",
            "render-blocking" => "blocking",
            "renderblocking" => "blocking",
            "preload" => "preload",
            "async" => "async",
            "async-print" => "async",
            "print" => "async",
            _ => WarnUnknown(original)
        };
    }

    private static string WarnUnknown(string cssStrategy)
    {
        Trace.TraceWarning($"Unknown CssStrategy '{cssStrategy}', falling back to blocking.");
        return "blocking";
    }
}
