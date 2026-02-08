using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Diagnostics;
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
        string codeLanguage)
    {
        var sb = new StringBuilder();
        var inheritanceChain = BuildInheritanceChain(type, typeIndex);
        var derivedTypes = GetDerivedTypes(type, derivedMap);
        var toc = BuildTypeToc(type, inheritanceChain.Count > 0, derivedTypes.Count > 0);
        sb.AppendLine("    <article class=\"type-detail\">");
        var indexUrl = EnsureTrailingSlash(baseUrl);
        sb.AppendLine("      <nav class=\"breadcrumb\">");
        sb.AppendLine($"        <a href=\"{indexUrl}\">API Reference</a>");
        sb.AppendLine("        <span class=\"sep\">/</span>");
        sb.AppendLine($"        <span class=\"current\">{System.Web.HttpUtility.HtmlEncode(type.Name)}</span>");
        sb.AppendLine("      </nav>");

        sb.AppendLine("      <header class=\"type-header\" id=\"overview\">");
        var kindLabel = string.IsNullOrWhiteSpace(type.Kind) ? "Type" : type.Kind;
        sb.AppendLine("        <div class=\"type-title-row\">");
        sb.AppendLine($"          <span class=\"type-badge {NormalizeKind(type.Kind)}\">{System.Web.HttpUtility.HtmlEncode(kindLabel)}</span>");
        sb.AppendLine($"          <h1>{System.Web.HttpUtility.HtmlEncode(type.Name)}</h1>");
        sb.AppendLine("        </div>");
        var sourceAction = RenderTypeSourceAction(type.Source);
        if (!string.IsNullOrWhiteSpace(sourceAction))
        {
            sb.AppendLine("        <div class=\"type-actions\">");
            sb.AppendLine($"          {sourceAction}");
            sb.AppendLine("        </div>");
        }
        sb.AppendLine("      </header>");

        var flags = new List<string>();
        if (type.IsStatic) flags.Add("static");
        else
        {
            if (type.IsAbstract) flags.Add("abstract");
            if (type.IsSealed) flags.Add("sealed");
        }

        sb.AppendLine("      <div class=\"type-meta\">");
        sb.AppendLine("        <div class=\"type-meta-row\">");
        sb.AppendLine("          <span class=\"type-meta-label\">Namespace</span>");
        sb.AppendLine($"          <code>{System.Web.HttpUtility.HtmlEncode(type.Namespace)}</code>");
        sb.AppendLine("        </div>");
        if (!string.IsNullOrWhiteSpace(type.Assembly))
        {
            sb.AppendLine("        <div class=\"type-meta-row\">");
            sb.AppendLine("          <span class=\"type-meta-label\">Assembly</span>");
            sb.AppendLine($"          <code>{System.Web.HttpUtility.HtmlEncode(type.Assembly)}</code>");
            sb.AppendLine("        </div>");
        }
        if (type.Source is not null)
        {
            sb.AppendLine("        <div class=\"type-meta-row type-meta-source\">");
            sb.AppendLine("          <span class=\"type-meta-label\">Source</span>");
            sb.AppendLine($"          {RenderSourceLink(type.Source)}");
            sb.AppendLine("        </div>");
        }
        if (!string.IsNullOrWhiteSpace(type.BaseType))
        {
            sb.AppendLine("        <div class=\"type-meta-row type-meta-inheritance\">");
            sb.AppendLine("          <span class=\"type-meta-label\">Base</span>");
            sb.AppendLine($"          <code>{LinkifyType(type.BaseType, baseUrl, slugMap)}</code>");
            sb.AppendLine("        </div>");
        }
        if (type.Interfaces.Count > 0)
        {
            sb.AppendLine("        <div class=\"type-meta-row type-meta-interfaces\">");
            sb.AppendLine("          <span class=\"type-meta-label\">Implements</span>");
            sb.AppendLine("          <div class=\"type-meta-list\">");
            foreach (var iface in type.Interfaces.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine($"            <code>{LinkifyType(iface, baseUrl, slugMap)}</code>");
            }
            sb.AppendLine("          </div>");
            sb.AppendLine("        </div>");
        }
        if (flags.Count > 0)
        {
            sb.AppendLine("        <div class=\"type-meta-row type-meta-flags\">");
            sb.AppendLine("          <span class=\"type-meta-label\">Modifiers</span>");
            sb.AppendLine($"          <span class=\"type-meta-flags-list\">{System.Web.HttpUtility.HtmlEncode(string.Join(", ", flags))}</span>");
            sb.AppendLine("        </div>");
        }
        if (type.Attributes.Count > 0)
        {
            sb.AppendLine("        <div class=\"type-meta-row type-meta-attributes\">");
            sb.AppendLine("          <span class=\"type-meta-label\">Attributes</span>");
            sb.AppendLine("          <div class=\"type-meta-list\">");
            foreach (var attr in type.Attributes)
            {
                sb.AppendLine($"            <code>{System.Web.HttpUtility.HtmlEncode(attr)}</code>");
            }
            sb.AppendLine("          </div>");
            sb.AppendLine("        </div>");
        }
        sb.AppendLine("      </div>");

        if (toc.Count > 1)
        {
          sb.AppendLine("      <nav class=\"type-toc\">");
          sb.AppendLine("        <div class=\"type-toc-header\">");
          sb.AppendLine("          <span class=\"type-toc-title\">On this page</span>");
          sb.AppendLine("          <button class=\"type-toc-toggle\" type=\"button\" aria-label=\"Toggle table of contents\">");
          sb.AppendLine("            <svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">");
          sb.AppendLine("              <path d=\"M9 18l6-6-6-6\"/>");
          sb.AppendLine("            </svg>");
          sb.AppendLine("          </button>");
          sb.AppendLine("        </div>");
          sb.AppendLine("        <ul>");
            foreach (var entry in toc)
            {
                sb.AppendLine($"          <li><a href=\"#{entry.id}\">{System.Web.HttpUtility.HtmlEncode(entry.label)}</a></li>");
            }
            sb.AppendLine("        </ul>");
            sb.AppendLine("      </nav>");
        }

        if (!string.IsNullOrWhiteSpace(type.Summary))
            sb.AppendLine($"      <p class=\"type-summary\">{RenderLinkedText(type.Summary, baseUrl, slugMap)}</p>");
        if (inheritanceChain.Count > 0)
        {
            sb.AppendLine("      <section class=\"type-inheritance\" id=\"inheritance\">");
            sb.AppendLine("        <h2>Inheritance</h2>");
            sb.AppendLine("        <ul class=\"inheritance-list\">");
            foreach (var entry in inheritanceChain)
            {
                sb.AppendLine($"          <li>{LinkifyType(entry, baseUrl, slugMap)}</li>");
            }
            sb.AppendLine($"          <li class=\"inheritance-current\">{System.Web.HttpUtility.HtmlEncode(type.Name)}</li>");
            sb.AppendLine("        </ul>");
            sb.AppendLine("      </section>");
        }

        if (derivedTypes.Count > 0)
        {
            sb.AppendLine("      <section class=\"type-derived\" id=\"derived-types\">");
            sb.AppendLine("        <h2>Derived Types</h2>");
            sb.AppendLine("        <ul class=\"derived-list\">");
            foreach (var derived in derivedTypes)
            {
                sb.AppendLine($"          <li>{LinkifyType(derived.FullName, baseUrl, slugMap)}</li>");
            }
            sb.AppendLine("        </ul>");
            sb.AppendLine("      </section>");
        }

        if (!string.IsNullOrWhiteSpace(type.Remarks))
        {
            sb.AppendLine("      <section class=\"remarks\" id=\"remarks\">");
            sb.AppendLine("        <h2>Remarks</h2>");
            sb.AppendLine($"        <p>{RenderLinkedText(type.Remarks, baseUrl, slugMap)}</p>");
            sb.AppendLine("      </section>");
        }

        if (type.TypeParameters.Count > 0)
        {
            sb.AppendLine("      <section class=\"type-parameters\" id=\"type-parameters\">");
            sb.AppendLine("        <h2>Type Parameters</h2>");
            sb.AppendLine("        <dl class=\"typeparam-list\">");
            foreach (var tp in type.TypeParameters)
            {
                sb.AppendLine($"          <dt>{System.Web.HttpUtility.HtmlEncode(tp.Name)}</dt>");
                if (!string.IsNullOrWhiteSpace(tp.Summary))
                    sb.AppendLine($"          <dd>{RenderLinkedText(tp.Summary, baseUrl, slugMap)}</dd>");
            }
            sb.AppendLine("        </dl>");
            sb.AppendLine("      </section>");
        }

        if (type.Examples.Count > 0)
        {
            sb.AppendLine("      <section class=\"type-examples\" id=\"examples\">");
            sb.AppendLine("        <h2>Examples</h2>");
            AppendExamples(sb, type.Examples, baseUrl, slugMap, codeLanguage);
            sb.AppendLine("      </section>");
        }

        if (type.SeeAlso.Count > 0)
        {
            sb.AppendLine("      <section class=\"type-see-also\" id=\"see-also\">");
            sb.AppendLine("        <h2>See Also</h2>");
            sb.AppendLine("        <ul class=\"see-also-list\">");
            foreach (var item in type.SeeAlso)
            {
                sb.AppendLine($"          <li>{RenderLinkedText(item, baseUrl, slugMap)}</li>");
            }
            sb.AppendLine("        </ul>");
            sb.AppendLine("      </section>");
        }

          var totalMembers = type.Constructors.Count + type.Methods.Count + type.Properties.Count + type.Fields.Count + type.Events.Count + type.ExtensionMethods.Count;
          sb.AppendLine("      <div class=\"member-toolbar\" data-member-total=\"" + totalMembers + "\">");
          sb.AppendLine("        <div class=\"member-filter\">");
          sb.AppendLine("          <label for=\"api-member-filter\">Filter members</label>");
          sb.AppendLine("          <input id=\"api-member-filter\" type=\"text\" placeholder=\"Search members...\" />");
          sb.AppendLine("        </div>");
          sb.AppendLine("        <div class=\"member-kind-filter\">");
          sb.AppendLine($"          <button class=\"member-kind active\" type=\"button\" data-member-kind=\"\">All ({totalMembers})</button>");
          if (type.Constructors.Count > 0)
              sb.AppendLine($"          <button class=\"member-kind\" type=\"button\" data-member-kind=\"constructor\">Constructors ({type.Constructors.Count})</button>");
          sb.AppendLine($"          <button class=\"member-kind\" type=\"button\" data-member-kind=\"method\">Methods ({type.Methods.Count})</button>");
          sb.AppendLine($"          <button class=\"member-kind\" type=\"button\" data-member-kind=\"property\">Properties ({type.Properties.Count})</button>");
          sb.AppendLine($"          <button class=\"member-kind\" type=\"button\" data-member-kind=\"field\">{(type.Kind == "Enum" ? "Values" : "Fields")} ({type.Fields.Count})</button>");
          sb.AppendLine($"          <button class=\"member-kind\" type=\"button\" data-member-kind=\"event\">Events ({type.Events.Count})</button>");
          if (type.ExtensionMethods.Count > 0)
              sb.AppendLine($"          <button class=\"member-kind\" type=\"button\" data-member-kind=\"extension\">Extensions ({type.ExtensionMethods.Count})</button>");
        sb.AppendLine("        </div>");
          sb.AppendLine("        <label class=\"member-toggle\">");
          sb.AppendLine("          <input type=\"checkbox\" id=\"api-show-inherited\" />");
          sb.AppendLine("          Show inherited");
          sb.AppendLine("        </label>");
          sb.AppendLine("        <div class=\"member-actions\">");
          sb.AppendLine("          <button class=\"member-expand-all\" type=\"button\">Expand all</button>");
          sb.AppendLine("          <button class=\"member-collapse-all\" type=\"button\">Collapse all</button>");
          sb.AppendLine("          <button class=\"member-reset\" type=\"button\">Reset</button>");
          sb.AppendLine("        </div>");
          sb.AppendLine("      </div>");

        var usedMemberIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AppendMemberSections(sb, "Constructors", "constructor", type.Constructors, baseUrl, slugMap, codeLanguage, usedMemberIds, treatAsInherited: false, groupOverloads: true, sectionId: "constructors");
        AppendMemberSections(sb, "Methods", "method", type.Methods, baseUrl, slugMap, codeLanguage, usedMemberIds, groupOverloads: true, sectionId: "methods");
        AppendMemberSections(sb, "Properties", "property", type.Properties, baseUrl, slugMap, codeLanguage, usedMemberIds, sectionId: "properties");
        AppendMemberSections(sb, type.Kind == "Enum" ? "Values" : "Fields", "field", type.Fields, baseUrl, slugMap, codeLanguage, usedMemberIds, sectionId: type.Kind == "Enum" ? "values" : "fields");
        AppendMemberSections(sb, "Events", "event", type.Events, baseUrl, slugMap, codeLanguage, usedMemberIds, sectionId: "events");
        if (type.ExtensionMethods.Count > 0)
            AppendMemberSections(sb, "Extension Methods", "extension", type.ExtensionMethods, baseUrl, slugMap, codeLanguage, usedMemberIds, treatAsInherited: false, groupOverloads: true, sectionId: "extensions");

        sb.AppendLine("    </article>");
        return sb.ToString().TrimEnd();
    }

    private static void AppendMemberSections(
        StringBuilder sb,
        string label,
        string memberKind,
        List<ApiMemberModel> members,
        string baseUrl,
        IReadOnlyDictionary<string, string> slugMap,
        string codeLanguage,
        ISet<string> usedMemberIds,
        bool treatAsInherited = true,
        bool groupOverloads = false,
        string? sectionId = null)
    {
        if (members.Count == 0) return;
        var direct = members.Where(m => !m.IsInherited).ToList();
        var inherited = treatAsInherited ? members.Where(m => m.IsInherited).ToList() : new List<ApiMemberModel>();

        var directId = direct.Count > 0 ? sectionId : null;
        var inheritedId = direct.Count == 0 ? sectionId : null;
        if (direct.Count > 0)
            AppendMemberCards(sb, label, memberKind, direct, baseUrl, slugMap, codeLanguage, usedMemberIds, false, groupOverloads, directId);
        if (inherited.Count > 0)
            AppendMemberCards(sb, $"Inherited {label}", memberKind, inherited, baseUrl, slugMap, codeLanguage, usedMemberIds, true, groupOverloads, inheritedId);
    }

    private static void AppendMemberCards(
        StringBuilder sb,
        string label,
        string memberKind,
        List<ApiMemberModel> members,
        string baseUrl,
        IReadOnlyDictionary<string, string> slugMap,
        string codeLanguage,
        ISet<string> usedMemberIds,
        bool inheritedSection,
        bool groupOverloads,
        string? sectionId)
    {
        if (members.Count == 0) return;
        var collapsed = inheritedSection ? " collapsed" : string.Empty;
        var idAttribute = string.IsNullOrWhiteSpace(sectionId) ? string.Empty : $" id=\"{sectionId}\"";
        sb.AppendLine($"      <section class=\"member-section{collapsed}\" data-kind=\"{memberKind}\"{idAttribute}>");
        sb.AppendLine("        <div class=\"member-section-header\">");
        sb.AppendLine($"          <h2>{label}</h2>");
        sb.AppendLine("          <button class=\"member-section-toggle\" type=\"button\" aria-label=\"Toggle section\">");
        sb.AppendLine("            <svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">");
        sb.AppendLine("              <path d=\"M9 18l6-6-6-6\"/>");
        sb.AppendLine("            </svg>");
        sb.AppendLine("          </button>");
        sb.AppendLine("        </div>");
        var hidden = inheritedSection ? " hidden" : string.Empty;
        sb.AppendLine($"        <div class=\"member-section-body\"{hidden}>");
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
                    AppendMemberCard(sb, memberKind, group.First(), baseUrl, slugMap, codeLanguage, usedMemberIds, label);
                    continue;
                }
                sb.AppendLine("          <div class=\"member-group\">");
                sb.AppendLine("            <div class=\"member-group-header\">");
                sb.AppendLine($"              <span class=\"member-group-name\">{System.Web.HttpUtility.HtmlEncode(group.Key)}</span>");
                sb.AppendLine($"              <span class=\"member-group-count\">{group.Count()} overloads</span>");
                sb.AppendLine("            </div>");
                sb.AppendLine("            <div class=\"member-group-body\">");
                foreach (var member in group)
                {
                    AppendMemberCard(sb, memberKind, member, baseUrl, slugMap, codeLanguage, usedMemberIds, label);
                }
                sb.AppendLine("            </div>");
                sb.AppendLine("          </div>");
            }
        }
        else
        {
            foreach (var member in members)
            {
                AppendMemberCard(sb, memberKind, member, baseUrl, slugMap, codeLanguage, usedMemberIds, label);
            }
        }
        sb.AppendLine("        </div>");
        sb.AppendLine("      </section>");
    }

    private static void AppendMemberCard(
        StringBuilder sb,
        string memberKind,
        ApiMemberModel member,
        string baseUrl,
        IReadOnlyDictionary<string, string> slugMap,
        string codeLanguage,
        ISet<string> usedMemberIds,
        string sectionLabel)
    {
        var memberId = BuildUniqueMemberId(BuildMemberId(memberKind, member), usedMemberIds);
        var signature = !string.IsNullOrWhiteSpace(member.Signature)
            ? member.Signature
            : BuildSignature(member, sectionLabel);
        var search = $"{member.Name} {signature} {member.Summary}".Trim();
        var searchAttr = System.Web.HttpUtility.HtmlEncode(search);
        var inherited = member.IsInherited ? "true" : "false";
        var inheritedNote = member.IsInherited && !string.IsNullOrWhiteSpace(member.DeclaringType)
            ? $"Inherited from {member.DeclaringType}"
            : string.Empty;

        sb.AppendLine($"        <div class=\"member-card\" id=\"{memberId}\" data-kind=\"{memberKind}\" data-inherited=\"{inherited}\" data-search=\"{searchAttr}\">");
        sb.AppendLine("          <div class=\"member-header\">");
        sb.AppendLine($"            <code class=\"member-signature\">{System.Web.HttpUtility.HtmlEncode(signature)}</code>");
        sb.AppendLine($"            <a class=\"member-anchor\" href=\"#{memberId}\" aria-label=\"Link to {System.Web.HttpUtility.HtmlEncode(member.Name)}\">#</a>");
        sb.AppendLine("          </div>");
        if (member.Source is not null)
            sb.AppendLine($"          <div class=\"member-source\">{RenderSourceLink(member.Source)}</div>");
        if (!string.IsNullOrWhiteSpace(member.ReturnType) && (sectionLabel.Contains("Method") || memberKind == "extension"))
            sb.AppendLine($"          <div class=\"member-return\">Returns: <code>{System.Web.HttpUtility.HtmlEncode(member.ReturnType)}</code></div>");
        if (!string.IsNullOrWhiteSpace(inheritedNote))
        {
            var declaring = LinkifyType(member.DeclaringType, baseUrl, slugMap);
            sb.AppendLine($"          <div class=\"member-inherited\">Inherited from {declaring}</div>");
        }
        if (member.Attributes.Count > 0)
        {
            sb.AppendLine("          <div class=\"member-attributes\">");
            foreach (var attr in member.Attributes)
            {
                sb.AppendLine($"            <code>{System.Web.HttpUtility.HtmlEncode(attr)}</code>");
            }
            sb.AppendLine("          </div>");
        }
        if (!string.IsNullOrWhiteSpace(member.Summary))
            sb.AppendLine($"          <p class=\"member-summary\">{RenderLinkedText(member.Summary, baseUrl, slugMap)}</p>");
        if (member.TypeParameters.Count > 0)
        {
            sb.AppendLine("          <h4>Type Parameters</h4>");
            sb.AppendLine("          <dl class=\"typeparam-list\">");
            foreach (var tp in member.TypeParameters)
            {
                sb.AppendLine($"            <dt>{System.Web.HttpUtility.HtmlEncode(tp.Name)}</dt>");
                if (!string.IsNullOrWhiteSpace(tp.Summary))
                    sb.AppendLine($"            <dd>{RenderLinkedText(tp.Summary, baseUrl, slugMap)}</dd>");
            }
            sb.AppendLine("          </dl>");
        }
        if (member.Parameters.Count > 0)
        {
            sb.AppendLine("          <h4>Parameters</h4>");
            sb.AppendLine("          <dl class=\"param-list\">");
            foreach (var param in member.Parameters)
            {
                var optional = param.IsOptional ? " optional" : string.Empty;
                var defaultValue = param.DefaultValue;
                var defaultText = string.IsNullOrWhiteSpace(defaultValue) ? string.Empty : $" = {defaultValue}";
                sb.AppendLine($"            <dt>{System.Web.HttpUtility.HtmlEncode(param.Name)} <span class=\"param-type{optional}\">{System.Web.HttpUtility.HtmlEncode(param.Type)}</span><span class=\"param-default\">{System.Web.HttpUtility.HtmlEncode(defaultText)}</span></dt>");
                if (!string.IsNullOrWhiteSpace(param.Summary))
                    sb.AppendLine($"            <dd>{RenderLinkedText(param.Summary, baseUrl, slugMap)}</dd>");
            }
            sb.AppendLine("          </dl>");
        }
        if (!string.IsNullOrWhiteSpace(member.ValueSummary))
        {
            sb.AppendLine("          <h4>Value</h4>");
            sb.AppendLine($"          <p>{RenderLinkedText(member.ValueSummary, baseUrl, slugMap)}</p>");
        }
        if (sectionLabel == "Fields" || sectionLabel == "Values")
        {
            if (!string.IsNullOrWhiteSpace(member.Value))
                sb.AppendLine($"          <div class=\"member-value\">Value: <code>{System.Web.HttpUtility.HtmlEncode(member.Value)}</code></div>");
        }
        if (!string.IsNullOrWhiteSpace(member.Returns))
        {
            sb.AppendLine("          <h4>Returns</h4>");
            sb.AppendLine($"          <p>{RenderLinkedText(member.Returns, baseUrl, slugMap)}</p>");
        }
        if (member.Exceptions.Count > 0)
        {
            sb.AppendLine("          <h4>Exceptions</h4>");
            sb.AppendLine("          <ul class=\"exception-list\">");
            foreach (var ex in member.Exceptions)
            {
                var type = LinkifyType(ex.Type, baseUrl, slugMap);
                var desc = string.IsNullOrWhiteSpace(ex.Summary) ? string.Empty : $" – {RenderLinkedText(ex.Summary, baseUrl, slugMap)}";
                sb.AppendLine($"            <li><code>{type}</code>{desc}</li>");
            }
            sb.AppendLine("          </ul>");
        }
        if (member.Examples.Count > 0)
        {
            sb.AppendLine("          <h4>Examples</h4>");
            AppendExamples(sb, member.Examples, baseUrl, slugMap, codeLanguage);
        }
        if (member.SeeAlso.Count > 0)
        {
            sb.AppendLine("          <h4>See Also</h4>");
            sb.AppendLine("          <ul class=\"see-also-list\">");
            foreach (var item in member.SeeAlso)
            {
                sb.AppendLine($"            <li>{RenderLinkedText(item, baseUrl, slugMap)}</li>");
            }
            sb.AppendLine("          </ul>");
        }
        sb.AppendLine("        </div>");
    }

    private static void AppendExamples(
        StringBuilder sb,
        List<ApiExampleModel> examples,
        string baseUrl,
        IReadOnlyDictionary<string, string> slugMap,
        string codeLanguage)
    {
        foreach (var example in examples)
        {
            if (string.IsNullOrWhiteSpace(example.Text)) continue;
            if (string.Equals(example.Kind, "code", StringComparison.OrdinalIgnoreCase))
            {
                var languageClass = string.IsNullOrWhiteSpace(codeLanguage) ? string.Empty : $" class=\"language-{codeLanguage}\"";
                sb.AppendLine($"        <pre{languageClass}><code{languageClass}>");
                sb.AppendLine(System.Web.HttpUtility.HtmlEncode(example.Text));
                sb.AppendLine("        </code></pre>");
            }
            else
            {
                sb.AppendLine($"        <p>{RenderLinkedText(example.Text, baseUrl, slugMap)}</p>");
            }
        }
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

        if (!string.IsNullOrWhiteSpace(member.ReturnType))
        {
            if (section == "Events")
                return $"{prefix}event {member.ReturnType} {displayName}".Trim();
            return $"{prefix}{member.ReturnType} {displayName}".Trim();
        }

        return $"{prefix}{displayName}".Trim();
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
}
