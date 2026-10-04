namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    // Keep long type names, registry paths, and other identifiers within the content panel.
    // Scroll the entire drawer when filters can exceed a landscape viewport.
    private const string ApiDocsResponsiveCss = """
body.pf-api-docs .api-content{overflow-wrap:anywhere}
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
