namespace PowerForge.Web;

internal static class ThemeEngineRegistry
{
    public static ITemplateEngine Resolve(string? engine)
    {
        if (string.IsNullOrWhiteSpace(engine))
            return new SimpleTemplateEngine();

        if (engine.Equals("scriban", StringComparison.OrdinalIgnoreCase))
            return new ScribanTemplateEngine();

        if (engine.Equals("simple", StringComparison.OrdinalIgnoreCase))
            return new SimpleTemplateEngine();

        return new SimpleTemplateEngine();
    }
}
