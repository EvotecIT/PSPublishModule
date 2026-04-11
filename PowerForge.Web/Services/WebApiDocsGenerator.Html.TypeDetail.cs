using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.Loader;
using System.Xml;
using System.Xml.Linq;

namespace PowerForge.Web;

/// <summary>Generates API documentation artifacts from XML docs.</summary>
public static partial class WebApiDocsGenerator
{
    private static string BuildDocsTypeDetail(
        ApiTypeModel type,
        string baseUrl,
        IReadOnlyDictionary<string, string> slugMap,
        IReadOnlyDictionary<string, ApiTypeModel> typeIndex,
        IReadOnlyDictionary<string, List<ApiTypeModel>> derivedMap,
        ApiTypeUsageModel? usage,
        ApiTypeRelatedContentModel? relatedContent,
        string codeLanguage,
        string displayName)
    {
        var sb = new StringBuilder();
        var inheritanceChain = BuildInheritanceChain(type, typeIndex);
        var derivedTypes = GetDerivedTypes(type, derivedMap);
        var isPowerShellCommand = IsPowerShellCommandType(type);
        var methodSectionLabel = isPowerShellCommand ? "Syntax" : "Methods";
        var methodSectionId = isPowerShellCommand ? "syntax" : "methods";
        var memberFilterLabel = isPowerShellCommand ? "Filter syntax" : "Filter members";
        var memberFilterPlaceholder = isPowerShellCommand ? "Search syntax..." : "Search members...";
        var hasPowerShellCommonParameters = isPowerShellCommand && HasPowerShellCommonParameters(type);
        var toc = BuildTypeToc(type, inheritanceChain.Count > 0, derivedTypes.Count > 0, usage?.HasEntries == true, relatedContent?.Entries.Count > 0);
        var detailClasses = isPowerShellCommand
            ? "type-detail ev-page-body type-detail--powershell-command"
            : "type-detail ev-page-body";
        sb.AppendLine($"    <article class=\"{detailClasses}\">");
        var indexUrl = EnsureTrailingSlash(baseUrl);
        var kindLabel = string.IsNullOrWhiteSpace(type.Kind) ? "Type" : type.Kind;
        var sourceAction = RenderTypeSourceAction(type.Source);
        sb.AppendLine(BuildTypeBreadcrumbHtml(indexUrl, displayName).TrimEnd());
        sb.AppendLine(BuildTypeHeaderHtml(type, displayName, kindLabel, isPowerShellCommand, sourceAction).TrimEnd());

        var flags = new List<string>();
        if (type.IsStatic) flags.Add("static");
        else
        {
            if (type.IsAbstract) flags.Add("abstract");
            if (type.IsSealed) flags.Add("sealed");
        }
        sb.AppendLine(BuildTypeMetaHtml(type, baseUrl, slugMap, isPowerShellCommand, sourceAction, flags).TrimEnd());

        var detailBody = new HtmlFragmentBuilder(initialIndent: 6);

        if (!string.IsNullOrWhiteSpace(type.Summary))
            detailBody.Line($"<p class=\"type-summary\">{RenderLinkedText(type.Summary, baseUrl, slugMap)}</p>");
        if (inheritanceChain.Count > 0)
        {
            detailBody.Line("<section class=\"type-inheritance\" id=\"inheritance\">");
            using (detailBody.Indent())
            {
                detailBody.Line("<h2>Inheritance</h2>");
                detailBody.Line("<ul class=\"inheritance-list\">");
                using (detailBody.Indent())
                {
                    foreach (var entry in inheritanceChain)
                    {
                        detailBody.Line($"<li>{LinkifyType(entry, baseUrl, slugMap)}</li>");
                    }
                    detailBody.Line($"<li class=\"inheritance-current\">{System.Web.HttpUtility.HtmlEncode(type.Name)}</li>");
                }
                detailBody.Line("</ul>");
            }
            detailBody.Line("</section>");
        }

        if (derivedTypes.Count > 0)
        {
            detailBody.Line("<section class=\"type-derived\" id=\"derived-types\">");
            using (detailBody.Indent())
            {
                detailBody.Line("<h2>Derived Types</h2>");
                detailBody.Line("<ul class=\"derived-list\">");
                using (detailBody.Indent())
                {
                    foreach (var derived in derivedTypes)
                    {
                        detailBody.Line($"<li>{LinkifyType(derived.FullName, baseUrl, slugMap)}</li>");
                    }
                }
                detailBody.Line("</ul>");
            }
            detailBody.Line("</section>");
        }

        if (!string.IsNullOrWhiteSpace(type.Remarks))
        {
            detailBody.Line("<section class=\"remarks\" id=\"remarks\">");
            using (detailBody.Indent())
            {
                detailBody.Line("<h2>Remarks</h2>");
                detailBody.Line($"<p>{RenderLinkedText(type.Remarks, baseUrl, slugMap)}</p>");
            }
            detailBody.Line("</section>");
        }

        if (usage?.HasEntries == true)
            AppendUsageSection(detailBody, usage, baseUrl, slugMap);

        if (relatedContent?.Entries.Count > 0)
        {
            AppendRelatedContentSection(
                detailBody,
                relatedContent.Entries,
                baseUrl,
                slugMap,
                "guides-and-samples",
                "Guides & Samples",
                "Authored walkthroughs and practical samples linked to this API.",
                "h2");
        }

        if (type.TypeParameters.Count > 0)
        {
            detailBody.Line("<section class=\"type-parameters\" id=\"type-parameters\">");
            using (detailBody.Indent())
            {
                detailBody.Line("<h2>Type Parameters</h2>");
                detailBody.Line("<dl class=\"typeparam-list\">");
                using (detailBody.Indent())
                {
                    foreach (var tp in type.TypeParameters)
                    {
                        detailBody.Line($"<dt>{System.Web.HttpUtility.HtmlEncode(tp.Name)}</dt>");
                        if (!string.IsNullOrWhiteSpace(tp.Summary))
                            detailBody.Line($"<dd>{RenderLinkedText(tp.Summary, baseUrl, slugMap)}</dd>");
                    }
                }
                detailBody.Line("</dl>");
            }
            detailBody.Line("</section>");
        }

        if (type.Examples.Count > 0)
        {
            detailBody.Line("<section class=\"type-examples\" id=\"examples\">");
            using (detailBody.Indent())
            {
                detailBody.Line("<h2>Examples</h2>");
                AppendExamples(detailBody, type.Examples, baseUrl, slugMap, codeLanguage);
            }
            detailBody.Line("</section>");
        }

        if (type.SeeAlso.Count > 0)
        {
            detailBody.Line("<section class=\"type-see-also\" id=\"see-also\">");
            using (detailBody.Indent())
            {
                detailBody.Line("<h2>See Also</h2>");
                detailBody.Line("<ul class=\"see-also-list\">");
                using (detailBody.Indent())
                {
                    foreach (var item in type.SeeAlso)
                    {
                        detailBody.Line($"<li>{RenderLinkedText(item, baseUrl, slugMap)}</li>");
                    }
                }
                detailBody.Line("</ul>");
            }
            detailBody.Line("</section>");
        }

        if (hasPowerShellCommonParameters)
        {
            var commonParametersLink = ResolvePowerShellCommonParametersUrl(baseUrl, slugMap);
            var commonParametersLinkTarget = IsExternal(commonParametersLink) ? " target=\"_blank\" rel=\"noopener\"" : string.Empty;
            detailBody.Line("<section class=\"type-common-parameters\" id=\"common-parameters\">");
            using (detailBody.Indent())
            {
                detailBody.Line("<h2>Common Parameters</h2>");
                detailBody.Line("<p>This command supports the common parameters: -Debug, -ErrorAction, -ErrorVariable, -InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose, -WarningAction, and -WarningVariable.</p>");
                detailBody.Line($"<p>For more information, see <a href=\"{System.Web.HttpUtility.HtmlAttributeEncode(commonParametersLink)}\"{commonParametersLinkTarget}>about_CommonParameters</a>.</p>");
            }
            detailBody.Line("</section>");
        }

        var totalMembers = type.Constructors.Count + type.Methods.Count + type.Properties.Count + type.Fields.Count + type.Events.Count + type.ExtensionMethods.Count;
        if (totalMembers > 0)
        {
            detailBody.AppendRaw(BuildMemberToolbarHtml(type, totalMembers, methodSectionLabel, memberFilterLabel, memberFilterPlaceholder, isPowerShellCommand) + Environment.NewLine);

            var usedMemberIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (isPowerShellCommand)
            {
                AppendMemberSections(detailBody, methodSectionLabel, "method", type.Methods, baseUrl, slugMap, relatedContent, codeLanguage, usedMemberIds, treatAsInherited: false, groupOverloads: false, sectionId: methodSectionId);
            }
            else
            {
                AppendMemberSections(detailBody, "Constructors", "constructor", type.Constructors, baseUrl, slugMap, relatedContent, codeLanguage, usedMemberIds, treatAsInherited: false, groupOverloads: true, sectionId: "constructors");
                AppendMemberSections(detailBody, methodSectionLabel, "method", type.Methods, baseUrl, slugMap, relatedContent, codeLanguage, usedMemberIds, groupOverloads: true, sectionId: methodSectionId);
                AppendMemberSections(detailBody, "Properties", "property", type.Properties, baseUrl, slugMap, relatedContent, codeLanguage, usedMemberIds, sectionId: "properties");
                AppendMemberSections(detailBody, type.Kind == "Enum" ? "Values" : "Fields", "field", type.Fields, baseUrl, slugMap, relatedContent, codeLanguage, usedMemberIds, sectionId: type.Kind == "Enum" ? "values" : "fields");
                AppendMemberSections(detailBody, "Events", "event", type.Events, baseUrl, slugMap, relatedContent, codeLanguage, usedMemberIds, sectionId: "events");
                if (type.ExtensionMethods.Count > 0)
                    AppendMemberSections(detailBody, "Extension Methods", "extension", type.ExtensionMethods, baseUrl, slugMap, relatedContent, codeLanguage, usedMemberIds, treatAsInherited: false, groupOverloads: true, sectionId: "extensions");
            }
        }

        sb.Append(BuildTypeDetailShellHtml(detailBody.ToString(), toc));

        sb.AppendLine("    </article>");
        return sb.ToString().TrimEnd();
    }

