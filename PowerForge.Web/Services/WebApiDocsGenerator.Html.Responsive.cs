namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    // The filters can exceed a landscape viewport before the type list begins.
    // Scroll the entire drawer in that state so controls and links remain reachable.
    private const string ApiDocsResponsiveNavigationCss = """
@media (max-width:960px) and (max-height:600px){
  body.pf-api-docs .api-sidebar{overflow-y:auto}
  body.pf-api-docs .api-sidebar-shell{
    height:auto;
    min-height:100%;
    flex:none;
  }
  body.pf-api-docs .sidebar-nav{
    flex:none;
    overflow-y:visible;
  }
}
""";
}
