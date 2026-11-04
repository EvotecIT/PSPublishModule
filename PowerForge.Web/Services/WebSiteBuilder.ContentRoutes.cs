namespace PowerForge.Web;

public static partial class WebSiteBuilder
{
    /// <summary>Converts literal output path segments to a URL path.</summary>
    internal static string EncodePhysicalRoute(string route)
        => string.Join("/", route.Split('/').Select(Uri.EscapeDataString));

    /// <summary>Resolves the output route shared by content discovery and verification.</summary>
    internal static string ResolveContentRoute(string baseOutput, string slugPath, TrailingSlashMode slashMode, FrontMatter? matter)
    {
        if (matter?.Meta is not null && TryGetMetaBool(matter.Meta, "product_page", out var productPage) && productPage)
        {
            var productPath = GetMetaString(matter.Meta, "product_path");
            if (productPath.StartsWith('/') && !productPath.StartsWith("//", System.StringComparison.Ordinal))
                return BuildRoute(productPath, string.Empty, slashMode);
        }

        return BuildRoute(baseOutput, slugPath, slashMode);
    }
}
