using Scriban;
using Scriban.Runtime;
using Scriban.Parsing;
using System.Threading.Tasks;

namespace PowerForge.Web;

internal sealed class ScribanTemplateEngine : ITemplateEngine
{
    public string Render(string template, ThemeRenderContext context, Func<string, string?> partialResolver)
    {
        if (string.IsNullOrEmpty(template)) return string.Empty;

        var parsed = Template.Parse(template);
        if (parsed.HasErrors)
        {
            var messages = string.Join(Environment.NewLine, parsed.Messages.Select(m => m.Message));
            throw new InvalidOperationException($"Scriban template errors:{Environment.NewLine}{messages}");
        }

        var globals = new ScriptObject();
        globals.Add("site", context.Site);
        globals.Add("page", context.Page);
        globals.Add("content", context.Page.HtmlContent);
        globals.Add("toc", context.Page.TocHtml);
        globals.Add("project", context.Project);
        globals.Add("assets", new
        {
            css_html = context.CssHtml,
            js_html = context.JsHtml,
            preloads_html = context.PreloadsHtml,
            critical_css_html = context.CriticalCssHtml
        });
        globals.Add("data", ToScriptValue(context.Data));
        globals.Add("canonical_html", context.CanonicalHtml);
        globals.Add("description_meta_html", context.DescriptionMetaHtml);
        globals.Add("site_name", context.Site.Name ?? string.Empty);
        globals.Add("base_url", context.Site.BaseUrl ?? string.Empty);

        var templateContext = new TemplateContext
        {
            TemplateLoader = new InlineTemplateLoader(partialResolver),
            StrictVariables = false
        };
        templateContext.PushGlobal(globals);

        return parsed.Render(templateContext);
    }

    private static object? ToScriptValue(object? value)
    {
        if (value is null) return null;

        if (value is IReadOnlyDictionary<string, object?> readOnlyMap)
        {
            var script = new ScriptObject();
            foreach (var kvp in readOnlyMap)
            {
                script.Add(kvp.Key, ToScriptValue(kvp.Value));
            }
            return script;
        }

        if (value is Dictionary<string, object?> map)
        {
            var script = new ScriptObject();
            foreach (var kvp in map)
            {
                script.Add(kvp.Key, ToScriptValue(kvp.Value));
            }
            return script;
        }

        if (value is IEnumerable<object?> list && value is not string)
        {
            var scriptArray = new ScriptArray();
            foreach (var item in list)
            {
                scriptArray.Add(ToScriptValue(item));
            }
            return scriptArray;
        }

        return value;
    }

    private sealed class InlineTemplateLoader : ITemplateLoader
    {
        private readonly Func<string, string?> _resolver;

        public InlineTemplateLoader(Func<string, string?> resolver)
        {
            _resolver = resolver;
        }

        public string GetPath(TemplateContext context, SourceSpan callerSpan, string templateName)
        {
            return templateName;
        }

        public string Load(TemplateContext context, SourceSpan callerSpan, string templatePath)
        {
            return _resolver(templatePath) ?? string.Empty;
        }

        public ValueTask<string> LoadAsync(TemplateContext context, SourceSpan callerSpan, string templatePath)
        {
            return new ValueTask<string>(Load(context, callerSpan, templatePath));
        }
    }
}