    private static string BuildTypeBreadcrumbHtml(string indexUrl, string displayName)
    {
        var html = new HtmlFragmentBuilder(initialIndent: 6);
        html.Line("<nav class=\"breadcrumb\">");
        using (html.Indent())
        {
            html.Line($"<a href=\"{indexUrl}\">API Reference</a>");
            html.Line("<span class=\"sep\">/</span>");
            html.Line($"<span class=\"current\">{System.Web.HttpUtility.HtmlEncode(displayName)}</span>");
        }
        html.Line("</nav>");
        return html.ToString();
    }

    private static string BuildTypeHeaderHtml(ApiTypeModel type, string displayName, string kindLabel, bool isPowerShellCommand, string? sourceAction)
    {
        var html = new HtmlFragmentBuilder(initialIndent: 6);
        html.Line("<header class=\"type-header ev-docs-header\" id=\"overview\">");
        using (html.Indent())
        {
            html.Line("<p class=\"ev-eyebrow\">API Reference</p>");
            html.Line("<div class=\"type-title-row\">");
            using (html.Indent())
            {
                html.Line($"<span class=\"type-badge {NormalizeKind(type.Kind)}\">{System.Web.HttpUtility.HtmlEncode(kindLabel)}</span>");
                AppendFreshnessBadge(html, type.Freshness, "type-freshness-badge");
                html.Line($"<h1>{System.Web.HttpUtility.HtmlEncode(displayName)}</h1>");
            }
            html.Line("</div>");
            AppendAliasInlineMeta(html, type, "type-header-meta", "type-header-aliases");
            if (!isPowerShellCommand && !string.IsNullOrWhiteSpace(sourceAction))
            {
                html.Line("<div class=\"type-actions\">");
                using (html.Indent())
                {
                    html.Line(sourceAction);
                }
                html.Line("</div>");
            }
        }
        html.Line("</header>");
        return html.ToString();
    }

    private static string BuildTypeMetaHtml(
        ApiTypeModel type,
        string baseUrl,
        IReadOnlyDictionary<string, string> slugMap,
        bool isPowerShellCommand,
        string? sourceAction,
        IReadOnlyList<string> flags)
    {
        var html = new HtmlFragmentBuilder(initialIndent: 6);
        html.Line("<div class=\"type-meta\">");
        using (html.Indent())
        {
            AppendTypeMetaHtmlRow(html, "Namespace", $"<code>{System.Web.HttpUtility.HtmlEncode(type.Namespace)}</code>");

            AppendTypeMetaListRow(
                html,
                "Aliases",
                type.Aliases.Distinct(StringComparer.OrdinalIgnoreCase),
                "type-meta-aliases",
                value => $"<code>{System.Web.HttpUtility.HtmlEncode(value)}</code>");

            AppendTypeMetaListRow(
                html,
                "Inputs",
                type.InputTypes.Distinct(StringComparer.OrdinalIgnoreCase),
                "type-meta-inputs",
                value => $"<code>{System.Web.HttpUtility.HtmlEncode(value)}</code>");

            AppendTypeMetaListRow(
                html,
                "Outputs",
                type.OutputTypes.Distinct(StringComparer.OrdinalIgnoreCase),
                "type-meta-outputs",
                value => $"<code>{System.Web.HttpUtility.HtmlEncode(value)}</code>");

            if (!string.IsNullOrWhiteSpace(type.Assembly))
                AppendTypeMetaHtmlRow(html, "Assembly", $"<code>{System.Web.HttpUtility.HtmlEncode(type.Assembly)}</code>");

            if (type.Source is not null)
            {
                if (isPowerShellCommand && !string.IsNullOrWhiteSpace(sourceAction))
                {
                    AppendTypeMetaListRow(
                        html,
                        "Source",
                        new[] { RenderSourceLink(type.Source), sourceAction },
                        "type-meta-source",
                        static value => value,
                        listClass: "type-meta-list type-meta-source-links");
                }
                else
                {
                    AppendTypeMetaHtmlRow(html, "Source", RenderSourceLink(type.Source), "type-meta-source");
                }
            }

            if (type.Freshness is not null)
                AppendTypeMetaHtmlRow(html, "Updated", $"<span>{RenderFreshnessText(type.Freshness)}</span>", "type-meta-freshness");

            if (!string.IsNullOrWhiteSpace(type.BaseType))
                AppendTypeMetaHtmlRow(html, "Base", $"<code>{LinkifyType(type.BaseType, baseUrl, slugMap)}</code>", "type-meta-inheritance");

            AppendTypeMetaListRow(
                html,
                "Implements",
                type.Interfaces.Distinct(StringComparer.OrdinalIgnoreCase),
                "type-meta-interfaces",
                iface => $"<code>{LinkifyType(iface, baseUrl, slugMap)}</code>");

            if (flags.Count > 0)
                AppendTypeMetaHtmlRow(html, "Modifiers", $"<span class=\"type-meta-flags-list\">{System.Web.HttpUtility.HtmlEncode(string.Join(", ", flags))}</span>", "type-meta-flags");

            AppendTypeMetaListRow(
                html,
                "Attributes",
                type.Attributes,
                "type-meta-attributes",
                value => $"<code>{System.Web.HttpUtility.HtmlEncode(value)}</code>");
        }
        html.Line("</div>");
        return html.ToString();
    }

