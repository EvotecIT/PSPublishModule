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

        // Expose stable, snake_case helpers for themes.
        // Scriban does not support importing instance methods from an object directly (MethodInstance is obsolete),
        // so we register explicit delegates.
        var helpers = new ScribanThemeHelpers(context);
        var pf = new ScriptObject();
        pf.Import("menu", new Func<string?, NavigationMenu?>(helpers.Menu));
        pf.Import("surface", new Func<string?, NavigationSurfaceRuntime?>(helpers.Surface));
        pf.Import("nav_actions", new Func<string>(helpers.NavActions));
        pf.Add("nav_links", new PfNavLinksFunction(helpers));
        pf.Add("menu_tree", new PfMenuTreeFunction(helpers));
        pf.Add("editorial_cards", new PfEditorialCardsFunction(helpers));
        pf.Add("editorial_pager", new PfEditorialPagerFunction(helpers));
        pf.Add("editorial_post_nav", new PfEditorialPostNavFunction(helpers));
        globals.Add("pf", pf);

        var templateContext = new TemplateContext
        {
            TemplateLoader = new InlineTemplateLoader(partialResolver),
            StrictVariables = false
        };
        templateContext.PushGlobal(globals);

        return parsed.Render(templateContext);
    }

    private sealed class PfNavLinksFunction : IScriptCustomFunction
    {
        private readonly ScribanThemeHelpers _helpers;

        public PfNavLinksFunction(ScribanThemeHelpers helpers)
        {
            _helpers = helpers ?? throw new ArgumentNullException(nameof(helpers));
        }

        public int RequiredParameterCount => 0;
        public int ParameterCount => 2;
        public ScriptVarParamKind VarParamKind => ScriptVarParamKind.None;
        public Type ReturnType => typeof(string);

        public ScriptParameterInfo GetParameterInfo(int index)
        {
            return index switch
            {
                0 => new ScriptParameterInfo(typeof(string), "menu_name", "main"),
                1 => new ScriptParameterInfo(typeof(int), "max_depth", 1),
                _ => new ScriptParameterInfo(typeof(object), "arg")
            };
        }

        public object Invoke(TemplateContext context, Scriban.Syntax.ScriptNode callerContext, ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement blockStatement)
        {
            var menu = arguments.Count > 0 ? arguments[0]?.ToString() : "main";
            var depthArg = arguments.Count > 1 ? arguments[1] : null;
            var depth = ScribanThemeHelpers.ParseInt(depthArg, 1);
            return _helpers.NavLinks(menu, depth);
        }

        public ValueTask<object> InvokeAsync(TemplateContext context, Scriban.Syntax.ScriptNode callerContext, ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement blockStatement)
        {
            return new ValueTask<object>(Invoke(context, callerContext, arguments, blockStatement));
        }
    }

    private sealed class PfMenuTreeFunction : IScriptCustomFunction
    {
        private readonly ScribanThemeHelpers _helpers;

        public PfMenuTreeFunction(ScribanThemeHelpers helpers)
        {
            _helpers = helpers ?? throw new ArgumentNullException(nameof(helpers));
        }

        public int RequiredParameterCount => 0;
        public int ParameterCount => 2;
        public ScriptVarParamKind VarParamKind => ScriptVarParamKind.None;
        public Type ReturnType => typeof(string);

        public ScriptParameterInfo GetParameterInfo(int index)
        {
            return index switch
            {
                0 => new ScriptParameterInfo(typeof(string), "menu_name", "main"),
                1 => new ScriptParameterInfo(typeof(int), "max_depth", 3),
                _ => new ScriptParameterInfo(typeof(object), "arg")
            };
        }

        public object Invoke(TemplateContext context, Scriban.Syntax.ScriptNode callerContext, ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement blockStatement)
        {
            var menu = arguments.Count > 0 ? arguments[0]?.ToString() : "main";
            var depthArg = arguments.Count > 1 ? arguments[1] : null;
            var depth = ScribanThemeHelpers.ParseInt(depthArg, 3);
            return _helpers.MenuTree(menu, depth);
        }

        public ValueTask<object> InvokeAsync(TemplateContext context, Scriban.Syntax.ScriptNode callerContext, ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement blockStatement)
        {
            return new ValueTask<object>(Invoke(context, callerContext, arguments, blockStatement));
        }
    }

    private sealed class PfEditorialCardsFunction : IScriptCustomFunction
    {
        private readonly ScribanThemeHelpers _helpers;

        public PfEditorialCardsFunction(ScribanThemeHelpers helpers)
        {
            _helpers = helpers ?? throw new ArgumentNullException(nameof(helpers));
        }

        public int RequiredParameterCount => 0;
        public int ParameterCount => 13;
        public ScriptVarParamKind VarParamKind => ScriptVarParamKind.None;
        public Type ReturnType => typeof(string);

        public ScriptParameterInfo GetParameterInfo(int index)
        {
            return index switch
            {
                0 => new ScriptParameterInfo(typeof(int), "max_items", 0),
                1 => new ScriptParameterInfo(typeof(int), "excerpt_length", 160),
                2 => new ScriptParameterInfo(typeof(bool), "show_collection", true),
                3 => new ScriptParameterInfo(typeof(bool), "show_date", true),
                4 => new ScriptParameterInfo(typeof(bool), "show_tags", true),
                5 => new ScriptParameterInfo(typeof(bool), "show_image", true),
                6 => new ScriptParameterInfo(typeof(string), "image_aspect", string.Empty),
                7 => new ScriptParameterInfo(typeof(string), "fallback_image", string.Empty),
                8 => new ScriptParameterInfo(typeof(string), "variant", string.Empty),
                9 => new ScriptParameterInfo(typeof(string), "grid_class", string.Empty),
                10 => new ScriptParameterInfo(typeof(string), "card_class", string.Empty),
                11 => new ScriptParameterInfo(typeof(object), "show_categories", null),
                12 => new ScriptParameterInfo(typeof(object), "link_taxonomy", null),
                _ => new ScriptParameterInfo(typeof(object), "arg")
            };
        }

        public object Invoke(TemplateContext context, Scriban.Syntax.ScriptNode callerContext, ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement blockStatement)
        {
            var maxItems = arguments.Count > 0 ? ScribanThemeHelpers.ParseInt(arguments[0], 0) : 0;
            var excerptLength = arguments.Count > 1 ? ScribanThemeHelpers.ParseInt(arguments[1], 160) : 160;
            var showCollection = arguments.Count > 2 ? ScribanThemeHelpers.ParseBool(arguments[2], true) : true;
            var showDate = arguments.Count > 3 ? ScribanThemeHelpers.ParseBool(arguments[3], true) : true;
            var showTags = arguments.Count > 4 ? ScribanThemeHelpers.ParseBool(arguments[4], true) : true;
            var showImage = arguments.Count > 5 ? ScribanThemeHelpers.ParseBool(arguments[5], true) : true;
            var imageAspect = arguments.Count > 6 ? arguments[6]?.ToString() : null;
            var fallbackImage = arguments.Count > 7 ? arguments[7]?.ToString() : null;
            var variant = arguments.Count > 8 ? arguments[8]?.ToString() : null;
            var gridClass = arguments.Count > 9 ? arguments[9]?.ToString() : null;
            var cardClass = arguments.Count > 10 ? arguments[10]?.ToString() : null;
            var showCategoriesArg = arguments.Count > 11 ? arguments[11] : null;
            var linkTaxonomyArg = arguments.Count > 12 ? arguments[12] : null;
            bool? showCategories = showCategoriesArg is null
                ? null
                : ScribanThemeHelpers.ParseBool(showCategoriesArg, false);
            bool? linkTaxonomy = linkTaxonomyArg is null
                ? null
                : ScribanThemeHelpers.ParseBool(linkTaxonomyArg, false);
            return _helpers.EditorialCards(maxItems, excerptLength, showCollection, showDate, showTags, showImage, imageAspect, fallbackImage, variant, gridClass, cardClass, showCategories, linkTaxonomy);
        }

        public ValueTask<object> InvokeAsync(TemplateContext context, Scriban.Syntax.ScriptNode callerContext, ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement blockStatement)
        {
            return new ValueTask<object>(Invoke(context, callerContext, arguments, blockStatement));
        }
    }

    private sealed class PfEditorialPostNavFunction : IScriptCustomFunction
    {
        private readonly ScribanThemeHelpers _helpers;

        public PfEditorialPostNavFunction(ScribanThemeHelpers helpers)
        {
            _helpers = helpers ?? throw new ArgumentNullException(nameof(helpers));
        }

        public int RequiredParameterCount => 0;
        public int ParameterCount => 6;
        public ScriptVarParamKind VarParamKind => ScriptVarParamKind.None;
        public Type ReturnType => typeof(string);

        public ScriptParameterInfo GetParameterInfo(int index)
        {
            return index switch
            {
                0 => new ScriptParameterInfo(typeof(string), "back_label", "Back to list"),
                1 => new ScriptParameterInfo(typeof(string), "newer_label", "Newer post"),
                2 => new ScriptParameterInfo(typeof(string), "older_label", "Older post"),
                3 => new ScriptParameterInfo(typeof(string), "related_heading", "Related posts"),
                4 => new ScriptParameterInfo(typeof(int), "related_count", 3),
                5 => new ScriptParameterInfo(typeof(string), "css_class", "pf-post-nav"),
                _ => new ScriptParameterInfo(typeof(object), "arg")
            };
        }

        public object Invoke(TemplateContext context, Scriban.Syntax.ScriptNode callerContext, ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement blockStatement)
        {
            var backLabel = arguments.Count > 0 ? arguments[0]?.ToString() : "Back to list";
            var newerLabel = arguments.Count > 1 ? arguments[1]?.ToString() : "Newer post";
            var olderLabel = arguments.Count > 2 ? arguments[2]?.ToString() : "Older post";
            var relatedHeading = arguments.Count > 3 ? arguments[3]?.ToString() : "Related posts";
            var relatedCount = arguments.Count > 4 ? ScribanThemeHelpers.ParseInt(arguments[4], 3) : 3;
            var cssClass = arguments.Count > 5 ? arguments[5]?.ToString() : "pf-post-nav";
            return _helpers.EditorialPostNav(
                backLabel ?? "Back to list",
                newerLabel ?? "Newer post",
                olderLabel ?? "Older post",
                relatedHeading ?? "Related posts",
                relatedCount,
                cssClass ?? "pf-post-nav");
        }

        public ValueTask<object> InvokeAsync(TemplateContext context, Scriban.Syntax.ScriptNode callerContext, ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement blockStatement)
        {
            return new ValueTask<object>(Invoke(context, callerContext, arguments, blockStatement));
        }
    }

    private sealed class PfEditorialPagerFunction : IScriptCustomFunction
    {
        private readonly ScribanThemeHelpers _helpers;

        public PfEditorialPagerFunction(ScribanThemeHelpers helpers)
        {
            _helpers = helpers ?? throw new ArgumentNullException(nameof(helpers));
        }

        public int RequiredParameterCount => 0;
        public int ParameterCount => 3;
        public ScriptVarParamKind VarParamKind => ScriptVarParamKind.None;
        public Type ReturnType => typeof(string);

        public ScriptParameterInfo GetParameterInfo(int index)
        {
            return index switch
            {
                0 => new ScriptParameterInfo(typeof(string), "newer_label", "Newer posts"),
                1 => new ScriptParameterInfo(typeof(string), "older_label", "Older posts"),
                2 => new ScriptParameterInfo(typeof(string), "css_class", "pf-pagination"),
                _ => new ScriptParameterInfo(typeof(object), "arg")
            };
        }

        public object Invoke(TemplateContext context, Scriban.Syntax.ScriptNode callerContext, ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement blockStatement)
        {
            var newer = arguments.Count > 0 ? arguments[0]?.ToString() : "Newer posts";
            var older = arguments.Count > 1 ? arguments[1]?.ToString() : "Older posts";
            var css = arguments.Count > 2 ? arguments[2]?.ToString() : "pf-pagination";
            return _helpers.EditorialPager(newer ?? "Newer posts", older ?? "Older posts", css ?? "pf-pagination");
        }

        public ValueTask<object> InvokeAsync(TemplateContext context, Scriban.Syntax.ScriptNode callerContext, ScriptArray arguments, Scriban.Syntax.ScriptBlockStatement blockStatement)
        {
            return new ValueTask<object>(Invoke(context, callerContext, arguments, blockStatement));
        }
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
