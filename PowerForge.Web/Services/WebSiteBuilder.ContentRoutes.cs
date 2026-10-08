namespace PowerForge.Web;

public static partial class WebSiteBuilder
{
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