    private static void AppendTypeMetaHtmlRow(HtmlFragmentBuilder html, string label, string valueHtml, string? rowClass = null)
    {
        if (html is null || string.IsNullOrWhiteSpace(valueHtml))
            return;

        var classSuffix = string.IsNullOrWhiteSpace(rowClass) ? string.Empty : $" {rowClass}";
        html.Line($"<div class=\"type-meta-row{classSuffix}\">");
        using (html.Indent())
        {
            html.Line($"<span class=\"type-meta-label\">{System.Web.HttpUtility.HtmlEncode(label)}</span>");
            html.Line(valueHtml);
        }
        html.Line("</div>");
    }

    private static void AppendTypeMetaListRow(
        HtmlFragmentBuilder html,
        string label,
        IEnumerable<string> values,
        string rowClass,
        Func<string, string> renderItem,
        string listClass = "type-meta-list")
    {
        if (html is null || values is null)
            return;

        var items = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(renderItem)
            .ToArray();
        if (items.Length == 0)
            return;

        html.Line($"<div class=\"type-meta-row {rowClass}\">");
        using (html.Indent())
        {
            html.Line($"<span class=\"type-meta-label\">{System.Web.HttpUtility.HtmlEncode(label)}</span>");
            html.Line($"<div class=\"{listClass}\">");
            using (html.Indent())
            {
                foreach (var item in items)
                {
                    html.Line(item);
                }
            }
            html.Line("</div>");
        }
        html.Line("</div>");
    }

    private static string BuildTypeDetailShellHtml(string detailBodyHtml, IReadOnlyList<(string id, string label)> toc)
    {
        if (toc is null || toc.Count <= 1)
            return detailBodyHtml;

        var html = new HtmlFragmentBuilder(initialIndent: 6);
        html.Line("<div class=\"type-detail-shell\">");
        using (html.Indent())
        {
            html.Line("<div class=\"type-detail-main\">");
            html.AppendRaw(detailBodyHtml);
            html.Line("</div>");
            html.Line("<aside class=\"type-detail-rail\">");
            html.AppendRaw(BuildTypeTocHtml(toc));
            html.Line("</aside>");
        }
        html.Line("</div>");
        return html.ToString();
    }

