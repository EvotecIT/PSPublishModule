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
        globals.Add("items", context.Items);
        globals.Add("content", context.Page.HtmlContent);
        globals.Add("toc", context.Page.TocHtml);
        globals.Add("project", context.Project);
        globals.Add("navigation", context.Navigation);
        globals.Add("localization", context.Localization);
        globals.Add("languages", context.Localization.Languages);
        globals.Add("current_language", context.Localization.Current);
        globals.Add("versioning", context.Versioning);
        globals.Add("versions", context.Versioning.Versions);
        globals.Add("current_version", context.Versioning.Current);
        globals.Add("latest_version", context.Versioning.Latest);
        globals.Add("outputs", context.Outputs);
        globals.Add("feed_url", context.FeedUrl ?? string.Empty);
        globals.Add("breadcrumbs", context.Breadcrumbs);
        globals.Add("shortcode", context.Shortcode);
        globals.Add("taxonomy", context.Taxonomy);
        globals.Add("term", context.Term);
        globals.Add("taxonomy_index", ToTaxonomyIndexScript(context.TaxonomyIndex));
        globals.Add("taxonomy_terms", ToTaxonomyTermsScript(context.TaxonomyTerms));
        globals.Add("taxonomy_term_summary", ToTaxonomyTermSummaryScript(context.TaxonomyTermSummary));
        globals.Add("pagination", ToPaginationScript(context.Pagination));
        globals.Add("assets", new
        {
            css_html = context.CssHtml,
            js_html = context.JsHtml,
            preloads_html = context.PreloadsHtml,
            critical_css_html = context.CriticalCssHtml
        });
        globals.Add("head_html", context.HeadHtml);
        globals.Add("opengraph_html", context.OpenGraphHtml);
        globals.Add("structured_data_html", context.StructuredDataHtml);
        globals.Add("extra_css_html", context.ExtraCssHtml);
        globals.Add("extra_scripts_html", context.ExtraScriptsHtml);
        globals.Add("body_class", context.BodyClass);
        globals.Add("edit_url", context.Page.EditUrl ?? string.Empty);
        globals.Add("data", ToScriptValue(context.Data));
        globals.Add("canonical_html", context.CanonicalHtml);
        globals.Add("description_meta_html", context.DescriptionMetaHtml);
        globals.Add("site_name", context.Site.Name ?? string.Empty);
        globals.Add("base_url", context.Site.BaseUrl ?? string.Empty);
        globals.Add("current_path", context.CurrentPath);

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

    private static ScriptObject ToPaginationScript(PaginationRuntime pagination)
    {
        var script = new ScriptObject
        {
            ["page"] = pagination.Page,
            ["total_pages"] = pagination.TotalPages,
            ["page_size"] = pagination.PageSize,
            ["total_items"] = pagination.TotalItems,
            ["has_previous"] = pagination.HasPrevious,
            ["has_next"] = pagination.HasNext,
            ["previous_url"] = pagination.PreviousUrl,
            ["next_url"] = pagination.NextUrl,
            ["first_url"] = pagination.FirstUrl,
            ["last_url"] = pagination.LastUrl,
            ["path_segment"] = pagination.PathSegment
        };
        return script;
    }

    private static ScriptObject? ToTaxonomyIndexScript(TaxonomyIndexRuntime? index)
    {
        if (index is null)
            return null;

        return new ScriptObject
        {
            ["name"] = index.Name,
            ["language"] = index.Language,
            ["total_terms"] = index.TotalTerms,
            ["total_items"] = index.TotalItems,
            ["terms"] = ToTaxonomyTermsScript(index.Terms)
        };
    }

    private static ScriptArray ToTaxonomyTermsScript(IEnumerable<TaxonomyTermRuntime>? terms)
    {
        var result = new ScriptArray();
        foreach (var term in terms ?? Array.Empty<TaxonomyTermRuntime>())
        {
            var item = new ScriptObject
            {
                ["name"] = term.Name,
                ["url"] = term.Url,
                ["count"] = term.Count,
                ["latest_date_utc"] = term.LatestDateUtc
            };
            result.Add(item);
        }

        return result;
    }

    private static ScriptObject? ToTaxonomyTermSummaryScript(TaxonomyTermSummaryRuntime? summary)
    {
        if (summary is null)
            return null;

        return new ScriptObject
        {
            ["name"] = summary.Name,
            ["count"] = summary.Count,
            ["latest_date_utc"] = summary.LatestDateUtc
        };
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
