namespace PowerForge.Web;

internal interface ITemplateEngine
{
    string Render(string template, ThemeRenderContext context, Func<string, string?> partialResolver);
}