    private static void AppendAliasInlineMeta(HtmlFragmentBuilder html, ApiTypeModel type, string wrapperClass, string aliasesClass)
    {
        if (html is null || type is null)
            return;

        var aliases = type.Aliases
            .Where(static alias => !string.IsNullOrWhiteSpace(alias))
            .Select(static alias => alias.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (aliases.Length == 0)
            return;

        html.Line($"<span class=\"{System.Web.HttpUtility.HtmlEncode(wrapperClass)}\">");
        using (html.Indent())
        {
            html.Line($"<span class=\"{System.Web.HttpUtility.HtmlEncode(aliasesClass)}\">Aliases: {System.Web.HttpUtility.HtmlEncode(string.Join(", ", aliases))}</span>");
        }
        html.Line("</span>");
    }

    private static string BuildTypeTocHtml(IReadOnlyList<(string id, string label)> toc)
    {
        if (toc is null || toc.Count <= 1)
            return string.Empty;

        var html = new HtmlFragmentBuilder(initialIndent: 10);
        html.Line("<nav class=\"type-toc\">");
        using (html.Indent())
        {
            html.Line("<div class=\"type-toc-header\">");
            using (html.Indent())
            {
                html.Line("<span class=\"type-toc-title\">On this page</span>");
                html.Line("<button class=\"type-toc-toggle\" type=\"button\" aria-label=\"Toggle table of contents\">");
                using (html.Indent())
                {
                    html.Line("<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">");
                    using (html.Indent())
                    {
                        html.Line("<path d=\"M9 18l6-6-6-6\"/>");
                    }
                    html.Line("</svg>");
                }
                html.Line("</button>");
            }
            html.Line("</div>");
            html.Line("<ul>");
            using (html.Indent())
            {
                foreach (var entry in toc)
                {
                    html.Line($"<li><a href=\"#{entry.id}\">{System.Web.HttpUtility.HtmlEncode(entry.label)}</a></li>");
                }
            }
            html.Line("</ul>");
        }
        html.Line("</nav>");
        return html.ToString();
    }

    private static string BuildMemberToolbarHtml(
        ApiTypeModel type,
        int totalMembers,
        string methodSectionLabel,
        string memberFilterLabel,
        string memberFilterPlaceholder,
        bool isPowerShellCommand)
    {
        var html = new HtmlFragmentBuilder(initialIndent: 6);
        html.Line($"<div class=\"member-toolbar\" data-member-total=\"{totalMembers}\">");
        using (html.Indent())
        {
            html.Line("<div class=\"member-filter\">");
            using (html.Indent())
            {
                html.Line($"<label for=\"api-member-filter\">{System.Web.HttpUtility.HtmlEncode(memberFilterLabel)}</label>");
                html.Line($"<input id=\"api-member-filter\" type=\"text\" placeholder=\"{System.Web.HttpUtility.HtmlAttributeEncode(memberFilterPlaceholder)}\" />");
            }
            html.Line("</div>");

            html.Line("<div class=\"member-kind-filter\">");
            using (html.Indent())
            {
                html.Line($"<button class=\"member-kind active\" type=\"button\" data-member-kind=\"\">All ({totalMembers})</button>");
                if (type.Constructors.Count > 0)
                    html.Line($"<button class=\"member-kind\" type=\"button\" data-member-kind=\"constructor\">Constructors ({type.Constructors.Count})</button>");
                if (type.Methods.Count > 0)
                    html.Line($"<button class=\"member-kind\" type=\"button\" data-member-kind=\"method\">{System.Web.HttpUtility.HtmlEncode(methodSectionLabel)} ({type.Methods.Count})</button>");
                if (type.Properties.Count > 0)
                    html.Line($"<button class=\"member-kind\" type=\"button\" data-member-kind=\"property\">Properties ({type.Properties.Count})</button>");
                if (type.Fields.Count > 0)
                    html.Line($"<button class=\"member-kind\" type=\"button\" data-member-kind=\"field\">{System.Web.HttpUtility.HtmlEncode(type.Kind == "Enum" ? "Values" : "Fields")} ({type.Fields.Count})</button>");
                if (type.Events.Count > 0)
                    html.Line($"<button class=\"member-kind\" type=\"button\" data-member-kind=\"event\">Events ({type.Events.Count})</button>");
                if (type.ExtensionMethods.Count > 0)
                    html.Line($"<button class=\"member-kind\" type=\"button\" data-member-kind=\"extension\">Extensions ({type.ExtensionMethods.Count})</button>");
            }
            html.Line("</div>");

            if (!isPowerShellCommand)
            {
                html.Line("<label class=\"member-toggle\">");
                using (html.Indent())
                {
                    html.Line("<input type=\"checkbox\" id=\"api-show-inherited\" />");
                    html.Line("Show inherited");
                }
                html.Line("</label>");
            }

            html.Line("<div class=\"member-actions\">");
            using (html.Indent())
            {
                html.Line("<button class=\"member-expand-all\" type=\"button\">Expand all</button>");
                html.Line("<button class=\"member-collapse-all\" type=\"button\">Collapse all</button>");
                html.Line("<button class=\"member-reset\" type=\"button\">Reset</button>");
            }
            html.Line("</div>");
        }
        html.Line("</div>");
        return html.ToString().TrimEnd();
    }

    private static void AppendMemberSections(
        StringBuilder sb,
        string label,
        string memberKind,
        List<ApiMemberModel> members,
        string baseUrl,
        IReadOnlyDictionary<string, string> slugMap,
        ApiTypeRelatedContentModel? relatedContent,
        string codeLanguage,
        ISet<string> usedMemberIds,
        bool treatAsInherited = true,
        bool groupOverloads = false,
        string? sectionId = null)
    {
        if (sb is null)
            return;

        var html = new HtmlFragmentBuilder(initialIndent: 6);
        AppendMemberSections(html, label, memberKind, members, baseUrl, slugMap, relatedContent, codeLanguage, usedMemberIds, treatAsInherited, groupOverloads, sectionId);
        if (!html.IsEmpty)
            sb.AppendLine(html.ToString().TrimEnd());
    }

    private static void AppendMemberSections(
        HtmlFragmentBuilder html,
        string label,
        string memberKind,
        List<ApiMemberModel> members,
        string baseUrl,
        IReadOnlyDictionary<string, string> slugMap,
        ApiTypeRelatedContentModel? relatedContent,
        string codeLanguage,
        ISet<string> usedMemberIds,
        bool treatAsInherited = true,
        bool groupOverloads = false,
        string? sectionId = null)
    {
        if (html is null || members.Count == 0)
            return;

        var direct = members.Where(m => !m.IsInherited).ToList();
        var inherited = treatAsInherited ? members.Where(m => m.IsInherited).ToList() : new List<ApiMemberModel>();

        var directId = direct.Count > 0 ? sectionId : null;
        var inheritedId = direct.Count == 0 ? sectionId : null;
        if (direct.Count > 0)
            AppendMemberCards(html, label, memberKind, direct, baseUrl, slugMap, relatedContent, codeLanguage, usedMemberIds, false, groupOverloads, directId);
        if (inherited.Count > 0)
            AppendMemberCards(html, $"Inherited {label}", memberKind, inherited, baseUrl, slugMap, relatedContent, codeLanguage, usedMemberIds, true, groupOverloads, inheritedId);
    }

    private static void AppendMemberCards(
        StringBuilder sb,
        string label,
        string memberKind,
        List<ApiMemberModel> members,
        string baseUrl,
        IReadOnlyDictionary<string, string> slugMap,
        ApiTypeRelatedContentModel? relatedContent,
        string codeLanguage,
        ISet<string> usedMemberIds,
        bool inheritedSection,
        bool groupOverloads,
        string? sectionId)
    {
        if (sb is null)
            return;

        var html = new HtmlFragmentBuilder(initialIndent: 6);
        AppendMemberCards(html, label, memberKind, members, baseUrl, slugMap, relatedContent, codeLanguage, usedMemberIds, inheritedSection, groupOverloads, sectionId);
        if (!html.IsEmpty)
            sb.AppendLine(html.ToString().TrimEnd());
    }

    private static void AppendMemberCards(
        HtmlFragmentBuilder html,
        string label,
        string memberKind,
        List<ApiMemberModel> members,
        string baseUrl,
        IReadOnlyDictionary<string, string> slugMap,
        ApiTypeRelatedContentModel? relatedContent,
        string codeLanguage,
        ISet<string> usedMemberIds,
        bool inheritedSection,
        bool groupOverloads,
        string? sectionId)
    {
        if (html is null || members.Count == 0)
            return;

        var collapsed = inheritedSection ? " collapsed" : string.Empty;
        var idAttribute = string.IsNullOrWhiteSpace(sectionId) ? string.Empty : $" id=\"{sectionId}\"";
        html.Line($"<section class=\"member-section{collapsed}\" data-kind=\"{memberKind}\"{idAttribute}>");
        using (html.Indent())
        {
            html.Line("<div class=\"member-section-header\">");
            using (html.Indent())
            {
                html.Line($"<h2>{System.Web.HttpUtility.HtmlEncode(label)}</h2>");
                html.Line("<button class=\"member-section-toggle\" type=\"button\" aria-label=\"Toggle section\">");
                using (html.Indent())
                {
                    html.Line("<svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">");
                    using (html.Indent())
                    {
                        html.Line("<path d=\"M9 18l6-6-6-6\"/>");
                    }
                    html.Line("</svg>");
                }
                html.Line("</button>");
            }
            html.Line("</div>");

        var hidden = inheritedSection ? " hidden" : string.Empty;
            html.Line($"<div class=\"member-section-body\"{hidden}>");
            using (html.Indent())
            {
                if (groupOverloads)
                {
                    var grouped = members
                        .GroupBy(m => string.IsNullOrWhiteSpace(m.DisplayName) ? m.Name : m.DisplayName, StringComparer.OrdinalIgnoreCase)
                        .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    foreach (var group in grouped)
                    {
                        if (group.Count() == 1)
                        {
                            AppendMemberCard(html, memberKind, group.First(), baseUrl, slugMap, relatedContent, codeLanguage, usedMemberIds, label);
                            continue;
                        }

                        html.Line("<div class=\"member-group\">");
                        using (html.Indent())
                        {
                            html.Line("<div class=\"member-group-header\">");
                            using (html.Indent())
                            {
                                html.Line($"<span class=\"member-group-name\">{System.Web.HttpUtility.HtmlEncode(group.Key)}</span>");
                                html.Line($"<span class=\"member-group-count\">{group.Count()} overloads</span>");
                            }
                            html.Line("</div>");
                            html.Line("<div class=\"member-group-body\">");
                            using (html.Indent())
                            {
                                foreach (var member in group)
                                {
                                    AppendMemberCard(html, memberKind, member, baseUrl, slugMap, relatedContent, codeLanguage, usedMemberIds, label);
                                }
                            }
                            html.Line("</div>");
                        }
                        html.Line("</div>");
                    }
                }
                else
                {
                    foreach (var member in members)
                    {
                        AppendMemberCard(html, memberKind, member, baseUrl, slugMap, relatedContent, codeLanguage, usedMemberIds, label);
                    }
                }
            }
            html.Line("</div>");
        }
        html.Line("</section>");
    }

    private static void AppendMemberCard(
        StringBuilder sb,
        string memberKind,
        ApiMemberModel member,
        string baseUrl,
        IReadOnlyDictionary<string, string> slugMap,
        ApiTypeRelatedContentModel? relatedContent,
        string codeLanguage,
        ISet<string> usedMemberIds,
        string sectionLabel)
    {
        if (sb is null)
            return;

        var html = new HtmlFragmentBuilder(initialIndent: 8);
        AppendMemberCard(html, memberKind, member, baseUrl, slugMap, relatedContent, codeLanguage, usedMemberIds, sectionLabel);
        if (!html.IsEmpty)
            sb.AppendLine(html.ToString().TrimEnd());
    }

    private static void AppendMemberCard(
        HtmlFragmentBuilder html,
        string memberKind,
        ApiMemberModel member,
        string baseUrl,
        IReadOnlyDictionary<string, string> slugMap,
        ApiTypeRelatedContentModel? relatedContent,
        string codeLanguage,
        ISet<string> usedMemberIds,
        string sectionLabel)
    {
        if (html is null)
            return;

        var memberId = BuildUniqueMemberId(BuildMemberId(memberKind, member), usedMemberIds);
        var signature = !string.IsNullOrWhiteSpace(member.Signature)
            ? member.Signature
            : BuildSignature(member, sectionLabel);
        var search = $"{member.Name} {signature} {member.Summary} {member.ParameterSetName}".Trim();
        var searchAttr = System.Web.HttpUtility.HtmlEncode(search);
        var inherited = member.IsInherited ? "true" : "false";
        var inheritedNote = member.IsInherited && !string.IsNullOrWhiteSpace(member.DeclaringType)
            ? $"Inherited from {member.DeclaringType}"
            : string.Empty;

        html.Line($"<div class=\"member-card\" id=\"{memberId}\" data-kind=\"{memberKind}\" data-inherited=\"{inherited}\" data-search=\"{searchAttr}\">");
        using (html.Indent())
        {
            html.Line("<div class=\"member-header\">");
            using (html.Indent())
            {
                html.Line(BuildMemberSignatureHtml(signature, sectionLabel, codeLanguage));
                html.Line($"<a class=\"member-anchor\" href=\"#{memberId}\" aria-label=\"Link to {System.Web.HttpUtility.HtmlEncode(member.Name)}\">#</a>");
            }
            html.Line("</div>");

            if (string.Equals(sectionLabel, "Syntax", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(member.ParameterSetName))
                html.Line($"<div class=\"member-parameter-set\">Parameter set: <code>{System.Web.HttpUtility.HtmlEncode(member.ParameterSetName)}</code></div>");

            if (member.Source is not null)
                html.Line($"<div class=\"member-source\">{RenderSourceLink(member.Source)}</div>");

            if (!string.IsNullOrWhiteSpace(member.ReturnType) && (sectionLabel.Contains("Method") || memberKind == "extension"))
                html.Line($"<div class=\"member-return\">Returns: <code>{System.Web.HttpUtility.HtmlEncode(member.ReturnType)}</code></div>");

            if (!string.IsNullOrWhiteSpace(inheritedNote))
            {
                var declaring = LinkifyType(member.DeclaringType, baseUrl, slugMap);
                html.Line($"<div class=\"member-inherited\">Inherited from {declaring}</div>");
            }

            if (member.Attributes.Count > 0)
            {
                html.Line("<div class=\"member-attributes\">");
                using (html.Indent())
                {
                    foreach (var attr in member.Attributes)
                    {
                        html.Line($"<code>{System.Web.HttpUtility.HtmlEncode(attr)}</code>");
                    }
                }
                html.Line("</div>");
            }

            if (!string.IsNullOrWhiteSpace(member.Summary))
                html.Line($"<p class=\"member-summary\">{RenderLinkedText(member.Summary, baseUrl, slugMap)}</p>");

            if (relatedContent is not null &&
                relatedContent.MemberEntries.TryGetValue(member, out var memberRelatedContent) &&
                memberRelatedContent.Count > 0)
            {
                AppendMemberRelatedContent(html, memberRelatedContent, baseUrl, slugMap);
            }

            if (member.TypeParameters.Count > 0)
            {
                html.Line("<h3>Type Parameters</h3>");
                html.Line("<dl class=\"typeparam-list\">");
                using (html.Indent())
                {
                    foreach (var tp in member.TypeParameters)
                    {
                        html.Line($"<dt>{System.Web.HttpUtility.HtmlEncode(tp.Name)}</dt>");
                        if (!string.IsNullOrWhiteSpace(tp.Summary))
                            html.Line($"<dd>{RenderLinkedText(tp.Summary, baseUrl, slugMap)}</dd>");
                    }
                }
                html.Line("</dl>");
            }

            if (member.Parameters.Count > 0)
            {
                html.Line("<h3>Parameters</h3>");
                html.Line("<dl class=\"param-list\">");
                using (html.Indent())
                {
                    foreach (var param in member.Parameters)
                    {
                        var optional = param.IsOptional ? " optional" : string.Empty;
                        var defaultValue = param.DefaultValue;
                        var defaultText = string.IsNullOrWhiteSpace(defaultValue) ? string.Empty : $" = {defaultValue}";
                        html.Line($"<dt><span class=\"param-name\">{System.Web.HttpUtility.HtmlEncode(param.Name)}</span> <span class=\"param-type{optional}\">{System.Web.HttpUtility.HtmlEncode(param.Type)}</span><span class=\"param-default\">{System.Web.HttpUtility.HtmlEncode(defaultText)}</span>{BuildParameterMetaChips(param)}</dt>");
                        if (!string.IsNullOrWhiteSpace(param.Summary))
                            html.Line($"<dd>{RenderLinkedText(param.Summary, baseUrl, slugMap)}</dd>");
                        if (param.PossibleValues.Count > 0)
                            html.Line($"<dd class=\"param-possible-values\">Possible values: {RenderPowerShellPossibleValues(param.PossibleValues)}</dd>");
                    }
                }
                html.Line("</dl>");
            }

            if (!string.IsNullOrWhiteSpace(member.ValueSummary))
            {
                html.Line("<h3>Value</h3>");
                html.Line($"<p>{RenderLinkedText(member.ValueSummary, baseUrl, slugMap)}</p>");
            }

            if (sectionLabel == "Fields" || sectionLabel == "Values")
            {
                if (!string.IsNullOrWhiteSpace(member.Value))
                    html.Line($"<div class=\"member-value\">Value: <code>{System.Web.HttpUtility.HtmlEncode(member.Value)}</code></div>");
            }

            if (!string.IsNullOrWhiteSpace(member.Returns))
            {
                var returnsLabel = string.Equals(sectionLabel, "Syntax", StringComparison.OrdinalIgnoreCase) ? "Outputs" : "Returns";
                html.Line($"<h3>{returnsLabel}</h3>");
                html.Line($"<p>{RenderLinkedText(member.Returns, baseUrl, slugMap)}</p>");
            }

            if (member.Exceptions.Count > 0)
            {
                html.Line("<h3>Exceptions</h3>");
                html.Line("<ul class=\"exception-list\">");
                using (html.Indent())
                {
                    foreach (var ex in member.Exceptions)
                    {
                        var type = LinkifyType(ex.Type, baseUrl, slugMap);
                        var desc = string.IsNullOrWhiteSpace(ex.Summary) ? string.Empty : $" – {RenderLinkedText(ex.Summary, baseUrl, slugMap)}";
                        html.Line($"<li><code>{type}</code>{desc}</li>");
                    }
                }
                html.Line("</ul>");
            }

            if (member.Examples.Count > 0)
            {
                html.Line("<h3>Examples</h3>");
                AppendExamples(html, member.Examples, baseUrl, slugMap, codeLanguage);
            }

            if (member.SeeAlso.Count > 0)
            {
                html.Line("<h3>See Also</h3>");
                html.Line("<ul class=\"see-also-list\">");
                using (html.Indent())
                {
                    foreach (var item in member.SeeAlso)
                    {
                        html.Line($"<li>{RenderLinkedText(item, baseUrl, slugMap)}</li>");
                    }
                }
                html.Line("</ul>");
            }
        }
        html.Line("</div>");
    }

    private static void AppendExamples(
        StringBuilder sb,
        List<ApiExampleModel> examples,
        string baseUrl,
        IReadOnlyDictionary<string, string> slugMap,
        string codeLanguage)
    {
        if (sb is null)
            return;

        var html = new HtmlFragmentBuilder(initialIndent: 8);
        AppendExamples(html, examples, baseUrl, slugMap, codeLanguage);
        if (!html.IsEmpty)
            sb.AppendLine(html.ToString().TrimEnd());
    }

    private static void AppendExamples(
        HtmlFragmentBuilder html,
        List<ApiExampleModel> examples,
        string baseUrl,
        IReadOnlyDictionary<string, string> slugMap,
        string codeLanguage)
    {
        if (html is null || examples is null || examples.Count == 0)
            return;

        string? lastOrigin = null;
        foreach (var example in examples)
        {
            AppendExampleOriginBadge(html, example, ref lastOrigin);
            if (string.Equals(example.Kind, "media", StringComparison.OrdinalIgnoreCase) && example.Media is not null)
            {
                AppendExampleMedia(html, example.Media, baseUrl, slugMap);
                continue;
            }
            if (string.IsNullOrWhiteSpace(example.Text))
                continue;
            if (string.Equals(example.Kind, "code", StringComparison.OrdinalIgnoreCase))
            {
                AppendExampleCodeBlock(html, example.Text, codeLanguage);
            }
            else if (string.Equals(example.Kind, "heading", StringComparison.OrdinalIgnoreCase))
            {
                html.Line($"<h3 class=\"example-title\">{System.Web.HttpUtility.HtmlEncode(example.Text)}</h3>");
            }
            else
            {
                foreach (var paragraph in SplitExampleParagraphs(example.Text))
                {
                    html.Line($"<p>{RenderLinkedText(paragraph, baseUrl, slugMap)}</p>");
                }
            }
        }
    }

    private static void AppendExampleCodeBlock(StringBuilder sb, string? codeText, string codeLanguage)
    {
        if (sb is null)
            return;

        var html = new HtmlFragmentBuilder(initialIndent: 8);
        AppendExampleCodeBlock(html, codeText, codeLanguage);
        if (!html.IsEmpty)
            sb.AppendLine(html.ToString().TrimEnd());
    }

    private static void AppendExampleCodeBlock(HtmlFragmentBuilder html, string? codeText, string codeLanguage)
    {
        if (html is null || string.IsNullOrWhiteSpace(codeText))
            return;

        var normalizedCode = codeText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        var languageToken = string.IsNullOrWhiteSpace(codeLanguage)
            ? string.Empty
            : codeLanguage.Trim().ToLowerInvariant();
        var languageClass = string.IsNullOrWhiteSpace(languageToken)
            ? string.Empty
            : $" class=\"language-{languageToken}\"";

        if (TrySplitPowerShellPrompt(normalizedCode, languageToken, out var prompt, out var body))
        {
            html.Line($"<pre class=\"language-{languageToken} example-code-block example-code-block--has-prompt\">");
            using (html.Indent())
            {
                html.Line($"<span class=\"example-code__prompt\">{System.Web.HttpUtility.HtmlEncode(prompt)}</span>");
                html.Line($"<code class=\"language-{languageToken} example-code__body\">{System.Web.HttpUtility.HtmlEncode(body)}</code>");
            }
            html.Line("</pre>");
            return;
        }

        html.Line($"<pre{languageClass}><code{languageClass}>");
        html.AppendRaw(System.Web.HttpUtility.HtmlEncode(normalizedCode));
        if (!normalizedCode.EndsWith('\n'))
            html.AppendRaw(Environment.NewLine);
        html.Line("</code></pre>");
    }

    private static bool TrySplitPowerShellPrompt(string codeText, string? languageToken, out string prompt, out string body)
    {
        prompt = string.Empty;
        body = codeText;

        if (!string.Equals(languageToken, "powershell", StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.IsNullOrWhiteSpace(codeText))
            return false;

        var trimmedStart = codeText.TrimStart();
        if (!trimmedStart.StartsWith("PS>", StringComparison.Ordinal))
            return false;

        prompt = "PS>";
        body = trimmedStart.Substring(prompt.Length).TrimStart();
        return !string.IsNullOrWhiteSpace(body);
    }

    private static void AppendExampleOriginBadge(
        StringBuilder sb,
        ApiExampleModel example,
        ref string? lastOrigin)
    {
        if (sb is null)
            return;

        var html = new HtmlFragmentBuilder(initialIndent: 8);
        AppendExampleOriginBadge(html, example, ref lastOrigin);
        if (!html.IsEmpty)
            sb.AppendLine(html.ToString().TrimEnd());
    }

    private static void AppendExampleOriginBadge(
        HtmlFragmentBuilder html,
        ApiExampleModel example,
        ref string? lastOrigin)
    {
        if (html is null || example is null)
            return;

        if (string.IsNullOrWhiteSpace(example.Origin))
        {
            lastOrigin = null;
            return;
        }

        if (string.Equals(lastOrigin, example.Origin, StringComparison.OrdinalIgnoreCase))
            return;

        var (label, description, className) = GetExampleOriginPresentation(example.Origin);
        if (string.IsNullOrWhiteSpace(label))
        {
            lastOrigin = example.Origin;
            return;
        }

        var encodedOrigin = System.Web.HttpUtility.HtmlEncode(example.Origin);
        var encodedClass = System.Web.HttpUtility.HtmlEncode(className);
        var encodedLabel = System.Web.HttpUtility.HtmlEncode(label);
        var encodedDescription = System.Web.HttpUtility.HtmlEncode(description);

        html.Line($"<div class=\"example-origin\" data-example-origin=\"{encodedOrigin}\">");
        using (html.Indent())
        {
            html.Line($"<span class=\"param-meta-chip example-origin-badge {encodedClass}\" title=\"{encodedDescription}\">{encodedLabel}</span>");
        }
        html.Line("</div>");
        lastOrigin = example.Origin;
    }

    private static (string Label, string Description, string ClassName) GetExampleOriginPresentation(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
            return (string.Empty, string.Empty, string.Empty);

        if (string.Equals(origin, ApiExampleOrigins.AuthoredHelp, StringComparison.OrdinalIgnoreCase))
            return ("Authored help example", "Example authored directly in source help.", "example-origin-authored");
        if (string.Equals(origin, ApiExampleOrigins.ImportedScript, StringComparison.OrdinalIgnoreCase))
            return ("Imported script example", "Example imported from a curated PowerShell script.", "example-origin-imported");
        if (string.Equals(origin, ApiExampleOrigins.GeneratedFallback, StringComparison.OrdinalIgnoreCase))
            return ("Generated fallback example", "Example generated from documented parameter sets.", "example-origin-generated");

        return (origin.Trim(), $"Example origin: {origin.Trim()}.", "example-origin-other");
    }

    private static void AppendExampleMedia(
        StringBuilder sb,
        ApiExampleMediaModel media,
        string baseUrl,
        IReadOnlyDictionary<string, string> slugMap)
    {
        if (sb is null)
            return;

        var html = new HtmlFragmentBuilder(initialIndent: 8);
        AppendExampleMedia(html, media, baseUrl, slugMap);
        if (!html.IsEmpty)
            sb.AppendLine(html.ToString().TrimEnd());
    }

    private static void AppendExampleMedia(
        HtmlFragmentBuilder html,
        ApiExampleMediaModel media,
        string baseUrl,
        IReadOnlyDictionary<string, string> slugMap)
    {
        if (html is null || media is null || string.IsNullOrWhiteSpace(media.Url))
            return;

        var mediaType = string.IsNullOrWhiteSpace(media.Type) ? "link" : media.Type.Trim().ToLowerInvariant();
        var safeType = System.Web.HttpUtility.HtmlEncode(mediaType);
        var safeUrl = System.Web.HttpUtility.HtmlEncode(media.Url);
        var caption = string.IsNullOrWhiteSpace(media.Caption) ? string.Empty : media.Caption.Trim();
        var title = string.IsNullOrWhiteSpace(media.Title) ? string.Empty : media.Title.Trim();
        var alt = string.IsNullOrWhiteSpace(media.Alt)
            ? (!string.IsNullOrWhiteSpace(title) ? title : "Example media")
            : media.Alt.Trim();
        var safeAlt = System.Web.HttpUtility.HtmlEncode(alt);
        var safeTitle = System.Web.HttpUtility.HtmlEncode(title);
        var safePoster = string.IsNullOrWhiteSpace(media.PosterUrl) ? string.Empty : System.Web.HttpUtility.HtmlEncode(media.PosterUrl);
        var safeMimeType = string.IsNullOrWhiteSpace(media.MimeType) ? string.Empty : System.Web.HttpUtility.HtmlEncode(media.MimeType);
        var widthAttr = media.Width is > 0 ? $" width=\"{media.Width.Value}\"" : string.Empty;
        var heightAttr = media.Height is > 0 ? $" height=\"{media.Height.Value}\"" : string.Empty;

        html.Line($"<figure class=\"example-media example-media-{safeType}\" data-example-media-type=\"{safeType}\">");
        using (html.Indent())
        {
            switch (mediaType)
            {
                case "image":
                    html.Line("<div class=\"example-media-frame\">");
                    using (html.Indent())
                    {
                        html.Line($"<img src=\"{safeUrl}\" alt=\"{safeAlt}\" loading=\"lazy\" decoding=\"async\"{widthAttr}{heightAttr} />");
                    }
                    html.Line("</div>");
                    break;
                case "video":
                    html.Line("<div class=\"example-media-frame\">");
                    using (html.Indent())
                    {
                        html.Line(BuildExampleVideoOpenTag(safePoster, safeTitle, widthAttr, heightAttr));

                        using (html.Indent())
                        {
                            html.Line(BuildExampleVideoSourceTag(safeUrl, safeMimeType));
                        }

                        html.Line("</video>");
                    }
                    html.Line("</div>");
                    break;
                case "terminal":
                    html.Line("<div class=\"example-media-frame example-media-frame-terminal\">");
                    using (html.Indent())
                    {
                        if (!string.IsNullOrWhiteSpace(safePoster))
                            html.Line($"<img src=\"{safePoster}\" alt=\"{safeAlt}\" loading=\"lazy\" decoding=\"async\" class=\"example-media-poster\"{widthAttr}{heightAttr} />");
                        html.Line($"<a class=\"example-media-link\" href=\"{safeUrl}\">{System.Web.HttpUtility.HtmlEncode(string.IsNullOrWhiteSpace(title) ? "Open terminal recording" : title)}</a>");
                    }
                    html.Line("</div>");
                    break;
                default:
                    html.Line("<div class=\"example-media-frame example-media-frame-link\">");
                    using (html.Indent())
                    {
                        html.Line($"<a class=\"example-media-link\" href=\"{safeUrl}\">{System.Web.HttpUtility.HtmlEncode(string.IsNullOrWhiteSpace(title) ? media.Url : title)}</a>");
                    }
                    html.Line("</div>");
                    break;
            }

            if (!string.IsNullOrWhiteSpace(caption))
                html.Line($"<figcaption class=\"example-media-caption\">{RenderLinkedText(caption, baseUrl, slugMap)}</figcaption>");
            var mediaMeta = BuildExampleMediaMeta(media);
            if (!string.IsNullOrWhiteSpace(mediaMeta))
                html.Line($"<p class=\"example-media-meta\">{System.Web.HttpUtility.HtmlEncode(mediaMeta)}</p>");
        }

        html.Line("</figure>");
    }

    private static string BuildExampleVideoOpenTag(string safePoster, string safeTitle, string widthAttr, string heightAttr)
    {
        var attributes = new List<string> { "controls", "preload=\"metadata\"" };
        if (!string.IsNullOrWhiteSpace(safePoster))
            attributes.Add($"poster=\"{safePoster}\"");
        if (!string.IsNullOrWhiteSpace(safeTitle))
            attributes.Add($"title=\"{safeTitle}\"");
        if (!string.IsNullOrWhiteSpace(widthAttr))
            attributes.Add(widthAttr.Trim());
        if (!string.IsNullOrWhiteSpace(heightAttr))
            attributes.Add(heightAttr.Trim());

        return $"<video {string.Join(" ", attributes)}>";
    }

    private static string BuildExampleVideoSourceTag(string safeUrl, string safeMimeType)
    {
        var attributes = new List<string> { $"src=\"{safeUrl}\"" };
        if (!string.IsNullOrWhiteSpace(safeMimeType))
            attributes.Add($"type=\"{safeMimeType}\"");

        return $"<source {string.Join(" ", attributes)} />";
    }

    private static string BuildExampleMediaMeta(ApiExampleMediaModel media)
    {
        if (media is null)
            return string.Empty;

        var parts = new List<string>();
        if (media.CapturedAtUtc is not null)
            parts.Add("Captured " + media.CapturedAtUtc.Value.UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture));
        if (media.HasStaleAssets)
            parts.Add("Script changed after this capture");
        else if (media.SourceUpdatedAtUtc is not null &&
                 media.CapturedAtUtc is not null &&
                 media.SourceUpdatedAtUtc.Value > media.CapturedAtUtc.Value)
            parts.Add("Script changed after this capture");

        return string.Join(" | ", parts.Where(static part => !string.IsNullOrWhiteSpace(part)));
    }

    private static IEnumerable<string> SplitExampleParagraphs(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Array.Empty<string>();

        var normalized = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');

        var blocks = ParagraphSplitRegex.Split(normalized)
            .Select(block => block.Trim())
            .Where(block => !string.IsNullOrWhiteSpace(block))
            .ToList();

        return blocks.Count == 0 ? new[] { text.Trim() } : blocks;
    }

    private static string GetDefaultCodeLanguage(WebApiDocsOptions options)
    {
        return options.Type switch
        {
            ApiDocsType.PowerShell => "powershell",
            ApiDocsType.CSharp => "csharp",
            _ => string.Empty
        };
    }

    private static string BuildSignature(ApiMemberModel member, string section)
    {
        var displayName = string.IsNullOrWhiteSpace(member.DisplayName) ? member.Name : member.DisplayName;
        var prefix = BuildMemberPrefix(member);
        var args = member.Parameters
            .Select(p =>
            {
                var type = string.IsNullOrWhiteSpace(p.Type) ? string.Empty : p.Type;
                var name = string.IsNullOrWhiteSpace(p.Name) ? string.Empty : p.Name;
                return string.IsNullOrWhiteSpace(type) ? name : $"{type} {name}".Trim();
            })
            .ToList();
        if (member.IsConstructor || section == "Constructors")
            return $"{prefix}{displayName}({string.Join(", ", args)})".Trim();

        if (section == "Methods" || section == "Extension Methods")
        {
            var returnType = string.IsNullOrWhiteSpace(member.ReturnType) ? string.Empty : $"{member.ReturnType} ";
            return $"{prefix}{returnType}{displayName}({string.Join(", ", args)})".Trim();
        }
        if (section == "Syntax")
        {
            return BuildPowerShellSyntaxSignature(displayName, member.Parameters, member.IncludesCommonParameters);
        }

        if (!string.IsNullOrWhiteSpace(member.ReturnType))
        {
            if (section == "Events")
                return $"{prefix}event {member.ReturnType} {displayName}".Trim();
            return $"{prefix}{member.ReturnType} {displayName}".Trim();
        }

        return $"{prefix}{displayName}".Trim();
    }

    private static string BuildParameterMetaChips(ApiParameterModel param)
    {
        if (param is null)
            return string.Empty;

        var chips = new List<string>
        {
            $"<span class=\"param-meta-chip {(param.IsOptional ? "optional" : "required")}\">{(param.IsOptional ? "optional" : "required")}</span>"
        };

        if (!string.IsNullOrWhiteSpace(param.Position))
            chips.Add($"<span class=\"param-meta-chip\">position: {System.Web.HttpUtility.HtmlEncode(param.Position)}</span>");
        if (!string.IsNullOrWhiteSpace(param.PipelineInput))
            chips.Add($"<span class=\"param-meta-chip\">pipeline: {System.Web.HttpUtility.HtmlEncode(param.PipelineInput)}</span>");
        if (param.Aliases.Count > 0)
        {
            var aliases = string.Join(", ", param.Aliases
                .Where(alias => !string.IsNullOrWhiteSpace(alias))
                .Distinct(StringComparer.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(aliases))
                chips.Add($"<span class=\"param-meta-chip\">aliases: {System.Web.HttpUtility.HtmlEncode(aliases)}</span>");
        }
        if (param.PossibleValues.Count > 0)
        {
            var count = param.PossibleValues
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            if (count > 0)
                chips.Add($"<span class=\"param-meta-chip values\">values: {count}</span>");
        }

        return chips.Count == 0 ? string.Empty : $" <span class=\"param-meta\">{string.Join(string.Empty, chips)}</span>";
    }

    private static string BuildMemberSignatureHtml(string signature, string sectionLabel, string codeLanguage)
    {
        var encoded = System.Web.HttpUtility.HtmlEncode(signature);
        if (!string.Equals(sectionLabel, "Syntax", StringComparison.OrdinalIgnoreCase))
            return $"<code class=\"member-signature\">{encoded}</code>";

        var language = string.IsNullOrWhiteSpace(codeLanguage)
            ? "powershell"
            : codeLanguage.Trim().ToLowerInvariant();
        var languageClass = $"language-{language}";
        return $"<pre class=\"member-signature {languageClass}\"><code class=\"{languageClass}\">{encoded}</code></pre>";
    }

    private static string RenderPowerShellPossibleValues(IReadOnlyList<string> values)
    {
        if (values is null || values.Count == 0)
            return string.Empty;

        var rendered = values
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => $"<code>{System.Web.HttpUtility.HtmlEncode(value)}</code>")
            .ToList();

        return rendered.Count == 0 ? string.Empty : string.Join(", ", rendered);
    }

    private static string ResolvePowerShellCommonParametersUrl(string baseUrl, IReadOnlyDictionary<string, string> slugMap)
    {
        if (slugMap.TryGetValue("about_CommonParameters", out var slug))
            return BuildDocsTypeUrl(baseUrl, slug);

        return "https://learn.microsoft.com/powershell/module/microsoft.powershell.core/about/about_commonparameters";
    }

    private static string BuildMemberPrefix(ApiMemberModel member)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(member.Access)) parts.Add(member.Access);
        parts.AddRange(member.Modifiers);
        return parts.Count == 0 ? string.Empty : string.Join(" ", parts) + " ";
    }

    private static string BuildMemberId(string memberKind, ApiMemberModel member)
    {
        var baseName = $"{memberKind}-{member.Name}";
        if (member.Parameters.Count > 0)
        {
            var suffix = string.Join("-", member.Parameters.Select(p => NormalizeTypeName(p.Type)));
            if (!string.IsNullOrWhiteSpace(suffix))
                baseName = $"{baseName}-{suffix}";
        }
        return Slugify(baseName);
    }

    private static string BuildUniqueMemberId(string preferredId, ISet<string> usedIds)
    {
        var candidate = preferredId;
        var suffix = 2;
        while (!usedIds.Add(candidate))
        {
            candidate = $"{preferredId}-{suffix}";
            suffix++;
        }

        return candidate;
    }

    private static void AppendFreshnessBadge(StringBuilder sb, ApiFreshnessModel? freshness, string cssClass)
    {
        if (sb is null || freshness is null)
            return;

        var status = (freshness.Status ?? string.Empty).Trim().ToLowerInvariant();
        if (!status.Equals("new", StringComparison.Ordinal) &&
            !status.Equals("updated", StringComparison.Ordinal))
            return;

        var label = status.Equals("new", StringComparison.Ordinal) ? "New" : "Updated";
        var title = RenderFreshnessText(freshness);
        sb.AppendLine($"          <span class=\"freshness-badge {System.Web.HttpUtility.HtmlEncode(status)} {System.Web.HttpUtility.HtmlEncode(cssClass)}\" title=\"{System.Web.HttpUtility.HtmlAttributeEncode(title)}\">{System.Web.HttpUtility.HtmlEncode(label)}</span>");
    }

    private static void AppendFreshnessBadge(HtmlFragmentBuilder html, ApiFreshnessModel? freshness, string cssClass)
    {
        if (html is null)
            return;

        var badgeHtml = BuildFreshnessBadgeHtml(freshness, cssClass);
        if (!string.IsNullOrWhiteSpace(badgeHtml))
            html.Line(badgeHtml);
    }

    private static string RenderFreshnessText(ApiFreshnessModel freshness)
    {
        if (freshness is null)
            return string.Empty;

        var lastModified = freshness.LastModifiedUtc == default
            ? string.Empty
            : freshness.LastModifiedUtc.ToString("yyyy-MM-dd");
        var ageText = freshness.AgeDays <= 0 ? "today" : $"{freshness.AgeDays} day{(freshness.AgeDays == 1 ? string.Empty : "s")} ago";
        return string.IsNullOrWhiteSpace(lastModified)
            ? ageText
            : $"{lastModified} ({ageText})";
    }
}
