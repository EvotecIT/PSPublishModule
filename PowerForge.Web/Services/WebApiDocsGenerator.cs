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

/// <summary>Options for API documentation generation.</summary>
public sealed class WebApiDocsOptions
{
    /// <summary>Documentation source type.</summary>
    public ApiDocsType Type { get; set; } = ApiDocsType.CSharp;
    /// <summary>Path to the XML documentation file.</summary>
    public string XmlPath { get; set; } = string.Empty;
    /// <summary>Path to PowerShell help XML or folder.</summary>
    public string? HelpPath { get; set; }
    /// <summary>Optional assembly path for version metadata.</summary>
    public string? AssemblyPath { get; set; }
    /// <summary>Output directory for generated docs.</summary>
    public string OutputPath { get; set; } = string.Empty;
    /// <summary>Documentation title.</summary>
    public string Title { get; set; } = "API Reference";
    /// <summary>Base URL for API documentation routes.</summary>
    public string BaseUrl { get; set; } = "/api";
      /// <summary>Optional docs home URL for the "Back to Docs" link.</summary>
      public string? DocsHomeUrl { get; set; }
      /// <summary>Sidebar position for docs template (left or right).</summary>
      public string? SidebarPosition { get; set; }
      /// <summary>Optional CSS class applied to the &lt;body&gt; element.</summary>
      public string? BodyClass { get; set; }
    /// <summary>Output format hint (json, html, hybrid, both).</summary>
    public string? Format { get; set; }
    /// <summary>Optional stylesheet href for HTML output.</summary>
    public string? CssHref { get; set; }
    /// <summary>Optional path to header HTML fragment.</summary>
    public string? HeaderHtmlPath { get; set; }
    /// <summary>Optional path to footer HTML fragment.</summary>
    public string? FooterHtmlPath { get; set; }
    /// <summary>Optional nav config path (site.json or site-nav.json) for header/footer tokens.</summary>
    public string? NavJsonPath { get; set; }
    /// <summary>Optional site display name override.</summary>
    public string? SiteName { get; set; }
    /// <summary>Optional brand URL override.</summary>
    public string? BrandUrl { get; set; }
    /// <summary>Optional brand icon URL override.</summary>
    public string? BrandIcon { get; set; }
    /// <summary>Optional HTML template name (simple, docs).</summary>
    public string? Template { get; set; }
    /// <summary>Optional root folder for API docs templates/assets overrides.</summary>
    public string? TemplateRootPath { get; set; }
    /// <summary>Optional override for index template.</summary>
    public string? IndexTemplatePath { get; set; }
    /// <summary>Optional override for type template.</summary>
    public string? TypeTemplatePath { get; set; }
    /// <summary>Optional override for docs index template.</summary>
    public string? DocsIndexTemplatePath { get; set; }
    /// <summary>Optional override for docs type template.</summary>
    public string? DocsTypeTemplatePath { get; set; }
    /// <summary>Optional override for docs script.</summary>
    public string? DocsScriptPath { get; set; }
    /// <summary>Optional override for search script.</summary>
    public string? SearchScriptPath { get; set; }
    /// <summary>Optional root path for source link generation.</summary>
    public string? SourceRootPath { get; set; }
    /// <summary>Optional source URL pattern (use {path} and {line}).</summary>
    public string? SourceUrlPattern { get; set; }
    /// <summary>Include undocumented public types when XML docs are partial.</summary>
    public bool IncludeUndocumentedTypes { get; set; } = true;
    /// <summary>Optional list of namespace prefixes to include.</summary>
    public List<string> IncludeNamespacePrefixes { get; } = new();
    /// <summary>Optional list of namespace prefixes to exclude.</summary>
    public List<string> ExcludeNamespacePrefixes { get; } = new();
    /// <summary>Optional list of type full names to include.</summary>
    public List<string> IncludeTypeNames { get; } = new();
    /// <summary>Optional list of type full names to exclude.</summary>
    public List<string> ExcludeTypeNames { get; } = new();
}

/// <summary>Generates API documentation artifacts from XML docs.</summary>
public static class WebApiDocsGenerator
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex GenericArityRegex = new("`{1,2}\\d+", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex WhitespaceRegex = new("\\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex SlugNonAlnumRegex = new("[^a-z0-9]+", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex SlugDashRegex = new("-{2,}", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex CrefTokenRegex = new("\\[\\[cref:(?<name>[^\\]]+)\\]\\]", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex HrefTokenRegex = new("\\[\\[href:(?<url>[^|\\]]+)\\|(?<label>[^\\]]*)\\]\\]", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    /// <summary>Generates API documentation output.</summary>
    /// <param name="options">Generation options.</param>
    /// <returns>Result payload describing generated artifacts.</returns>
    public static WebApiDocsResult Generate(WebApiDocsOptions options)
    {
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (options.Type == ApiDocsType.CSharp && string.IsNullOrWhiteSpace(options.XmlPath))
            throw new ArgumentException("XmlPath is required for CSharp API docs.", nameof(options));
        if (options.Type == ApiDocsType.PowerShell && string.IsNullOrWhiteSpace(options.HelpPath))
            throw new ArgumentException("HelpPath is required for PowerShell API docs.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.OutputPath))
            throw new ArgumentException("OutputPath is required.", nameof(options));

        var xmlPath = options.Type == ApiDocsType.CSharp
            ? Path.GetFullPath(options.XmlPath)
            : string.Empty;
        var helpPath = options.Type == ApiDocsType.PowerShell && !string.IsNullOrWhiteSpace(options.HelpPath)
            ? Path.GetFullPath(options.HelpPath)
            : string.Empty;
        var outputPath = Path.GetFullPath(options.OutputPath);
        Directory.CreateDirectory(outputPath);

        var warnings = new List<string>();
        if (options.Type == ApiDocsType.CSharp && !File.Exists(xmlPath))
            warnings.Add($"XML docs not found: {xmlPath}");
        if (options.Type == ApiDocsType.PowerShell && !File.Exists(helpPath) && !Directory.Exists(helpPath))
            warnings.Add($"PowerShell help not found: {helpPath}");

        var template = (options.Template ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(options.NavJsonPath) &&
            string.IsNullOrWhiteSpace(options.HeaderHtmlPath) &&
            string.IsNullOrWhiteSpace(options.FooterHtmlPath) &&
            (template.Equals("docs", StringComparison.OrdinalIgnoreCase) ||
             template.Equals("sidebar", StringComparison.OrdinalIgnoreCase)))
        {
            warnings.Add("NavJsonPath is set but HeaderHtmlPath/FooterHtmlPath are not set. " +
                         "The docs template will render without site header/footer navigation unless you provide API header/footer fragments.");
        }

        Assembly? assembly = null;
        if (options.Type == ApiDocsType.CSharp && !string.IsNullOrWhiteSpace(options.AssemblyPath) && File.Exists(options.AssemblyPath))
        {
            assembly = TryLoadAssembly(Path.GetFullPath(options.AssemblyPath), warnings);
        }
        else if (options.Type == ApiDocsType.CSharp && !string.IsNullOrWhiteSpace(options.AssemblyPath))
        {
            warnings.Add($"Assembly not found: {options.AssemblyPath}");
        }

        var apiDoc = options.Type == ApiDocsType.PowerShell
            ? ParsePowerShellHelp(helpPath, warnings)
            : ParseXml(xmlPath, assembly, options);
        var usedReflectionFallback = false;
        if (options.Type == ApiDocsType.CSharp && assembly is not null && options.IncludeUndocumentedTypes)
        {
            var beforeCount = apiDoc.Types.Count;
            PopulateFromAssembly(apiDoc, assembly);
            usedReflectionFallback = apiDoc.Types.Count > beforeCount;
            if (apiDoc.Types.Count == 0)
                warnings.Add("Reflection fallback produced 0 public types.");
        }
        else if (options.Type == ApiDocsType.CSharp && apiDoc.Types.Count == 0 && assembly is null && !string.IsNullOrWhiteSpace(options.AssemblyPath))
        {
            warnings.Add("Reflection fallback unavailable (assembly could not be loaded).");
        }

        if (options.Type == ApiDocsType.CSharp && assembly is not null)
        {
            EnrichFromAssembly(apiDoc, assembly, options, warnings);
        }
        var assemblyName = apiDoc.AssemblyName;
        var assemblyVersion = apiDoc.AssemblyVersion;

        if (options.Type == ApiDocsType.CSharp && !string.IsNullOrWhiteSpace(options.AssemblyPath) && File.Exists(options.AssemblyPath))
        {
            try
            {
                var assemblyNameInfo = System.Reflection.AssemblyName.GetAssemblyName(options.AssemblyPath);
                if (!string.IsNullOrWhiteSpace(assemblyNameInfo.Name))
                    assemblyName = assemblyNameInfo.Name;
                if (assemblyNameInfo.Version is not null)
                    assemblyVersion = assemblyNameInfo.Version.ToString();
            }
            catch (Exception ex)
            {
                warnings.Add($"Assembly inspection failed: {Path.GetFileName(options.AssemblyPath)} ({ex.GetType().Name}: {ex.Message})");
                Trace.TraceWarning($"Assembly inspection failed: {options.AssemblyPath} ({ex.GetType().Name}: {ex.Message})");
            }
        }

        var types = apiDoc.Types.Values
            .Where(t => ShouldIncludeType(t, options))
            .OrderBy(t => t.FullName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var index = new Dictionary<string, object?>
        {
            ["title"] = options.Title,
            ["generatedAtUtc"] = DateTime.UtcNow.ToString("O"),
            ["assembly"] = new Dictionary<string, object?>
            {
                ["assemblyName"] = assemblyName ?? string.Empty,
                ["assemblyVersion"] = assemblyVersion
            },
            ["typeCount"] = types.Count,
            ["types"] = types.Select(t => new Dictionary<string, object?>
            {
                ["name"] = t.Name,
                ["fullName"] = t.FullName,
                ["namespace"] = t.Namespace,
                ["kind"] = t.Kind,
                ["slug"] = t.Slug,
                ["summary"] = t.Summary,
                ["typeParameters"] = t.TypeParameters.Select(tp => new Dictionary<string, object?>
                {
                    ["name"] = tp.Name,
                    ["summary"] = tp.Summary
                }).ToList()
            }).ToList()
        };

        var indexPath = Path.Combine(outputPath, "index.json");
        WriteJson(indexPath, index);

        var search = types.Select(t => new Dictionary<string, object?>
        {
            ["title"] = t.FullName,
            ["summary"] = t.Summary ?? string.Empty,
            ["kind"] = t.Kind,
            ["namespace"] = t.Namespace,
            ["slug"] = t.Slug,
            ["url"] = $"{options.BaseUrl.TrimEnd('/')}/types/{t.Slug}.json"
        }).ToList();

        var searchPath = Path.Combine(outputPath, "search.json");
        WriteJson(searchPath, search);

        var typesDir = Path.Combine(outputPath, "types");
        Directory.CreateDirectory(typesDir);
        foreach (var type in types)
        {
            var typeModel = new Dictionary<string, object?>
            {
                ["name"] = type.Name,
                ["fullName"] = type.FullName,
                ["namespace"] = type.Namespace,
                ["assembly"] = type.Assembly,
                ["source"] = BuildSourceJson(type.Source),
                ["baseType"] = type.BaseType,
                ["interfaces"] = type.Interfaces,
                ["attributes"] = type.Attributes,
                ["kind"] = type.Kind,
                ["slug"] = type.Slug,
                ["isStatic"] = type.IsStatic,
                ["isAbstract"] = type.IsAbstract,
                ["isSealed"] = type.IsSealed,
                ["summary"] = type.Summary,
                ["remarks"] = type.Remarks,
                ["typeParameters"] = type.TypeParameters.Select(tp => new Dictionary<string, object?>
                {
                    ["name"] = tp.Name,
                    ["summary"] = tp.Summary
                }).ToList(),
                ["examples"] = type.Examples.Select(ex => new Dictionary<string, object?>
                {
                    ["kind"] = ex.Kind,
                    ["text"] = ex.Text
                }).ToList(),
                ["seeAlso"] = type.SeeAlso,
                ["methods"] = type.Methods.Select(m => new Dictionary<string, object?>
                {
                    ["name"] = m.Name,
                    ["displayName"] = m.DisplayName,
                    ["summary"] = m.Summary,
                    ["kind"] = m.Kind,
                    ["signature"] = m.Signature,
                    ["returnType"] = m.ReturnType,
                    ["declaringType"] = m.DeclaringType,
                    ["source"] = BuildSourceJson(m.Source),
                    ["isInherited"] = m.IsInherited,
                    ["isStatic"] = m.IsStatic,
                    ["access"] = m.Access,
                    ["modifiers"] = m.Modifiers,
                    ["isConstructor"] = m.IsConstructor,
                    ["isExtension"] = m.IsExtension,
                    ["attributes"] = m.Attributes,
                    ["returns"] = m.Returns,
                    ["value"] = m.Value,
                    ["valueSummary"] = m.ValueSummary,
                    ["typeParameters"] = m.TypeParameters.Select(tp => new Dictionary<string, object?>
                    {
                        ["name"] = tp.Name,
                        ["summary"] = tp.Summary
                    }).ToList(),
                    ["examples"] = m.Examples.Select(ex => new Dictionary<string, object?>
                    {
                        ["kind"] = ex.Kind,
                        ["text"] = ex.Text
                    }).ToList(),
                    ["exceptions"] = m.Exceptions.Select(ex => new Dictionary<string, object?>
                    {
                        ["type"] = ex.Type,
                        ["summary"] = ex.Summary
                    }).ToList(),
                    ["seeAlso"] = m.SeeAlso,
                    ["parameters"] = m.Parameters.Select(p => new Dictionary<string, object?>
                    {
                        ["name"] = p.Name,
                        ["type"] = p.Type,
                        ["summary"] = p.Summary,
                        ["isOptional"] = p.IsOptional,
                        ["defaultValue"] = p.DefaultValue
                    }).ToList()
                }).ToList(),
                ["constructors"] = type.Constructors.Select(m => new Dictionary<string, object?>
                {
                    ["name"] = m.Name,
                    ["displayName"] = m.DisplayName,
                    ["summary"] = m.Summary,
                    ["kind"] = m.Kind,
                    ["signature"] = m.Signature,
                    ["returnType"] = m.ReturnType,
                    ["declaringType"] = m.DeclaringType,
                    ["source"] = BuildSourceJson(m.Source),
                    ["isInherited"] = m.IsInherited,
                    ["isStatic"] = m.IsStatic,
                    ["access"] = m.Access,
                    ["modifiers"] = m.Modifiers,
                    ["isConstructor"] = m.IsConstructor,
                    ["attributes"] = m.Attributes,
                    ["returns"] = m.Returns,
                    ["value"] = m.Value,
                    ["valueSummary"] = m.ValueSummary,
                    ["typeParameters"] = m.TypeParameters.Select(tp => new Dictionary<string, object?>
                    {
                        ["name"] = tp.Name,
                        ["summary"] = tp.Summary
                    }).ToList(),
                    ["examples"] = m.Examples.Select(ex => new Dictionary<string, object?>
                    {
                        ["kind"] = ex.Kind,
                        ["text"] = ex.Text
                    }).ToList(),
                    ["exceptions"] = m.Exceptions.Select(ex => new Dictionary<string, object?>
                    {
                        ["type"] = ex.Type,
                        ["summary"] = ex.Summary
                    }).ToList(),
                    ["seeAlso"] = m.SeeAlso,
                    ["parameters"] = m.Parameters.Select(p => new Dictionary<string, object?>
                    {
                        ["name"] = p.Name,
                        ["type"] = p.Type,
                        ["summary"] = p.Summary,
                        ["isOptional"] = p.IsOptional,
                        ["defaultValue"] = p.DefaultValue
                    }).ToList()
                }).ToList(),
                ["properties"] = type.Properties.Select(p => new Dictionary<string, object?>
                {
                    ["name"] = p.Name,
                    ["displayName"] = p.DisplayName,
                    ["summary"] = p.Summary,
                    ["kind"] = p.Kind,
                    ["signature"] = p.Signature,
                    ["returnType"] = p.ReturnType,
                    ["declaringType"] = p.DeclaringType,
                    ["source"] = BuildSourceJson(p.Source),
                    ["isInherited"] = p.IsInherited,
                    ["isStatic"] = p.IsStatic,
                    ["access"] = p.Access,
                    ["modifiers"] = p.Modifiers,
                    ["valueSummary"] = p.ValueSummary,
                    ["examples"] = p.Examples.Select(ex => new Dictionary<string, object?>
                    {
                        ["kind"] = ex.Kind,
                        ["text"] = ex.Text
                    }).ToList(),
                    ["exceptions"] = p.Exceptions.Select(ex => new Dictionary<string, object?>
                    {
                        ["type"] = ex.Type,
                        ["summary"] = ex.Summary
                    }).ToList(),
                    ["seeAlso"] = p.SeeAlso
                }).ToList(),
                ["fields"] = type.Fields.Select(f => new Dictionary<string, object?>
                {
                    ["name"] = f.Name,
                    ["displayName"] = f.DisplayName,
                    ["summary"] = f.Summary,
                    ["kind"] = f.Kind,
                    ["signature"] = f.Signature,
                    ["returnType"] = f.ReturnType,
                    ["declaringType"] = f.DeclaringType,
                    ["source"] = BuildSourceJson(f.Source),
                    ["isInherited"] = f.IsInherited,
                    ["isStatic"] = f.IsStatic,
                    ["access"] = f.Access,
                    ["modifiers"] = f.Modifiers,
                    ["value"] = f.Value,
                    ["valueSummary"] = f.ValueSummary,
                    ["examples"] = f.Examples.Select(ex => new Dictionary<string, object?>
                    {
                        ["kind"] = ex.Kind,
                        ["text"] = ex.Text
                    }).ToList(),
                    ["exceptions"] = f.Exceptions.Select(ex => new Dictionary<string, object?>
                    {
                        ["type"] = ex.Type,
                        ["summary"] = ex.Summary
                    }).ToList(),
                    ["seeAlso"] = f.SeeAlso
                }).ToList(),
                ["events"] = type.Events.Select(e => new Dictionary<string, object?>
                {
                    ["name"] = e.Name,
                    ["displayName"] = e.DisplayName,
                    ["summary"] = e.Summary,
                    ["kind"] = e.Kind,
                    ["signature"] = e.Signature,
                    ["returnType"] = e.ReturnType,
                    ["declaringType"] = e.DeclaringType,
                    ["source"] = BuildSourceJson(e.Source),
                    ["isInherited"] = e.IsInherited,
                    ["isStatic"] = e.IsStatic,
                    ["access"] = e.Access,
                    ["modifiers"] = e.Modifiers,
                    ["examples"] = e.Examples.Select(ex => new Dictionary<string, object?>
                    {
                        ["kind"] = ex.Kind,
                        ["text"] = ex.Text
                    }).ToList(),
                    ["exceptions"] = e.Exceptions.Select(ex => new Dictionary<string, object?>
                    {
                        ["type"] = ex.Type,
                        ["summary"] = ex.Summary
                    }).ToList(),
                    ["seeAlso"] = e.SeeAlso
                }).ToList(),
                ["extensionMethods"] = type.ExtensionMethods.Select(m => new Dictionary<string, object?>
                {
                    ["name"] = m.Name,
                    ["displayName"] = m.DisplayName,
                    ["summary"] = m.Summary,
                    ["kind"] = m.Kind,
                    ["signature"] = m.Signature,
                    ["returnType"] = m.ReturnType,
                    ["declaringType"] = m.DeclaringType,
                    ["source"] = BuildSourceJson(m.Source),
                    ["isInherited"] = m.IsInherited,
                    ["isStatic"] = m.IsStatic,
                    ["access"] = m.Access,
                    ["modifiers"] = m.Modifiers,
                    ["isConstructor"] = m.IsConstructor,
                    ["isExtension"] = m.IsExtension,
                    ["attributes"] = m.Attributes,
                    ["returns"] = m.Returns,
                    ["value"] = m.Value,
                    ["valueSummary"] = m.ValueSummary,
                    ["typeParameters"] = m.TypeParameters.Select(tp => new Dictionary<string, object?>
                    {
                        ["name"] = tp.Name,
                        ["summary"] = tp.Summary
                    }).ToList(),
                    ["examples"] = m.Examples.Select(ex => new Dictionary<string, object?>
                    {
                        ["kind"] = ex.Kind,
                        ["text"] = ex.Text
                    }).ToList(),
                    ["exceptions"] = m.Exceptions.Select(ex => new Dictionary<string, object?>
                    {
                        ["type"] = ex.Type,
                        ["summary"] = ex.Summary
                    }).ToList(),
                    ["seeAlso"] = m.SeeAlso,
                    ["parameters"] = m.Parameters.Select(p => new Dictionary<string, object?>
                    {
                        ["name"] = p.Name,
                        ["type"] = p.Type,
                        ["summary"] = p.Summary,
                        ["isOptional"] = p.IsOptional,
                        ["defaultValue"] = p.DefaultValue
                    }).ToList()
                }).ToList()
            };

            var typePath = Path.Combine(typesDir, $"{type.Slug}.json");
            WriteJson(typePath, typeModel);
        }

        var format = (options.Format ?? "json").Trim().ToLowerInvariant();
        if (format is "hybrid" or "html" or "both")
        {
            GenerateHtml(outputPath, options, types);
        }

        return new WebApiDocsResult
        {
            OutputPath = outputPath,
            IndexPath = indexPath,
            SearchPath = searchPath,
            TypesPath = typesDir,
            TypeCount = types.Count,
            UsedReflectionFallback = usedReflectionFallback,
            Warnings = warnings.ToArray()
        };
    }

    private static ApiDocModel ParseXml(string xmlPath, Assembly? assembly, WebApiDocsOptions options)
    {
        var apiDoc = new ApiDocModel();
        if (!File.Exists(xmlPath))
            return apiDoc;

        using var stream = File.OpenRead(xmlPath);
        XDocument doc;
        try
        {
            doc = LoadXmlSafe(stream);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Failed to parse XML docs: {xmlPath} ({ex.GetType().Name}: {ex.Message})");
            return apiDoc;
        }
        var docElement = doc.Element("doc");
        if (docElement is null) return apiDoc;

        var assemblyElement = docElement.Element("assembly");
        if (assemblyElement is not null)
        {
            apiDoc.AssemblyName = assemblyElement.Element("name")?.Value ?? string.Empty;
        }

        var members = docElement.Element("members");
        if (members is null) return apiDoc;

        var memberLookup = new Dictionary<string, XElement>(StringComparer.Ordinal);
        foreach (var member in members.Elements("member"))
        {
            var memberName = member.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(memberName))
                continue;
            if (!memberLookup.ContainsKey(memberName))
                memberLookup[memberName] = member;
        }

        foreach (var member in members.Elements("member"))
        {
            var name = member.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(name) || name.Length < 2) continue;

            var prefix = name[0];
            var fullName = name.Substring(2);

            switch (prefix)
            {
                case 'T':
                    var type = ParseType(member, fullName, name, memberLookup);
                    apiDoc.Types[type.FullName] = type;
                    break;
                case 'M':
                    AddMethod(apiDoc, member, fullName, name, assembly, memberLookup);
                    break;
                case 'P':
                    AddProperty(apiDoc, member, fullName, name, memberLookup);
                    break;
                case 'F':
                    AddField(apiDoc, member, fullName, name, memberLookup);
                    break;
                case 'E':
                    AddEvent(apiDoc, member, fullName, name, memberLookup);
                    break;
            }
        }

        return apiDoc;
    }

    private static ApiDocModel ParsePowerShellHelp(string helpPath, List<string> warnings)
    {
        var apiDoc = new ApiDocModel();
        if (string.IsNullOrWhiteSpace(helpPath))
            return apiDoc;

        var resolved = ResolvePowerShellHelpFile(helpPath, warnings);
        if (string.IsNullOrWhiteSpace(resolved) || !File.Exists(resolved))
            return apiDoc;

        var moduleName = Path.GetFileNameWithoutExtension(resolved) ?? string.Empty;
        if (moduleName.EndsWith("-help", StringComparison.OrdinalIgnoreCase))
            moduleName = moduleName[..^5];
        apiDoc.AssemblyName = moduleName;

        XDocument doc;
        try
        {
            doc = LoadXmlSafe(resolved);
        }
        catch (Exception ex)
        {
            warnings.Add($"Failed to parse PowerShell help: {Path.GetFileName(resolved)} ({ex.GetType().Name}: {ex.Message})");
            return apiDoc;
        }

        var commandNs = XNamespace.Get("http://schemas.microsoft.com/maml/dev/command/2004/10");
        var mamlNs = XNamespace.Get("http://schemas.microsoft.com/maml/2004/10");
        var devNs = XNamespace.Get("http://schemas.microsoft.com/maml/dev/2004/10");

        foreach (var command in doc.Descendants(commandNs + "command"))
        {
            var details = command.Element(commandNs + "details");
            var name = details?.Element(commandNs + "name")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                name = command.Element(mamlNs + "name")?.Value?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var summary = GetFirstParagraph(details?.Element(mamlNs + "description"), mamlNs);
            var remarks = JoinParagraphs(command.Element(mamlNs + "description"), mamlNs);
            var returns = JoinReturnValues(command, commandNs, mamlNs);

            var type = new ApiTypeModel
            {
                Name = name!,
                FullName = name!,
                Namespace = moduleName ?? string.Empty,
                Kind = "Cmdlet",
                Slug = Slugify(name!),
                Summary = summary,
                Remarks = remarks
            };

            var syntax = command.Element(commandNs + "syntax");
            if (syntax is not null)
            {
                foreach (var syntaxItem in syntax.Elements(commandNs + "syntaxItem"))
                {
                    var member = new ApiMemberModel
                    {
                        Name = name!,
                        Returns = returns,
                        Kind = "Method"
                    };
                    foreach (var parameter in syntaxItem.Elements(commandNs + "parameter"))
                    {
                        var paramName = parameter.Element(mamlNs + "name")?.Value?.Trim() ?? string.Empty;
                        var paramSummary = JoinParagraphs(parameter.Element(mamlNs + "description"), mamlNs);
                        var paramType = parameter.Element(commandNs + "parameterValue")?.Value?.Trim();
                        if (string.IsNullOrWhiteSpace(paramType))
                            paramType = parameter.Element(devNs + "type")?.Element(mamlNs + "name")?.Value?.Trim();

                        member.Parameters.Add(new ApiParameterModel
                        {
                            Name = paramName,
                            Type = paramType,
                            Summary = paramSummary
                        });
                    }
                    type.Methods.Add(member);
                }
            }

            apiDoc.Types[type.FullName] = type;
        }

        return apiDoc;
    }

    private static XDocument LoadXmlSafe(string path)
    {
        using var stream = File.OpenRead(path);
        return LoadXmlSafe(stream);
    }

    private static XDocument LoadXmlSafe(Stream stream)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            CloseInput = false
        };
        using var reader = XmlReader.Create(stream, settings);
        return XDocument.Load(reader, LoadOptions.None);
    }

    private static string? ResolvePowerShellHelpFile(string helpPath, List<string> warnings)
    {
        if (File.Exists(helpPath))
            return helpPath;

        if (!Directory.Exists(helpPath))
            return null;

        var primary = Directory.GetFiles(helpPath, "*-help.xml", SearchOption.AllDirectories)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var secondary = primary.Count == 0
            ? Directory.GetFiles(helpPath, "*help.xml", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList()
            : new List<string>();

        var candidates = primary.Count > 0 ? primary : secondary;
        if (candidates.Count == 0)
            return null;

        if (candidates.Count > 1)
            warnings.Add($"Multiple PowerShell help files found, using {Path.GetFileName(candidates[0])}");

        return candidates[0];
    }

    private static string? GetFirstParagraph(XElement? parent, XNamespace mamlNs)
    {
        if (parent is null) return null;
        return parent.Elements(mamlNs + "para")
            .Select(p => p.Value.Trim())
            .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
    }

    private static string? JoinParagraphs(XElement? parent, XNamespace mamlNs)
    {
        if (parent is null) return null;
        var parts = parent.Elements(mamlNs + "para")
            .Select(p => p.Value.Trim())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .ToList();
        return parts.Count == 0 ? null : string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    private static string? JoinReturnValues(XElement command, XNamespace commandNs, XNamespace mamlNs)
    {
        var values = command.Element(commandNs + "returnValues");
        if (values is null) return null;
        var parts = values.Elements(commandNs + "returnValue")
            .Select(rv => JoinParagraphs(rv.Element(mamlNs + "description"), mamlNs))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!)
            .ToList();
        return parts.Count == 0 ? null : string.Join(Environment.NewLine + Environment.NewLine, parts);
    }

    private static void PopulateFromAssembly(ApiDocModel doc, Assembly assembly)
    {
        foreach (var type in GetExportedTypesSafe(assembly))
        {
            if (type is null) continue;
            var rawFullName = type.FullName ?? type.Name;
            if (string.IsNullOrWhiteSpace(rawFullName)) continue;
            var fullName = rawFullName.Replace('+', '.');
            if (doc.Types.ContainsKey(fullName)) continue;

            var model = new ApiTypeModel
            {
                Name = StripGenericArity(type.Name),
                FullName = fullName,
                Namespace = type.Namespace ?? string.Empty,
                Kind = GetTypeKind(type),
                Slug = Slugify(fullName)
            };
            if (type.IsGenericTypeDefinition || type.ContainsGenericParameters)
            {
                foreach (var arg in type.GetGenericArguments())
                {
                    if (!string.IsNullOrWhiteSpace(arg.Name))
                        model.TypeParameters.Add(new ApiTypeParameterModel { Name = arg.Name });
                }
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (method.IsSpecialName) continue;
                model.Methods.Add(new ApiMemberModel
                {
                    Name = method.Name,
                    DisplayName = method.Name,
                    Parameters = method.GetParameters().Select(p => new ApiParameterModel
                    {
                        Name = p.Name ?? string.Empty,
                        Type = GetReadableTypeName(p.ParameterType)
                    }).ToList()
                });
            }

            foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                model.Constructors.Add(new ApiMemberModel
                {
                    Name = "#ctor",
                    DisplayName = model.Name,
                    Kind = "Constructor",
                    IsConstructor = true,
                    Parameters = ctor.GetParameters().Select(p => new ApiParameterModel
                    {
                        Name = p.Name ?? string.Empty,
                        Type = GetReadableTypeName(p.ParameterType)
                    }).ToList()
                });
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                model.Properties.Add(new ApiMemberModel
                {
                    Name = property.Name,
                    DisplayName = property.Name
                });
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (field.IsSpecialName) continue;
                model.Fields.Add(new ApiMemberModel
                {
                    Name = field.Name,
                    DisplayName = field.Name
                });
            }

            foreach (var evt in type.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                model.Events.Add(new ApiMemberModel
                {
                    Name = evt.Name,
                    DisplayName = evt.Name
                });
            }

            doc.Types[fullName] = model;
        }
    }

    private static void EnrichFromAssembly(ApiDocModel doc, Assembly assembly, WebApiDocsOptions options, List<string> warnings)
    {
        using var sourceLinks = SourceLinkContext.Create(options, assembly, warnings);
        var extensionTargets = new Dictionary<string, List<ApiMemberModel>>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in GetExportedTypesSafe(assembly))
        {
            if (type is null) continue;
            var rawFullName = type.FullName ?? type.Name;
            if (string.IsNullOrWhiteSpace(rawFullName)) continue;
            var fullName = rawFullName.Replace('+', '.');
            if (!doc.Types.TryGetValue(fullName, out var model)) continue;

            model.Kind = GetTypeKind(type);
            model.Assembly = type.Assembly.GetName().Name;
            model.IsAbstract = type.IsAbstract;
            model.IsSealed = type.IsSealed;
            model.IsStatic = type.IsAbstract && type.IsSealed;
            model.Attributes.Clear();
            model.Attributes.AddRange(GetAttributeList(type));
            if (sourceLinks is not null)
                model.Source = sourceLinks.TryGetSource(type);
            model.BaseType = type.BaseType != null && type.BaseType != typeof(object)
                ? GetReadableTypeName(type.BaseType)
                : null;
            model.Interfaces.Clear();
            foreach (var iface in type.GetInterfaces())
            {
                model.Interfaces.Add(GetReadableTypeName(iface));
            }
            if (type.IsGenericTypeDefinition || type.ContainsGenericParameters)
            {
                MergeTypeParameters(model.TypeParameters, type.GetGenericArguments().Select(a => a.Name));
            }

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (method.IsSpecialName) continue;
                var member = FindMethodModel(model.Methods, method);
                if (member is null)
                {
                    member = new ApiMemberModel
                    {
                        Name = method.Name,
                        Kind = "Method"
                    };
                    model.Methods.Add(member);
                }
                FillMethodMember(member, method, type);
                member.Attributes.Clear();
                member.Attributes.AddRange(GetAttributeList(method));
                member.IsExtension = IsExtensionMethod(method);
                if (string.IsNullOrWhiteSpace(member.DisplayName))
                    member.DisplayName = member.Name;
                if (sourceLinks is not null)
                    member.Source = sourceLinks.TryGetSource(method);

                if (member.IsExtension)
                {
                    var targetType = method.GetParameters().FirstOrDefault()?.ParameterType;
                    var targetName = targetType?.FullName?.Replace('+', '.');
                    if (!string.IsNullOrWhiteSpace(targetName))
                    {
                        if (!extensionTargets.TryGetValue(targetName, out var list))
                        {
                            list = new List<ApiMemberModel>();
                            extensionTargets[targetName] = list;
                        }
                        list.Add(CloneMember(member, isExtension: true));
                    }
                }
            }

            foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                var member = FindConstructorModel(model.Constructors, ctor);
                if (member is null)
                {
                    member = new ApiMemberModel
                    {
                        Name = "#ctor",
                        Kind = "Constructor",
                        IsConstructor = true,
                        DisplayName = model.Name
                    };
                    model.Constructors.Add(member);
                }
                FillConstructorMember(member, ctor, type);
                member.Attributes.Clear();
                member.Attributes.AddRange(GetAttributeList(ctor));
                if (sourceLinks is not null)
                    member.Source = sourceLinks.TryGetSource(ctor);
            }

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                var member = FindNamedMember(model.Properties, property.Name);
                if (member is null)
                {
                    member = new ApiMemberModel
                    {
                        Name = property.Name,
                        Kind = "Property"
                    };
                    model.Properties.Add(member);
                }
                FillPropertyMember(member, property, type);
                member.Attributes.Clear();
                member.Attributes.AddRange(GetAttributeList(property));
                if (sourceLinks is not null)
                    member.Source = sourceLinks.TryGetSource(property);
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (field.IsSpecialName || field.Name == "value__") continue;
                var member = FindNamedMember(model.Fields, field.Name);
                if (member is null)
                {
                    member = new ApiMemberModel
                    {
                        Name = field.Name,
                        Kind = "Field"
                    };
                    model.Fields.Add(member);
                }
                FillFieldMember(member, field, type);
                member.Attributes.Clear();
                member.Attributes.AddRange(GetAttributeList(field));
                if (sourceLinks is not null)
                    member.Source = sourceLinks.TryGetSource(field);
            }

            foreach (var evt in type.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                var member = FindNamedMember(model.Events, evt.Name);
                if (member is null)
                {
                    member = new ApiMemberModel
                    {
                        Name = evt.Name,
                        Kind = "Event"
                    };
                    model.Events.Add(member);
                }
                FillEventMember(member, evt, type);
                member.Attributes.Clear();
                member.Attributes.AddRange(GetAttributeList(evt));
                if (sourceLinks is not null)
                    member.Source = sourceLinks.TryGetSource(evt);
            }
        }

        foreach (var kvp in extensionTargets)
        {
            if (!doc.Types.TryGetValue(kvp.Key, out var targetModel)) continue;
            foreach (var extension in kvp.Value)
            {
                if (!targetModel.ExtensionMethods.Any(m => string.Equals(m.Signature, extension.Signature, StringComparison.OrdinalIgnoreCase)))
                    targetModel.ExtensionMethods.Add(extension);
            }
        }
    }

    private static ApiMemberModel? FindNamedMember(List<ApiMemberModel> members, string name)
    {
        return members.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private static ApiMemberModel? FindMethodModel(List<ApiMemberModel> members, MethodInfo method)
    {
        var candidates = members
            .Where(m => string.Equals(m.Name, method.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0) return null;

        var parameters = method.GetParameters();
        foreach (var candidate in candidates)
        {
            if (candidate.Parameters.Count != parameters.Length) continue;
            if (ParamsMatch(candidate.Parameters, parameters)) return candidate;
        }

        return candidates.FirstOrDefault(c => c.Parameters.Count == parameters.Length) ?? candidates.First();
    }

    private static ApiMemberModel? FindConstructorModel(List<ApiMemberModel> members, ConstructorInfo ctor)
    {
        var candidates = members
            .Where(m => m.IsConstructor || string.Equals(m.Name, "#ctor", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (candidates.Count == 0) return null;

        var parameters = ctor.GetParameters();
        foreach (var candidate in candidates)
        {
            if (candidate.Parameters.Count != parameters.Length) continue;
            if (ParamsMatch(candidate.Parameters, parameters)) return candidate;
        }

        return candidates.FirstOrDefault(c => c.Parameters.Count == parameters.Length) ?? candidates.First();
    }

    private static bool ParamsMatch(List<ApiParameterModel> parameters, ParameterInfo[] infos)
    {
        if (parameters.Count != infos.Length) return false;
        for (var i = 0; i < parameters.Count; i++)
        {
            var left = NormalizeTypeName(parameters[i].Type);
            var right = NormalizeTypeName(GetReadableTypeName(infos[i].ParameterType));
            if (!string.Equals(left, right, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    private static string NormalizeTypeName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var name = value.Trim();
        if (name.StartsWith("System.", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(7);
        name = name.Replace("+", ".");
        name = name.Replace("{", "<").Replace("}", ">");
        name = GenericArityRegex.Replace(name, string.Empty);
        return name.Replace(" ", string.Empty);
    }

    private static void MergeTypeParameters(List<ApiTypeParameterModel> target, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (target.Any(tp => string.Equals(tp.Name, name, StringComparison.OrdinalIgnoreCase))) continue;
            target.Add(new ApiTypeParameterModel { Name = name });
        }
    }

    private static string GetAccessModifier(MethodBase method)
    {
        if (method.IsPublic) return "public";
        if (method.IsFamily && method.IsAssembly) return "private protected";
        if (method.IsFamilyOrAssembly) return "protected internal";
        if (method.IsFamily) return "protected";
        if (method.IsAssembly) return "internal";
        return "private";
    }

    private static string GetAccessModifier(FieldInfo field)
    {
        if (field.IsPublic) return "public";
        if (field.IsFamily && field.IsAssembly) return "private protected";
        if (field.IsFamilyOrAssembly) return "protected internal";
        if (field.IsFamily) return "protected";
        if (field.IsAssembly) return "internal";
        return "private";
    }

    private static MethodInfo? GetMostVisibleAccessor(MethodInfo? first, MethodInfo? second)
    {
        if (first is null) return second;
        if (second is null) return first;
        return GetAccessRank(first) >= GetAccessRank(second) ? first : second;
    }

    private static int GetAccessRank(MethodBase method)
    {
        if (method.IsPublic) return 5;
        if (method.IsFamilyOrAssembly) return 4;
        if (method.IsFamily) return 3;
        if (method.IsAssembly) return 2;
        if (method.IsFamily && method.IsAssembly) return 1;
        return 0;
    }

    private static List<string> GetMethodModifiers(MethodInfo method)
    {
        var modifiers = new List<string>();
        if (method.IsStatic) modifiers.Add("static");
        if (method.IsAbstract) modifiers.Add("abstract");
        else if (method.IsVirtual && method.GetBaseDefinition() != method) modifiers.Add("override");
        else if (method.IsVirtual) modifiers.Add("virtual");
        if (method.IsFinal && method.IsVirtual && method.GetBaseDefinition() != method) modifiers.Add("sealed");
        if (IsAsync(method)) modifiers.Add("async");
        return modifiers;
    }

    private static List<string> GetConstructorModifiers(ConstructorInfo ctor)
    {
        var modifiers = new List<string>();
        if (ctor.IsStatic) modifiers.Add("static");
        return modifiers;
    }

    private static List<string> GetPropertyModifiers(PropertyInfo property)
    {
        var accessor = GetMostVisibleAccessor(property.GetMethod, property.SetMethod);
        if (accessor is null) return new List<string>();
        var modifiers = GetMethodModifiers(accessor);
        modifiers.Remove("async");
        return modifiers;
    }

    private static List<string> GetEventModifiers(EventInfo evt)
    {
        var accessor = evt.AddMethod ?? evt.RemoveMethod;
        if (accessor is null) return new List<string>();
        var modifiers = GetMethodModifiers(accessor);
        modifiers.Remove("async");
        return modifiers;
    }

    private static List<string> GetFieldModifiers(FieldInfo field)
    {
        var modifiers = new List<string>();
        if (field.IsStatic && !field.IsLiteral) modifiers.Add("static");
        if (field.IsLiteral) modifiers.Add("const");
        else if (field.IsInitOnly) modifiers.Add("readonly");
        return modifiers;
    }

    private static bool IsAsync(MethodInfo method)
        => method.GetCustomAttributes(typeof(System.Runtime.CompilerServices.AsyncStateMachineAttribute), false).Length > 0;

    private static void FillMethodMember(ApiMemberModel member, MethodInfo method, Type declaring)
    {
        member.Kind = "Method";
        member.ReturnType = GetReadableTypeName(method.ReturnType);
        member.Signature = BuildMethodSignature(method);
        member.IsStatic = method.IsStatic;
        member.DeclaringType = method.DeclaringType?.FullName?.Replace('+', '.');
        member.IsInherited = method.DeclaringType != declaring;
        member.Access = GetAccessModifier(method);
        member.Modifiers.Clear();
        member.Modifiers.AddRange(GetMethodModifiers(method));
        if (method.IsGenericMethodDefinition || method.IsGenericMethod)
            MergeTypeParameters(member.TypeParameters, method.GetGenericArguments().Select(a => a.Name));

        var parameters = method.GetParameters();
        if (member.Parameters.Count == 0)
        {
            member.Parameters = parameters.Select(BuildParameterModel).ToList();
        }
        else
        {
            for (var i = 0; i < parameters.Length; i++)
            {
                if (i >= member.Parameters.Count) break;
                ApplyParameterMetadata(member.Parameters[i], parameters[i]);
            }
        }
    }

    private static void FillConstructorMember(ApiMemberModel member, ConstructorInfo ctor, Type declaring)
    {
        member.Kind = "Constructor";
        member.IsConstructor = true;
        member.ReturnType = null;
        member.Signature = BuildConstructorSignature(ctor, declaring);
        member.IsStatic = ctor.IsStatic;
        member.DeclaringType = ctor.DeclaringType?.FullName?.Replace('+', '.');
        member.IsInherited = false;
        member.Access = GetAccessModifier(ctor);
        member.Modifiers.Clear();
        member.Modifiers.AddRange(GetConstructorModifiers(ctor));
        if (string.IsNullOrWhiteSpace(member.DisplayName))
            member.DisplayName = StripGenericArity(declaring.Name);

        var parameters = ctor.GetParameters();
        if (member.Parameters.Count == 0)
        {
            member.Parameters = parameters.Select(BuildParameterModel).ToList();
        }
        else
        {
            for (var i = 0; i < parameters.Length; i++)
            {
                if (i >= member.Parameters.Count) break;
                ApplyParameterMetadata(member.Parameters[i], parameters[i]);
            }
        }
    }

    private static void FillPropertyMember(ApiMemberModel member, PropertyInfo property, Type declaring)
    {
        member.Kind = "Property";
        member.ReturnType = GetReadableTypeName(property.PropertyType);
        member.Signature = BuildPropertySignature(property);
        member.IsStatic = (property.GetMethod ?? property.SetMethod)?.IsStatic == true;
        member.DeclaringType = property.DeclaringType?.FullName?.Replace('+', '.');
        member.IsInherited = property.DeclaringType != declaring;
        var accessor = GetMostVisibleAccessor(property.GetMethod, property.SetMethod);
        if (accessor is not null)
            member.Access = GetAccessModifier(accessor);
        member.Modifiers.Clear();
        member.Modifiers.AddRange(GetPropertyModifiers(property));
        if (string.IsNullOrWhiteSpace(member.DisplayName))
            member.DisplayName = property.Name;
    }

    private static void FillFieldMember(ApiMemberModel member, FieldInfo field, Type declaring)
    {
        member.Kind = "Field";
        member.ReturnType = GetReadableTypeName(field.FieldType);
        member.Signature = BuildFieldSignature(field);
        member.IsStatic = field.IsStatic;
        member.DeclaringType = field.DeclaringType?.FullName?.Replace('+', '.');
        member.IsInherited = field.DeclaringType != declaring;
        member.Access = GetAccessModifier(field);
        member.Modifiers.Clear();
        member.Modifiers.AddRange(GetFieldModifiers(field));
        if (string.IsNullOrWhiteSpace(member.DisplayName))
            member.DisplayName = field.Name;
        if (field.IsLiteral && field.GetRawConstantValue() is { } value)
            member.Value = value.ToString();
    }

    private static void FillEventMember(ApiMemberModel member, EventInfo evt, Type declaring)
    {
        member.Kind = "Event";
        member.ReturnType = evt.EventHandlerType is null ? null : GetReadableTypeName(evt.EventHandlerType);
        member.Signature = BuildEventSignature(evt);
        member.IsStatic = evt.AddMethod?.IsStatic == true;
        member.DeclaringType = evt.DeclaringType?.FullName?.Replace('+', '.');
        member.IsInherited = evt.DeclaringType != declaring;
        var accessor = evt.AddMethod ?? evt.RemoveMethod;
        if (accessor is not null)
            member.Access = GetAccessModifier(accessor);
        member.Modifiers.Clear();
        member.Modifiers.AddRange(GetEventModifiers(evt));
        if (string.IsNullOrWhiteSpace(member.DisplayName))
            member.DisplayName = evt.Name;
    }

    private static bool IsExtensionMethod(MethodInfo method)
        => method.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute), false);

    private static List<string> GetAttributeList(MemberInfo member)
    {
        var list = new List<string>();
        foreach (var attr in CustomAttributeData.GetCustomAttributes(member))
        {
            if (!ShouldIncludeAttribute(attr)) continue;
            var formatted = FormatAttribute(attr);
            if (!string.IsNullOrWhiteSpace(formatted))
                list.Add(formatted);
        }
        return list;
    }

    private static bool ShouldIncludeAttribute(CustomAttributeData attr)
    {
        var name = attr.AttributeType.FullName ?? attr.AttributeType.Name;
        if (name.StartsWith("System.Runtime.CompilerServices", StringComparison.OrdinalIgnoreCase))
            return false;
        if (name.StartsWith("System.Diagnostics", StringComparison.OrdinalIgnoreCase))
            return false;
        if (name.EndsWith(".ExtensionAttribute", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private static string FormatAttribute(CustomAttributeData attr)
    {
        var name = attr.AttributeType.Name;
        if (name.EndsWith("Attribute", StringComparison.OrdinalIgnoreCase))
            name = name.Substring(0, name.Length - 9);

        var args = new List<string>();
        foreach (var arg in attr.ConstructorArguments)
        {
            args.Add(FormatAttributeArgument(arg));
        }
        foreach (var named in attr.NamedArguments)
        {
            var value = FormatAttributeArgument(named.TypedValue);
            args.Add($"{named.MemberName} = {value}");
        }

        if (args.Count == 0)
            return name;

        return $"{name}({string.Join(", ", args)})";
    }

    private static string FormatAttributeArgument(CustomAttributeTypedArgument arg)
    {
        var value = arg.Value;
        if (value is null) return "null";
        if (value is string s) return $"\"{s}\"";
        if (value is char c) return $"'{c}'";
        if (value is bool b) return b ? "true" : "false";
        if (value is Type t) return $"typeof({GetReadableTypeName(t)})";
        if (value is IReadOnlyCollection<CustomAttributeTypedArgument> list)
        {
            var items = list.Select(FormatAttributeArgument);
            return $"[{string.Join(", ", items)}]";
        }
        return value.ToString() ?? string.Empty;
    }

    private static ApiMemberModel CloneMember(ApiMemberModel source, bool isExtension)
    {
        var clone = new ApiMemberModel
        {
            Name = source.Name,
            DisplayName = source.DisplayName,
            Summary = source.Summary,
            Kind = source.Kind,
            Signature = source.Signature,
            ReturnType = source.ReturnType,
            DeclaringType = source.DeclaringType,
            IsInherited = source.IsInherited,
            IsStatic = source.IsStatic,
            Access = source.Access,
            IsExtension = isExtension,
            IsConstructor = source.IsConstructor,
            Returns = source.Returns,
            Value = source.Value,
            ValueSummary = source.ValueSummary,
            Source = source.Source is null
                ? null
                : new ApiSourceLink { Path = source.Source.Path, Line = source.Source.Line, Url = source.Source.Url }
        };
        foreach (var attr in source.Attributes)
            clone.Attributes.Add(attr);
        foreach (var modifier in source.Modifiers)
            clone.Modifiers.Add(modifier);
        foreach (var tp in source.TypeParameters)
            clone.TypeParameters.Add(new ApiTypeParameterModel { Name = tp.Name, Summary = tp.Summary });
        foreach (var ex in source.Examples)
            clone.Examples.Add(new ApiExampleModel { Kind = ex.Kind, Text = ex.Text });
        foreach (var ex in source.Exceptions)
            clone.Exceptions.Add(new ApiExceptionModel { Type = ex.Type, Summary = ex.Summary });
        foreach (var see in source.SeeAlso)
            clone.SeeAlso.Add(see);
        clone.Parameters = source.Parameters
            .Select(p => new ApiParameterModel
            {
                Name = p.Name,
                Type = p.Type,
                Summary = p.Summary,
                IsOptional = p.IsOptional,
                DefaultValue = p.DefaultValue
            }).ToList();
        return clone;
    }

    private static ApiParameterModel BuildParameterModel(ParameterInfo parameter)
    {
        var model = new ApiParameterModel
        {
            Name = parameter.Name ?? string.Empty,
            Type = GetReadableTypeName(parameter.ParameterType)
        };
        ApplyParameterMetadata(model, parameter);
        return model;
    }

    private static void ApplyParameterMetadata(ApiParameterModel model, ParameterInfo parameter)
    {
        model.IsOptional = parameter.IsOptional;
        if (parameter.HasDefaultValue)
            model.DefaultValue = FormatDefaultValue(parameter.DefaultValue);
    }

    private static string BuildMethodSignature(MethodInfo method)
    {
        var prefix = BuildMethodPrefix(method);
        var name = method.Name;
        if (method.IsGenericMethod)
        {
            var args = method.GetGenericArguments().Select(GetReadableTypeName);
            name += $"<{string.Join(", ", args)}>";
        }
        var returnType = GetReadableTypeName(method.ReturnType);
        var parameters = method.GetParameters()
            .Select(BuildParameterSignature)
            .ToList();
        return $"{prefix}{returnType} {name}({string.Join(", ", parameters)})".Trim();
    }

    private static string BuildParameterSignature(ParameterInfo parameter)
    {
        var prefix = parameter.IsOut ? "out " : parameter.ParameterType.IsByRef ? "ref " : string.Empty;
        if (parameter.GetCustomAttributes(typeof(ParamArrayAttribute), false).Length > 0)
            prefix = "params " + prefix;
        var typeName = GetReadableTypeName(parameter.ParameterType);
        var name = parameter.Name ?? "value";
        var value = $"{prefix}{typeName} {name}".Trim();
        if (parameter.IsOptional)
        {
            var def = parameter.HasDefaultValue ? FormatDefaultValue(parameter.DefaultValue) : "null";
            value += $" = {def}";
        }
        return value;
    }

    private static string BuildPropertySignature(PropertyInfo property)
    {
        var accessors = new List<string>();
        if (property.GetMethod is not null) accessors.Add("get;");
        if (property.SetMethod is not null) accessors.Add("set;");
        var prefix = BuildPropertyPrefix(property);
        return $"{prefix}{GetReadableTypeName(property.PropertyType)} {property.Name} {{ {string.Join(" ", accessors)} }}".Trim();
    }

    private static string BuildFieldSignature(FieldInfo field)
    {
        var prefix = BuildFieldPrefix(field);
        return $"{prefix}{GetReadableTypeName(field.FieldType)} {field.Name}".Trim();
    }

    private static string BuildEventSignature(EventInfo evt)
    {
        var prefix = BuildEventPrefix(evt);
        var handler = evt.EventHandlerType is null ? "EventHandler" : GetReadableTypeName(evt.EventHandlerType);
        return $"{prefix}event {handler} {evt.Name}".Trim();
    }

    private static string BuildConstructorSignature(ConstructorInfo ctor, Type declaringType)
    {
        var prefix = BuildMethodPrefix(ctor);
        var name = GetReadableTypeName(declaringType);
        var parameters = ctor.GetParameters()
            .Select(BuildParameterSignature)
            .ToList();
        return $"{prefix}{name}({string.Join(", ", parameters)})".Trim();
    }

    private static string BuildMethodPrefix(MethodBase method)
    {
        var parts = new List<string>();
        var access = GetAccessModifier(method);
        if (!string.IsNullOrWhiteSpace(access))
            parts.Add(access);
        if (method is MethodInfo mi)
            parts.AddRange(GetMethodModifiers(mi));
        else if (method is ConstructorInfo ci)
            parts.AddRange(GetConstructorModifiers(ci));
        return parts.Count == 0 ? string.Empty : string.Join(" ", parts) + " ";
    }

    private static string BuildPropertyPrefix(PropertyInfo property)
    {
        var accessor = GetMostVisibleAccessor(property.GetMethod, property.SetMethod);
        if (accessor is null) return string.Empty;
        var parts = new List<string> { GetAccessModifier(accessor) };
        parts.AddRange(GetPropertyModifiers(property));
        return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p))) + " ";
    }

    private static string BuildFieldPrefix(FieldInfo field)
    {
        var parts = new List<string> { GetAccessModifier(field) };
        parts.AddRange(GetFieldModifiers(field));
        return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p))) + " ";
    }

    private static string BuildEventPrefix(EventInfo evt)
    {
        var accessor = evt.AddMethod ?? evt.RemoveMethod;
        if (accessor is null) return string.Empty;
        var parts = new List<string> { GetAccessModifier(accessor) };
        parts.AddRange(GetEventModifiers(evt));
        return string.Join(" ", parts.Where(p => !string.IsNullOrWhiteSpace(p))) + " ";
    }

    private static string FormatDefaultValue(object? value)
    {
        if (value is null) return "null";
        return value switch
        {
            string s => $"\"{s}\"",
            char c => $"'{c}'",
            bool b => b ? "true" : "false",
            _ => value.ToString() ?? string.Empty
        };
    }

    private static Assembly? TryLoadAssembly(string assemblyPath, List<string> warnings)
    {
        try
        {
            return AssemblyLoadContext.Default.LoadFromAssemblyPath(assemblyPath);
        }
        catch (Exception ex)
        {
            try
            {
                var bytes = File.ReadAllBytes(assemblyPath);
                return Assembly.Load(bytes);
            }
            catch (Exception ex2)
            {
                warnings.Add($"Assembly load failed: {Path.GetFileName(assemblyPath)} ({ex2.GetType().Name}: {ex2.Message})");
                warnings.Add($"Primary load error: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
        }
    }

    private static IEnumerable<Type?> GetExportedTypesSafe(Assembly assembly)
    {
        try
        {
            return assembly.GetExportedTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            Trace.TraceWarning($"ReflectionTypeLoadException in GetExportedTypesSafe: {ex.Message}");
            return ex.Types ?? Array.Empty<Type?>();
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"GetExportedTypesSafe failed: {ex.GetType().Name}: {ex.Message}");
            return Array.Empty<Type?>();
        }
    }

    private static string GetTypeKind(Type type)
    {
        if (type.IsInterface) return "Interface";
        if (type.IsEnum) return "Enum";
        if (type.IsValueType) return "Struct";
        if (type.BaseType == typeof(MulticastDelegate)) return "Delegate";
        return "Class";
    }

    private static string GetReadableTypeName(Type type)
    {
        if (type.IsByRef)
            type = type.GetElementType() ?? type;

        if (type.IsArray)
            return $"{GetReadableTypeName(type.GetElementType() ?? typeof(object))}[]";

        if (type.IsGenericType)
        {
            var name = StripGenericArity(type.Name);
            var args = type.GetGenericArguments().Select(GetReadableTypeName);
            return $"{name}<{string.Join(", ", args)}>";
        }

        return type.Name;
    }

    private static ApiTypeModel ParseType(
        XElement member,
        string fullName,
        string memberKey,
        IReadOnlyDictionary<string, XElement> memberLookup)
    {
        var lastDot = fullName.LastIndexOf('.');
        var ns = lastDot > 0 ? fullName.Substring(0, lastDot) : string.Empty;
        var name = lastDot > 0 ? fullName.Substring(lastDot + 1) : fullName;

        var model = new ApiTypeModel
        {
            Name = name,
            FullName = fullName,
            Namespace = ns,
            Summary = GetSummary(member, memberKey, memberLookup),
            Remarks = GetElement(member, "remarks", memberKey, memberLookup),
            Kind = InferTypeKind(name),
            Slug = Slugify(fullName)
        };
        model.TypeParameters.AddRange(GetTypeParameters(member, memberKey, memberLookup));
        model.Examples.AddRange(GetExamples(member, memberKey, memberLookup));
        model.SeeAlso.AddRange(GetSeeAlso(member, memberKey, memberLookup));
        return model;
    }

    private static void AddMethod(
        ApiDocModel doc,
        XElement member,
        string fullName,
        string memberKey,
        Assembly? assembly,
        IReadOnlyDictionary<string, XElement> memberLookup)
    {
        var typeName = ExtractTypeName(fullName);
        if (!doc.Types.TryGetValue(typeName, out var type)) return;

        var name = ExtractMemberName(fullName);
        if (string.IsNullOrWhiteSpace(name)) return;

        var parameterTypes = ParseParameterTypes(fullName);
        var parameterNames = TryResolveParameterNames(assembly, typeName, name, parameterTypes);
        var parameters = ParseParameters(member, parameterTypes, parameterNames, memberKey, memberLookup);

        var isCtor = IsConstructorName(name);
        var displayName = isCtor ? GetShortTypeName(typeName) : name;
        var model = new ApiMemberModel
        {
            Name = name,
            DisplayName = displayName,
            Summary = GetSummary(member, memberKey, memberLookup),
            Kind = isCtor ? "Constructor" : "Method",
            Parameters = parameters,
            Returns = GetElement(member, "returns", memberKey, memberLookup),
            IsConstructor = isCtor
        };
        model.TypeParameters.AddRange(GetTypeParameters(member, memberKey, memberLookup));
        model.Examples.AddRange(GetExamples(member, memberKey, memberLookup));
        model.Exceptions.AddRange(GetExceptions(member, memberKey, memberLookup));
        model.SeeAlso.AddRange(GetSeeAlso(member, memberKey, memberLookup));
        if (isCtor)
            type.Constructors.Add(model);
        else
            type.Methods.Add(model);
    }

    private static void AddProperty(
        ApiDocModel doc,
        XElement member,
        string fullName,
        string memberKey,
        IReadOnlyDictionary<string, XElement> memberLookup)
    {
        var typeName = ExtractTypeName(fullName);
        if (!doc.Types.TryGetValue(typeName, out var type)) return;

        var name = ExtractMemberName(fullName);
        if (string.IsNullOrWhiteSpace(name)) return;

        var model = new ApiMemberModel
        {
            Name = name,
            Summary = GetSummary(member, memberKey, memberLookup),
            Kind = "Property",
            ValueSummary = GetElement(member, "value", memberKey, memberLookup)
        };
        model.Examples.AddRange(GetExamples(member, memberKey, memberLookup));
        model.Exceptions.AddRange(GetExceptions(member, memberKey, memberLookup));
        model.SeeAlso.AddRange(GetSeeAlso(member, memberKey, memberLookup));
        type.Properties.Add(model);
    }

    private static void AddField(
        ApiDocModel doc,
        XElement member,
        string fullName,
        string memberKey,
        IReadOnlyDictionary<string, XElement> memberLookup)
    {
        var typeName = ExtractTypeName(fullName);
        if (!doc.Types.TryGetValue(typeName, out var type)) return;

        var name = ExtractMemberName(fullName);
        if (string.IsNullOrWhiteSpace(name)) return;

        var model = new ApiMemberModel
        {
            Name = name,
            Summary = GetSummary(member, memberKey, memberLookup),
            Kind = "Field",
            ValueSummary = GetElement(member, "value", memberKey, memberLookup)
        };
        model.Examples.AddRange(GetExamples(member, memberKey, memberLookup));
        model.Exceptions.AddRange(GetExceptions(member, memberKey, memberLookup));
        model.SeeAlso.AddRange(GetSeeAlso(member, memberKey, memberLookup));
        type.Fields.Add(model);
    }

    private static void AddEvent(
        ApiDocModel doc,
        XElement member,
        string fullName,
        string memberKey,
        IReadOnlyDictionary<string, XElement> memberLookup)
    {
        var typeName = ExtractTypeName(fullName);
        if (!doc.Types.TryGetValue(typeName, out var type)) return;

        var name = ExtractMemberName(fullName);
        if (string.IsNullOrWhiteSpace(name)) return;

        var model = new ApiMemberModel
        {
            Name = name,
            Summary = GetSummary(member, memberKey, memberLookup),
            Kind = "Event"
        };
        model.Examples.AddRange(GetExamples(member, memberKey, memberLookup));
        model.Exceptions.AddRange(GetExceptions(member, memberKey, memberLookup));
        model.SeeAlso.AddRange(GetSeeAlso(member, memberKey, memberLookup));
        type.Events.Add(model);
    }

    private static string ExtractTypeName(string fullName)
    {
        var trimmed = fullName;
        var parenIdx = trimmed.IndexOf('(');
        if (parenIdx > 0)
            trimmed = trimmed.Substring(0, parenIdx);
        var lastDot = trimmed.LastIndexOf('.');
        return lastDot > 0 ? trimmed.Substring(0, lastDot) : trimmed;
    }

    private static string ExtractMemberName(string fullName)
    {
        var trimmed = fullName;
        var parenIdx = trimmed.IndexOf('(');
        if (parenIdx > 0)
            trimmed = trimmed.Substring(0, parenIdx);
        var lastDot = trimmed.LastIndexOf('.');
        return lastDot > 0 ? trimmed.Substring(lastDot + 1) : trimmed;
    }

    private static List<ApiParameterModel> ParseParameters(
        XElement member,
        IReadOnlyList<string> parameterTypes,
        IReadOnlyList<string>? parameterNames,
        string memberKey,
        IReadOnlyDictionary<string, XElement> memberLookup)
    {
        var results = new List<ApiParameterModel>();
        var ownParamElements = member.Elements("param").ToList();
        var inheritedParamElements = ownParamElements.Count > 0
            ? new List<XElement>()
            : GetInheritedElements(member, memberKey, "param", memberLookup);
        var count = Math.Max(Math.Max(ownParamElements.Count, inheritedParamElements.Count), parameterTypes.Count);
        for (var i = 0; i < count; i++)
        {
            var paramElement = i < ownParamElements.Count
                ? ownParamElements[i]
                : (i < inheritedParamElements.Count ? inheritedParamElements[i] : null);
            var paramName = paramElement is not null
                ? paramElement.Attribute("name")?.Value ?? $"arg{i + 1}"
                : (parameterNames != null && i < parameterNames.Count && !string.IsNullOrWhiteSpace(parameterNames[i])
                    ? parameterNames[i]
                    : $"arg{i + 1}");
            var summary = paramElement is null ? null : NormalizeXmlText(paramElement);
            var type = i < parameterTypes.Count ? parameterTypes[i] : string.Empty;
            results.Add(new ApiParameterModel
            {
                Name = paramName,
                Type = type,
                Summary = summary
            });
        }
        return results;
    }

    private static bool ShouldIncludeType(ApiTypeModel type, WebApiDocsOptions options)
    {
        var ns = type.Namespace ?? string.Empty;
        if (options.IncludeNamespacePrefixes.Count > 0)
        {
            var matches = options.IncludeNamespacePrefixes.Any(prefix =>
                ns.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                type.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (!matches) return false;
        }

        if (options.ExcludeNamespacePrefixes.Count > 0)
        {
            var excluded = options.ExcludeNamespacePrefixes.Any(prefix =>
                ns.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                type.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
            if (excluded) return false;
        }

        if (options.IncludeTypeNames.Count > 0)
        {
            var matches = options.IncludeTypeNames.Any(pattern => MatchTypePattern(pattern, type));
            if (!matches) return false;
        }

        if (options.ExcludeTypeNames.Count > 0)
        {
            var excluded = options.ExcludeTypeNames.Any(pattern => MatchTypePattern(pattern, type));
            if (excluded) return false;
        }

        return true;
    }

    private static bool MatchTypePattern(string pattern, ApiTypeModel type)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        var fullName = type.FullName ?? string.Empty;
        var name = type.Name ?? string.Empty;
        if (pattern.EndsWith("*", StringComparison.Ordinal))
        {
            var prefix = pattern.TrimEnd('*');
            return fullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                   name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(fullName, pattern, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string>? TryResolveParameterNames(Assembly? assembly, string typeName, string memberName, IReadOnlyList<string> parameterTypes)
    {
        if (assembly is null) return null;
        var type = ResolveType(assembly, typeName);
        if (type is null) return null;

        var lookupName = StripGenericArity(memberName);
        if (IsConstructorName(lookupName))
        {
            var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
            return ResolveParameterNamesFromCandidates(ctors, parameterTypes, assembly);
        }

        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => string.Equals(m.Name, lookupName, StringComparison.Ordinal))
            .ToArray();

        return ResolveParameterNamesFromCandidates(methods, parameterTypes, assembly);
    }

    private static IReadOnlyList<string>? ResolveParameterNamesFromCandidates(MethodBase[] candidates, IReadOnlyList<string> parameterTypes, Assembly assembly)
    {
        foreach (var candidate in candidates)
        {
            var parameters = candidate.GetParameters();
            if (!ParameterTypesMatch(parameters, parameterTypes, assembly)) continue;
            return parameters.Select(p => p.Name ?? string.Empty).ToList();
        }

        var countMatches = candidates
            .Where(m => m.GetParameters().Length == parameterTypes.Count)
            .ToArray();
        if (countMatches.Length == 1)
        {
            return countMatches[0].GetParameters().Select(p => p.Name ?? string.Empty).ToList();
        }

        return null;
    }

    private static bool ParameterTypesMatch(ParameterInfo[] parameters, IReadOnlyList<string> parameterTypes, Assembly assembly)
    {
        if (parameters.Length != parameterTypes.Count) return false;
        for (var i = 0; i < parameterTypes.Count; i++)
        {
            if (!ParameterTypeMatches(parameters[i].ParameterType, parameterTypes[i], assembly))
                return false;
        }
        return true;
    }

    private static bool ParameterTypeMatches(Type parameterType, string xmlType, Assembly assembly)
    {
        if (string.IsNullOrWhiteSpace(xmlType)) return false;
        var typeName = xmlType.Trim();
        var byRef = false;
        if (typeName.EndsWith("@", StringComparison.Ordinal) || typeName.EndsWith("&", StringComparison.Ordinal))
        {
            byRef = true;
            typeName = typeName.TrimEnd('@', '&');
        }

        if (parameterType.IsByRef != byRef)
            return false;
        if (byRef)
            parameterType = parameterType.GetElementType() ?? parameterType;

        var arrayRanks = 0;
        while (typeName.EndsWith("[]", StringComparison.Ordinal))
        {
            arrayRanks++;
            typeName = typeName.Substring(0, typeName.Length - 2);
        }

        if (arrayRanks > 0)
        {
            for (var i = 0; i < arrayRanks; i++)
            {
                if (!parameterType.IsArray) return false;
                parameterType = parameterType.GetElementType() ?? parameterType;
            }
        }
        else if (parameterType.IsArray)
        {
            return false;
        }

        if (TryParseGenericParameterToken(typeName, out var isMethodParameter, out var position))
        {
            if (!parameterType.IsGenericParameter) return false;
            if (parameterType.GenericParameterPosition != position) return false;
            if (isMethodParameter && parameterType.DeclaringMethod is null) return false;
            if (!isMethodParameter && parameterType.DeclaringMethod is not null) return false;
            return true;
        }

        var genericStart = typeName.IndexOf('{');
        if (genericStart >= 0 && typeName.EndsWith("}", StringComparison.Ordinal))
        {
            if (!parameterType.IsGenericType) return false;
            var outer = typeName.Substring(0, genericStart);
            var argsText = typeName.Substring(genericStart + 1, typeName.Length - genericStart - 2);
            var argTokens = SplitTypeArguments(argsText);
            var genericDefName = $"{outer}`{argTokens.Count}";
            var resolvedDef = ResolveType(assembly, genericDefName) ?? ResolveType(assembly, outer);
            if (resolvedDef is null) return false;
            if (parameterType.GetGenericTypeDefinition() != resolvedDef) return false;
            var argTypes = parameterType.GetGenericArguments();
            if (argTypes.Length != argTokens.Count) return false;
            for (var i = 0; i < argTypes.Length; i++)
            {
                if (!ParameterTypeMatches(argTypes[i], argTokens[i], assembly))
                    return false;
            }
            return true;
        }

        var resolved = ResolveType(assembly, typeName);
        return resolved is not null && parameterType == resolved;
    }

    private static bool TryParseGenericParameterToken(string typeName, out bool isMethodParameter, out int position)
    {
        isMethodParameter = false;
        position = -1;
        if (string.IsNullOrWhiteSpace(typeName)) return false;
        if (typeName.StartsWith("``", StringComparison.Ordinal))
        {
            isMethodParameter = true;
            if (typeName.Length <= 2) return false;
            return int.TryParse(typeName.Substring(2), out position) && position >= 0 && position < 128;
        }
        if (typeName.StartsWith("`", StringComparison.Ordinal))
        {
            isMethodParameter = false;
            if (typeName.Length <= 1) return false;
            return int.TryParse(typeName.Substring(1), out position) && position >= 0 && position < 128;
        }
        return false;
    }

    private static List<string> SplitTypeArguments(string argsText)
    {
        var results = new List<string>();
        if (string.IsNullOrWhiteSpace(argsText)) return results;
        var sb = new StringBuilder();
        var depth = 0;
        foreach (var ch in argsText)
        {
            if (ch == '{' || ch == '[')
                depth++;
            if (ch == '}' || ch == ']')
                depth = Math.Max(0, depth - 1);

            if (ch == ',' && depth == 0)
            {
                results.Add(sb.ToString().Trim());
                sb.Clear();
                continue;
            }
            sb.Append(ch);
        }
        if (sb.Length > 0) results.Add(sb.ToString().Trim());
        return results;
    }

    private static Type? ResolveType(Assembly assembly, string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return null;
        var candidate = fullName;
        var type = assembly.GetType(candidate) ?? Type.GetType(candidate);
        if (type is not null) return type;

        while (true)
        {
            var lastDot = candidate.LastIndexOf('.');
            if (lastDot <= 0) break;
            candidate = candidate.Substring(0, lastDot) + "+" + candidate.Substring(lastDot + 1);
            type = assembly.GetType(candidate) ?? Type.GetType(candidate);
            if (type is not null) return type;
        }

        foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
        {
            type = loaded.GetType(fullName);
            if (type is not null) return type;
        }

        return null;
    }

    private static string StripGenericArity(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;
        return GenericArityRegex.Replace(name, string.Empty);
    }

    private static List<string> ParseParameterTypes(string fullName)
    {
        var start = fullName.IndexOf('(');
        if (start < 0) return new List<string>();
        var end = fullName.LastIndexOf(')');
        if (end <= start) return new List<string>();
        var segment = fullName.Substring(start + 1, end - start - 1);
        if (string.IsNullOrWhiteSpace(segment)) return new List<string>();

        var results = new List<string>();
        var sb = new StringBuilder();
        var depth = 0;
        foreach (var ch in segment)
        {
            if (ch == '{' || ch == '[')
                depth++;
            if (ch == '}' || ch == ']')
                depth = Math.Max(0, depth - 1);

            if (ch == ',' && depth == 0)
            {
                results.Add(sb.ToString().Trim());
                sb.Clear();
                continue;
            }
            sb.Append(ch);
        }
        if (sb.Length > 0) results.Add(sb.ToString().Trim());
        return results;
    }

    private static string? GetSummary(
        XElement member,
        string memberKey,
        IReadOnlyDictionary<string, XElement> memberLookup)
    {
        var summary = member.Element("summary");
        if (summary is not null)
            return NormalizeXmlText(summary);
        return GetInheritedElement(member, memberKey, "summary", memberLookup);
    }

    private static string? GetElement(
        XElement member,
        string name,
        string memberKey,
        IReadOnlyDictionary<string, XElement> memberLookup)
    {
        var element = member.Element(name);
        if (element is not null)
            return NormalizeXmlText(element);
        return GetInheritedElement(member, memberKey, name, memberLookup);
    }

    private static List<ApiTypeParameterModel> GetTypeParameters(
        XElement member,
        string memberKey,
        IReadOnlyDictionary<string, XElement> memberLookup)
    {
        var results = ParseTypeParameters(member);
        if (results.Count > 0)
            return results;

        var inheritedTypeParams = GetInheritedElements(member, memberKey, "typeparam", memberLookup);
        if (inheritedTypeParams.Count == 0)
            return results;

        return ParseTypeParameters(inheritedTypeParams);
    }

    private static List<ApiExceptionModel> GetExceptions(
        XElement member,
        string memberKey,
        IReadOnlyDictionary<string, XElement> memberLookup)
    {
        var results = ParseExceptions(member);
        if (results.Count > 0)
            return results;

        var inheritedExceptions = GetInheritedElements(member, memberKey, "exception", memberLookup);
        if (inheritedExceptions.Count == 0)
            return results;

        return ParseExceptions(inheritedExceptions);
    }

    private static List<ApiExampleModel> GetExamples(
        XElement member,
        string memberKey,
        IReadOnlyDictionary<string, XElement> memberLookup)
    {
        var results = new List<ApiExampleModel>();
        foreach (var example in member.Elements("example"))
        {
            results.AddRange(ParseExampleBlocks(example));
        }
        if (results.Count > 0)
            return results;

        var inheritedExamples = GetInheritedElements(member, memberKey, "example", memberLookup);
        foreach (var example in inheritedExamples)
        {
            results.AddRange(ParseExampleBlocks(example));
        }
        return results;
    }

    private static List<ApiExampleModel> ParseExampleBlocks(XElement example)
    {
        var results = new List<ApiExampleModel>();
        foreach (var node in example.Nodes())
        {
            switch (node)
            {
                case XText text:
                    var normalized = Normalize(text.Value);
                    if (!string.IsNullOrWhiteSpace(normalized))
                        results.Add(new ApiExampleModel { Kind = "text", Text = normalized });
                    break;
                case XElement el:
                    var local = el.Name.LocalName;
                    if (local.Equals("code", StringComparison.OrdinalIgnoreCase))
                    {
                        var code = Dedent(el.Value.Trim('\r', '\n'));
                        if (!string.IsNullOrWhiteSpace(code))
                            results.Add(new ApiExampleModel { Kind = "code", Text = code });
                    }
                    else
                    {
                        var textBlock = NormalizeXmlText(el);
                        if (!string.IsNullOrWhiteSpace(textBlock))
                            results.Add(new ApiExampleModel { Kind = "text", Text = textBlock });
                    }
                    break;
            }
        }

        if (results.Count == 0)
        {
            var fallback = NormalizeXmlText(example);
            if (!string.IsNullOrWhiteSpace(fallback))
                results.Add(new ApiExampleModel { Kind = "text", Text = fallback });
        }

        return results;
    }

    private static List<string> GetSeeAlso(
        XElement member,
        string memberKey,
        IReadOnlyDictionary<string, XElement> memberLookup)
    {
        var results = new List<string>();
        var ownSeeAlso = member.Elements("seealso").ToList();
        foreach (var see in ownSeeAlso)
        {
            var text = NormalizeSeeAlsoElement(see);
            if (!string.IsNullOrWhiteSpace(text))
                results.Add(text);
        }
        if (results.Count > 0)
            return results;

        var inheritedSeeAlso = GetInheritedElements(member, memberKey, "seealso", memberLookup);
        foreach (var see in inheritedSeeAlso)
        {
            var text = NormalizeSeeAlsoElement(see);
            if (!string.IsNullOrWhiteSpace(text))
                results.Add(text);
        }
        return results;
    }

    private static string? NormalizeSeeAlsoElement(XElement see)
    {
        var href = NormalizeInlineHref(see.Attribute("href")?.Value);
        if (!string.IsNullOrWhiteSpace(href))
        {
            var label = Normalize(see.Value);
            if (string.IsNullOrWhiteSpace(label))
                label = href;
            return BuildHrefToken(href, label);
        }
        return NormalizeXmlText(see);
    }

    private static List<ApiTypeParameterModel> ParseTypeParameters(XElement member)
        => ParseTypeParameters(member.Elements("typeparam"));

    private static List<ApiTypeParameterModel> ParseTypeParameters(IEnumerable<XElement> typeParameters)
    {
        var results = new List<ApiTypeParameterModel>();
        foreach (var tp in typeParameters)
        {
            var name = tp.Attribute("name")?.Value;
            if (string.IsNullOrWhiteSpace(name))
                continue;
            results.Add(new ApiTypeParameterModel
            {
                Name = name,
                Summary = NormalizeXmlText(tp)
            });
        }
        return results;
    }

    private static List<ApiExceptionModel> ParseExceptions(XElement member)
        => ParseExceptions(member.Elements("exception"));

    private static List<ApiExceptionModel> ParseExceptions(IEnumerable<XElement> exceptions)
    {
        var results = new List<ApiExceptionModel>();
        foreach (var ex in exceptions)
        {
            var cref = ex.Attribute("cref")?.Value;
            var typeName = CleanCref(cref);
            if (string.IsNullOrWhiteSpace(typeName))
                continue;
            results.Add(new ApiExceptionModel
            {
                Type = typeName,
                Summary = NormalizeXmlText(ex)
            });
        }
        return results;
    }

    private static string? GetInheritedElement(
        XElement member,
        string memberKey,
        string elementName,
        IReadOnlyDictionary<string, XElement> memberLookup)
    {
        foreach (var inherited in EnumerateInheritedMembers(member, memberKey, memberLookup))
        {
            var value = inherited.Element(elementName);
            if (value is null)
                continue;
            var normalized = NormalizeXmlText(value);
            if (!string.IsNullOrWhiteSpace(normalized))
                return normalized;
        }
        return null;
    }

    private static List<XElement> GetInheritedElements(
        XElement member,
        string memberKey,
        string elementName,
        IReadOnlyDictionary<string, XElement> memberLookup)
    {
        foreach (var inherited in EnumerateInheritedMembers(member, memberKey, memberLookup))
        {
            var values = inherited.Elements(elementName).ToList();
            if (values.Count > 0)
                return values;
        }
        return new List<XElement>();
    }

    private static IEnumerable<XElement> EnumerateInheritedMembers(
        XElement member,
        string memberKey,
        IReadOnlyDictionary<string, XElement> memberLookup)
    {
        var current = member;
        var currentKey = memberKey;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var depth = 0; depth < 8; depth++)
        {
            if (!visited.Add(currentKey))
                yield break;

            var inheritDoc = current.Element("inheritdoc");
            if (inheritDoc is null)
                yield break;

            var cref = inheritDoc.Attribute("cref")?.Value;
            if (string.IsNullOrWhiteSpace(cref))
                yield break;

            var targetKey = ResolveInheritDocKey(cref, currentKey, memberLookup);
            if (string.IsNullOrWhiteSpace(targetKey))
                yield break;

            if (!memberLookup.TryGetValue(targetKey, out var inherited))
                yield break;

            yield return inherited;
            current = inherited;
            currentKey = targetKey;
        }
    }

    private static string? ResolveInheritDocKey(
        string cref,
        string currentKey,
        IReadOnlyDictionary<string, XElement> memberLookup)
    {
        var trimmed = cref.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return null;

        if (memberLookup.ContainsKey(trimmed))
            return trimmed;

        var currentPrefix = currentKey.Length > 1 && currentKey[1] == ':' ? currentKey[0] : '\0';
        if (trimmed.Length > 2 && trimmed[1] == ':')
            return trimmed;

        if (currentPrefix != '\0')
        {
            var samePrefix = $"{currentPrefix}:{trimmed}";
            if (memberLookup.ContainsKey(samePrefix))
                return samePrefix;
        }

        var typeKey = $"T:{trimmed}";
        if (memberLookup.ContainsKey(typeKey))
            return typeKey;

        return null;
    }

    private static string Dedent(string code)
    {
        var lines = code.Split('\n');
        var minIndent = int.MaxValue;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            var indent = 0;
            while (indent < line.Length && line[indent] == ' ') indent++;
            if (indent < minIndent) minIndent = indent;
        }
        if (minIndent == 0 || minIndent == int.MaxValue) return code;
        var sb = new StringBuilder();
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0) sb.Append('\n');
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line))
                sb.Append("");
            else
                sb.Append(line.Substring(Math.Min(minIndent, line.Length)));
        }
        return sb.ToString();
    }

    private static string Normalize(string value)
    {
        return WhitespaceRegex.Replace(value, " ").Trim();
    }

    private static string CleanCref(string? cref)
    {
        if (string.IsNullOrWhiteSpace(cref)) return string.Empty;
        var cleaned = cref;
        var colonIdx = cleaned.IndexOf(':');
        if (colonIdx >= 0 && colonIdx + 1 < cleaned.Length)
            cleaned = cleaned.Substring(colonIdx + 1);
        return cleaned.Trim();
    }

    private static bool IsConstructorName(string name)
        => name == "#ctor" || name == ".ctor" || name == ".cctor";

    private static string GetShortTypeName(string fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return fullName;
        var lastDot = fullName.LastIndexOf('.');
        var name = lastDot > -1 ? fullName.Substring(lastDot + 1) : fullName;
        return StripGenericArity(name);
    }

    private static string? NormalizeXmlText(XElement element)
    {
        var text = string.Concat(element.Nodes().Select(n =>
        {
            if (n is XText txt) return txt.Value;
            if (n is XElement el)
            {
                return el.Name.LocalName switch
                {
                    "see" => RenderSeeLikeElement(el),
                    "seealso" => RenderSeeLikeElement(el),
                    "a" => RenderAnchorElement(el),
                    "paramref" => el.Attribute("name")?.Value ?? el.Value,
                    "typeparamref" => el.Attribute("name")?.Value ?? el.Value,
                    "c" => el.Value,
                    "code" => $" {el.Value} ",
                    "para" => $" {el.Value} ",
                    _ => el.Value
                };
            }
            return string.Empty;
        }));

        return string.IsNullOrWhiteSpace(text) ? null : Normalize(text);
    }

    private static string RenderSeeLikeElement(XElement el)
    {
        var href = NormalizeInlineHref(el.Attribute("href")?.Value);
        if (!string.IsNullOrWhiteSpace(href))
        {
            var label = Normalize(el.Value);
            if (string.IsNullOrWhiteSpace(label))
                label = href;
            return BuildHrefToken(href, label);
        }
        return RenderCref(el);
    }

    private static string RenderAnchorElement(XElement el)
    {
        var href = NormalizeInlineHref(el.Attribute("href")?.Value);
        if (string.IsNullOrWhiteSpace(href))
            return Normalize(el.Value);

        var label = Normalize(el.Value);
        if (string.IsNullOrWhiteSpace(label))
            label = href;
        return BuildHrefToken(href, label);
    }

    private static string RenderCref(XElement el)
    {
        var cref = el.Attribute("cref")?.Value;
        if (!string.IsNullOrWhiteSpace(cref))
        {
            var cleaned = cref;
            var colonIdx = cleaned.IndexOf(':');
            if (colonIdx >= 0 && colonIdx + 1 < cleaned.Length)
                cleaned = cleaned.Substring(colonIdx + 1);
            return $"[[cref:{cleaned}]]";
        }

        var langword = el.Attribute("langword")?.Value;
        if (!string.IsNullOrWhiteSpace(langword))
            return langword;

        return el.Value;
    }

    private static string BuildHrefToken(string href, string label)
    {
        var encodedHref = Uri.EscapeDataString(href);
        var encodedLabel = Uri.EscapeDataString(label);
        return $"[[href:{encodedHref}|{encodedLabel}]]";
    }

    private static string NormalizeInlineHref(string? href)
    {
        if (string.IsNullOrWhiteSpace(href))
            return string.Empty;

        var trimmed = href.Trim();
        if (trimmed.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return string.Empty;

        return trimmed;
    }

    private static string InferTypeKind(string name)
    {
        if (name.StartsWith("I") && name.Length > 1 && char.IsUpper(name[1]))
            return "Interface";
        return "Class";
    }

    private static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var slug = value.ToLowerInvariant();
        slug = slug.Replace('+', '-');
        slug = GenericArityRegex.Replace(slug, string.Empty);
        slug = SlugNonAlnumRegex.Replace(slug, "-");
        slug = SlugDashRegex.Replace(slug, "-").Trim('-');
        return slug;
    }

    private static void WriteJson(string path, object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json, Encoding.UTF8);
    }

    private static void GenerateHtml(string outputPath, WebApiDocsOptions options, IReadOnlyList<ApiTypeModel> types)
    {
        var template = (options.Template ?? string.Empty).Trim().ToLowerInvariant();
        if (template is "docs" or "sidebar")
        {
            GenerateDocsHtml(outputPath, options, types);
            return;
        }

          var header = LoadOptionalHtml(options.HeaderHtmlPath);
          var footer = LoadOptionalHtml(options.FooterHtmlPath);
          ApplyNavTokens(options, ref header, ref footer);
          var bodyClass = ResolveBodyClass(options.BodyClass);
          var cssLink = string.IsNullOrWhiteSpace(options.CssHref) ? string.Empty : $"<link rel=\"stylesheet\" href=\"{options.CssHref}\" />";
          var fallbackCss = LoadAsset(options, "fallback.css", null);
          var cssBlock = string.IsNullOrWhiteSpace(cssLink)
              ? WrapStyle(fallbackCss)
              : cssLink;

        var indexTemplate = LoadTemplate(options, "index.html", options.IndexTemplatePath);
        var typeLinks = new StringBuilder();
        foreach (var type in types)
        {
            typeLinks.AppendLine($"      <a class=\"pf-api-type\" href=\"types/{type.Slug}.html\">{System.Web.HttpUtility.HtmlEncode(type.FullName)}</a>");
        }
        var searchScript = WrapScript(LoadAsset(options, "search.js", options.SearchScriptPath));
          var indexHtml = ApplyTemplate(indexTemplate, new Dictionary<string, string?>
          {
              ["TITLE"] = System.Web.HttpUtility.HtmlEncode(options.Title),
              ["CSS"] = cssBlock,
              ["HEADER"] = header,
              ["FOOTER"] = footer,
              ["BODY_CLASS"] = bodyClass,
              ["TYPE_COUNT"] = types.Count.ToString(),
              ["TYPE_LINKS"] = typeLinks.ToString().TrimEnd(),
              ["SEARCH_SCRIPT"] = searchScript
          });

        File.WriteAllText(Path.Combine(outputPath, "index.html"), indexHtml.ToString(), Encoding.UTF8);

        var typesDir = Path.Combine(outputPath, "types");
        Directory.CreateDirectory(typesDir);
        foreach (var type in types)
        {
            var memberHtml = new StringBuilder();
            var codeLanguage = GetDefaultCodeLanguage(options);
            AppendMembers(memberHtml, "Methods", type.Methods, codeLanguage);
            AppendMembers(memberHtml, "Properties", type.Properties, codeLanguage);
            AppendMembers(memberHtml, "Fields", type.Fields, codeLanguage);
            AppendMembers(memberHtml, "Events", type.Events, codeLanguage);

            var summaryHtml = string.IsNullOrWhiteSpace(type.Summary)
                ? string.Empty
                : $"    <p>{System.Web.HttpUtility.HtmlEncode(type.Summary)}</p>";
            var remarksHtml = string.IsNullOrWhiteSpace(type.Remarks)
                ? string.Empty
                : $"    <div class=\"pf-api-remarks\">{System.Web.HttpUtility.HtmlEncode(type.Remarks)}</div>";

            var typeTitle = $"{type.FullName} - {options.Title}";
            var typeTemplate = LoadTemplate(options, "type.html", options.TypeTemplatePath);
          var typeHtml = ApplyTemplate(typeTemplate, new Dictionary<string, string?>
          {
              ["TYPE_TITLE"] = System.Web.HttpUtility.HtmlEncode(typeTitle),
              ["TYPE_FULLNAME"] = System.Web.HttpUtility.HtmlEncode(type.FullName),
              ["CSS"] = cssBlock,
              ["HEADER"] = header,
              ["FOOTER"] = footer,
              ["BODY_CLASS"] = bodyClass,
              ["TYPE_SUMMARY"] = summaryHtml,
              ["TYPE_REMARKS"] = remarksHtml,
              ["MEMBERS"] = memberHtml.ToString().TrimEnd()
          });

            File.WriteAllText(Path.Combine(typesDir, $"{type.Slug}.html"), typeHtml, Encoding.UTF8);
        }

        var sitemapPath = Path.Combine(outputPath, "sitemap.xml");
        GenerateApiSitemap(sitemapPath, options.BaseUrl, types);
    }

    private static void GenerateDocsHtml(string outputPath, WebApiDocsOptions options, IReadOnlyList<ApiTypeModel> types)
    {
          var header = LoadOptionalHtml(options.HeaderHtmlPath);
          var footer = LoadOptionalHtml(options.FooterHtmlPath);
          ApplyNavTokens(options, ref header, ref footer);
          var bodyClass = ResolveBodyClass(options.BodyClass);
          var cssLink = string.IsNullOrWhiteSpace(options.CssHref) ? string.Empty : $"<link rel=\"stylesheet\" href=\"{options.CssHref}\" />";
        var fallbackCss = LoadAsset(options, "fallback.css", null);
        var cssBlock = string.IsNullOrWhiteSpace(cssLink)
            ? WrapStyle(fallbackCss)
            : cssLink;

        var baseUrl = string.IsNullOrWhiteSpace(options.BaseUrl) ? "/api" : options.BaseUrl.TrimEnd('/');
        var docsScript = WrapScript(LoadAsset(options, "docs.js", options.DocsScriptPath));
        var docsHomeUrl = NormalizeDocsHomeUrl(options.DocsHomeUrl);
        var sidebarHtml = BuildDocsSidebar(types, baseUrl, string.Empty, docsHomeUrl);
        var sidebarClass = BuildSidebarClass(options.SidebarPosition);
        var overviewHtml = BuildDocsOverview(types, baseUrl);
        var slugMap = BuildTypeSlugMap(types);
        var typeIndex = BuildTypeIndex(types);
        var derivedMap = BuildDerivedTypeMap(types, typeIndex);

        var indexTemplate = LoadTemplate(options, "docs-index.html", options.DocsIndexTemplatePath);
          var indexHtml = ApplyTemplate(indexTemplate, new Dictionary<string, string?>
          {
              ["TITLE"] = System.Web.HttpUtility.HtmlEncode(options.Title),
              ["CSS"] = cssBlock,
              ["HEADER"] = header,
              ["FOOTER"] = footer,
              ["BODY_CLASS"] = bodyClass,
              ["SIDEBAR"] = sidebarHtml,
              ["SIDEBAR_CLASS"] = sidebarClass,
              ["MAIN"] = overviewHtml,
              ["DOCS_SCRIPT"] = docsScript
          });
        File.WriteAllText(Path.Combine(outputPath, "index.html"), indexHtml.ToString(), Encoding.UTF8);

        foreach (var type in types)
        {
            var sidebar = BuildDocsSidebar(types, baseUrl, type.Slug, docsHomeUrl);
            var sidebarClassForType = BuildSidebarClass(options.SidebarPosition);
            var typeMain = BuildDocsTypeDetail(type, baseUrl, slugMap, typeIndex, derivedMap, GetDefaultCodeLanguage(options));
            var typeTemplate = LoadTemplate(options, "docs-type.html", options.DocsTypeTemplatePath);
            var pageTitle = $"{type.Name} - {options.Title}";
          var typeHtml = ApplyTemplate(typeTemplate, new Dictionary<string, string?>
          {
              ["TITLE"] = System.Web.HttpUtility.HtmlEncode(pageTitle),
              ["CSS"] = cssBlock,
              ["HEADER"] = header,
              ["FOOTER"] = footer,
              ["BODY_CLASS"] = bodyClass,
              ["SIDEBAR"] = sidebar,
              ["SIDEBAR_CLASS"] = sidebarClassForType,
              ["MAIN"] = typeMain,
              ["DOCS_SCRIPT"] = docsScript
          });

            var htmlPath = Path.Combine(outputPath, $"{type.Slug}.html");
            File.WriteAllText(htmlPath, typeHtml, Encoding.UTF8);

            var typeDir = Path.Combine(outputPath, type.Slug);
            Directory.CreateDirectory(typeDir);
            File.WriteAllText(Path.Combine(typeDir, "index.html"), typeHtml, Encoding.UTF8);
        }

        var sitemapPath = Path.Combine(outputPath, "sitemap.xml");
        GenerateDocsSitemap(sitemapPath, baseUrl, types);
    }

    private static void AppendMembers(StringBuilder sb, string label, List<ApiMemberModel> members, string codeLanguage)
    {
        if (members.Count == 0) return;
        sb.AppendLine($"    <section class=\"pf-api-section\">");
        sb.AppendLine($"      <h2>{label}</h2>");
        sb.AppendLine("      <ul>");
        foreach (var member in members)
        {
            var summaryText = StripCrefTokens(member.Summary);
            var summary = string.IsNullOrWhiteSpace(summaryText)
                ? string.Empty
                : $" - {System.Web.HttpUtility.HtmlEncode(summaryText)}";
            sb.AppendLine("        <li>");
            var signature = !string.IsNullOrWhiteSpace(member.Signature)
                ? member.Signature
                : BuildSignature(member, label);
            sb.AppendLine($"          <strong>{System.Web.HttpUtility.HtmlEncode(signature)}</strong>{summary}");
            if (member.Parameters.Count > 0)
            {
                sb.AppendLine("          <div class=\"pf-api-params\">");
                sb.AppendLine("            <ul>");
                foreach (var param in member.Parameters)
                {
                    var type = string.IsNullOrWhiteSpace(param.Type) ? string.Empty : $" ({System.Web.HttpUtility.HtmlEncode(param.Type)})";
                    var psummaryText = StripCrefTokens(param.Summary);
                    var psummary = string.IsNullOrWhiteSpace(psummaryText) ? string.Empty : $": {System.Web.HttpUtility.HtmlEncode(psummaryText)}";
                    sb.AppendLine($"              <li><code>{System.Web.HttpUtility.HtmlEncode(param.Name)}</code>{type}{psummary}</li>");
                }
                sb.AppendLine("            </ul>");
                sb.AppendLine("          </div>");
            }
            if (!string.IsNullOrWhiteSpace(member.Returns))
            {
                var returnsText = StripCrefTokens(member.Returns);
                sb.AppendLine($"          <div class=\"pf-api-returns\">Returns: {System.Web.HttpUtility.HtmlEncode(returnsText)}</div>");
            }
            sb.AppendLine("        </li>");
        }
        sb.AppendLine("      </ul>");
        sb.AppendLine("    </section>");
    }

    private static readonly string[] MainTypeOrder =
    {
        "QR",
        "Barcode",
        "QrEasy",
        "BarcodeEasy",
        "QrImageDecoder",
        "DataMatrixCode",
        "Pdf417Code",
        "AztecCode"
    };

    private static string BuildDocsSidebar(IReadOnlyList<ApiTypeModel> types, string baseUrl, string activeSlug, string docsHomeUrl)
    {
        var indexUrl = EnsureTrailingSlash(baseUrl);
        var sb = new StringBuilder();
        sb.AppendLine("    <div class=\"sidebar-header\">");
        var active = string.IsNullOrWhiteSpace(activeSlug) ? " active" : string.Empty;
        sb.AppendLine($"      <a href=\"{indexUrl}\" class=\"sidebar-title{active}\">");
        sb.AppendLine("        <svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" width=\"18\" height=\"18\">");
        sb.AppendLine("          <path d=\"M4 19.5A2.5 2.5 0 0 1 6.5 17H20\"/>");
        sb.AppendLine("          <path d=\"M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z\"/>");
        sb.AppendLine("        </svg>");
        sb.AppendLine("        <span>API Reference</span>");
        sb.AppendLine("      </a>");
        sb.AppendLine("    </div>");
          var totalTypes = types.Count;
          sb.AppendLine("    <div class=\"sidebar-search\">");
          sb.AppendLine("      <svg viewBox=\"0 0 24 24\" width=\"16\" height=\"16\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">");
          sb.AppendLine("        <circle cx=\"11\" cy=\"11\" r=\"8\"/>");
          sb.AppendLine("        <path d=\"M21 21l-4.35-4.35\"/>");
          sb.AppendLine("      </svg>");
          sb.AppendLine($"      <input id=\"api-filter\" type=\"text\" placeholder=\"Filter types ({totalTypes})...\" />");
          sb.AppendLine("      <button class=\"clear-search\" type=\"button\" aria-label=\"Clear search\">");
          sb.AppendLine("        <svg viewBox=\"0 0 24 24\" width=\"16\" height=\"16\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">");
          sb.AppendLine("          <path d=\"M18 6L6 18M6 6l12 12\"/>");
          sb.AppendLine("        </svg>");
          sb.AppendLine("      </button>");
          sb.AppendLine("    </div>");

          var kindFilters = BuildKindFilters(types);
          if (kindFilters.Count > 0)
          {
              sb.AppendLine("    <div class=\"sidebar-filters\">");
              sb.AppendLine("      <div class=\"filter-label\">Type filters</div>");
              sb.AppendLine("      <div class=\"filter-buttons\">");
              sb.AppendLine("        <button class=\"filter-button active\" type=\"button\" data-kind=\"\">All</button>");
              foreach (var kind in kindFilters)
              {
                  sb.AppendLine($"        <button class=\"filter-button\" type=\"button\" data-kind=\"{kind.Kind}\">{GetKindLabel(kind.Kind, kind.Count)}</button>");
              }
              sb.AppendLine("      </div>");
              var namespaceGroups = types
                  .GroupBy(t => string.IsNullOrWhiteSpace(t.Namespace) ? "(global)" : t.Namespace)
                  .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                  .ToList();
              if (namespaceGroups.Count > 0)
              {
                  sb.AppendLine("      <div class=\"filter-row\">");
                  sb.AppendLine("        <label for=\"api-namespace\" class=\"filter-label\">Namespace</label>");
                  sb.AppendLine("        <select id=\"api-namespace\" class=\"namespace-select\">");
                  sb.AppendLine("          <option value=\"\">All namespaces</option>");
                  foreach (var group in namespaceGroups)
                  {
                      var encoded = System.Web.HttpUtility.HtmlEncode(group.Key);
                      sb.AppendLine($"          <option value=\"{encoded}\">{encoded} ({group.Count()})</option>");
                  }
                  sb.AppendLine("        </select>");
                  sb.AppendLine("      </div>");
              }
              sb.AppendLine("      <div class=\"filter-row\">");
              sb.AppendLine("        <button class=\"sidebar-reset\" type=\"button\">Reset filters</button>");
              sb.AppendLine("      </div>");
              sb.AppendLine("    </div>");
          }
          sb.AppendLine($"    <div class=\"sidebar-count\" data-total=\"{totalTypes}\">Showing {totalTypes} types</div>");
          sb.AppendLine("    <div class=\"sidebar-tools\">");
          sb.AppendLine("      <button class=\"sidebar-expand-all\" type=\"button\">Expand all</button>");
          sb.AppendLine("      <button class=\"sidebar-collapse-all\" type=\"button\">Collapse all</button>");
          sb.AppendLine("    </div>");
          sb.AppendLine("    <nav class=\"sidebar-nav\">");

        var mainTypes = GetMainTypes(types);
        if (mainTypes.Count > 0)
        {
            sb.AppendLine("      <div class=\"nav-section\">");
            sb.AppendLine("        <div class=\"nav-section-header main-api\">");
            sb.AppendLine("          <svg class=\"chevron expanded\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">");
            sb.AppendLine("            <path d=\"M9 18l6-6-6-6\"/>");
            sb.AppendLine("          </svg>");
            sb.AppendLine("          <span>Main API</span>");
            sb.AppendLine($"          <span class=\"type-count\">{mainTypes.Count}</span>");
            sb.AppendLine("        </div>");
            sb.AppendLine("        <div class=\"nav-section-content\">");
            foreach (var type in mainTypes)
            {
                sb.AppendLine(BuildSidebarTypeItem(type, baseUrl, activeSlug));
            }
            sb.AppendLine("        </div>");
            sb.AppendLine("      </div>");
        }

        var grouped = types
            .Where(t => !IsMainType(t.Name))
            .GroupBy(t => string.IsNullOrWhiteSpace(t.Namespace) ? "(global)" : t.Namespace)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var group in grouped)
        {
            sb.AppendLine("      <div class=\"nav-section\">");
            sb.AppendLine("        <div class=\"nav-section-header\">");
            sb.AppendLine("          <svg class=\"chevron expanded\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\">");
            sb.AppendLine("            <path d=\"M9 18l6-6-6-6\"/>");
            sb.AppendLine("          </svg>");
            sb.AppendLine($"          <span>{System.Web.HttpUtility.HtmlEncode(GetShortNamespace(group.Key))}</span>");
            sb.AppendLine($"          <span class=\"type-count\">{group.Count()}</span>");
            sb.AppendLine("        </div>");
            sb.AppendLine("        <div class=\"nav-section-content\">");
            foreach (var type in group.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            {
                sb.AppendLine(BuildSidebarTypeItem(type, baseUrl, activeSlug));
            }
            sb.AppendLine("        </div>");
            sb.AppendLine("      </div>");
        }

          sb.AppendLine("    </nav>");
          sb.AppendLine("    <div class=\"sidebar-empty\" hidden>No matching types.</div>");
          sb.AppendLine("    <div class=\"sidebar-footer\">");
          sb.AppendLine($"      <a href=\"{docsHomeUrl}\" class=\"back-link\">");
        sb.AppendLine("        <svg viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" width=\"14\" height=\"14\">");
        sb.AppendLine("          <path d=\"M19 12H5M12 19l-7-7 7-7\"/>");
        sb.AppendLine("        </svg>");
        sb.AppendLine("        Back to Docs");
        sb.AppendLine("      </a>");
        sb.AppendLine("    </div>");
        return sb.ToString().TrimEnd();
    }

    private static string BuildSidebarTypeItem(ApiTypeModel type, string baseUrl, string activeSlug)
    {
        var active = string.Equals(activeSlug, type.Slug, StringComparison.OrdinalIgnoreCase) ? " active" : string.Empty;
        var summary = StripCrefTokens(type.Summary);
        var search = $"{type.Name} {type.FullName} {summary}".Trim();
        var searchAttr = System.Web.HttpUtility.HtmlEncode(search);
        var name = System.Web.HttpUtility.HtmlEncode(type.Name);
        var kind = NormalizeKind(type.Kind);
        var icon = GetTypeIcon(type.Kind);
        var ns = System.Web.HttpUtility.HtmlEncode(string.IsNullOrWhiteSpace(type.Namespace) ? "(global)" : type.Namespace);
        var href = BuildDocsTypeUrl(baseUrl, type.Slug);
        return $"          <a href=\"{href}\" class=\"type-item{active}\" data-search=\"{searchAttr}\" data-kind=\"{kind}\" data-namespace=\"{ns}\">" +
               $"<span class=\"type-icon {kind}\">{icon}</span><span class=\"type-name\">{name}</span></a>";
    }

    private static string BuildDocsOverview(IReadOnlyList<ApiTypeModel> types, string baseUrl)
    {
        var sb = new StringBuilder();
        sb.AppendLine("    <div class=\"api-overview\">");
        sb.AppendLine("      <h1>API Reference</h1>");
        sb.AppendLine("      <p class=\"lead\">Complete API documentation auto-generated from source documentation.</p>");

        var mainTypes = GetMainTypes(types);
        if (mainTypes.Count > 0)
        {
            sb.AppendLine("      <section class=\"quick-start\">");
            sb.AppendLine("        <h2>Quick Start</h2>");
            sb.AppendLine("        <p class=\"section-desc\">Frequently used types and entry points.</p>");
            sb.AppendLine("        <div class=\"quick-grid\">");
            foreach (var type in mainTypes.Take(6))
            {
                var summary = Truncate(StripCrefTokens(type.Summary), 100);
                var quickHref = BuildDocsTypeUrl(baseUrl, type.Slug);
                sb.AppendLine($"          <a href=\"{quickHref}\" class=\"quick-card\">");
                sb.AppendLine("            <div class=\"quick-card-header\">");
                sb.AppendLine($"              <span class=\"type-icon large {NormalizeKind(type.Kind)}\">{GetTypeIcon(type.Kind)}</span>");
                sb.AppendLine($"              <strong>{System.Web.HttpUtility.HtmlEncode(type.Name)}</strong>");
                sb.AppendLine("            </div>");
                if (!string.IsNullOrWhiteSpace(summary))
                {
                    sb.AppendLine($"            <p>{System.Web.HttpUtility.HtmlEncode(summary)}</p>");
                }
                sb.AppendLine("          </a>");
            }
            sb.AppendLine("        </div>");
            sb.AppendLine("      </section>");
        }

        sb.AppendLine("      <section class=\"all-namespaces\">");
        sb.AppendLine("        <h2>All Namespaces</h2>");
        sb.AppendLine($"        <p class=\"section-desc\">Browse all {types.Count} types organized by namespace.</p>");
        foreach (var group in types.GroupBy(t => string.IsNullOrWhiteSpace(t.Namespace) ? "(global)" : t.Namespace)
                     .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
        {
            sb.AppendLine("        <div class=\"namespace-group\">");
            var nsLabel = System.Web.HttpUtility.HtmlEncode(group.Key);
            sb.AppendLine($"          <h3>{nsLabel} <span class=\"count\">({group.Count()})</span></h3>");
            sb.AppendLine("          <div class=\"type-chips\">");
            foreach (var type in group.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
            {
                var summary = StripCrefTokens(type.Summary);
                var search = $"{type.Name} {type.FullName} {summary}".Trim();
                var searchAttr = System.Web.HttpUtility.HtmlEncode(search);
                var kind = NormalizeKind(type.Kind);
                var nsValue = System.Web.HttpUtility.HtmlEncode(string.IsNullOrWhiteSpace(type.Namespace) ? "(global)" : type.Namespace);
                var chipHref = BuildDocsTypeUrl(baseUrl, type.Slug);
                sb.AppendLine($"            <a href=\"{chipHref}\" class=\"type-chip {kind}\" data-search=\"{searchAttr}\" data-kind=\"{kind}\" data-namespace=\"{nsValue}\">");
                sb.AppendLine($"              <span class=\"chip-icon\">{GetTypeIcon(type.Kind)}</span>");
                sb.AppendLine($"              {System.Web.HttpUtility.HtmlEncode(type.Name)}");
                sb.AppendLine("            </a>");
            }
            sb.AppendLine("          </div>");
            sb.AppendLine("        </div>");
        }
        sb.AppendLine("      </section>");
        sb.AppendLine("    </div>");
        return sb.ToString().TrimEnd();
    }

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

    private static IReadOnlyDictionary<string, string> BuildTypeSlugMap(IReadOnlyList<ApiTypeModel> types)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var shortNameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in types)
        {
            if (!string.IsNullOrWhiteSpace(type.FullName))
                map[type.FullName] = type.Slug;
            if (!string.IsNullOrWhiteSpace(type.Name))
            {
                shortNameCounts.TryGetValue(type.Name, out var count);
                shortNameCounts[type.Name] = count + 1;
            }
        }
        foreach (var type in types)
        {
            if (string.IsNullOrWhiteSpace(type.Name)) continue;
            if (shortNameCounts.TryGetValue(type.Name, out var count) && count == 1)
                map[type.Name] = type.Slug;
        }
        return map;
    }

    private static IReadOnlyDictionary<string, ApiTypeModel> BuildTypeIndex(IReadOnlyList<ApiTypeModel> types)
    {
        var map = new Dictionary<string, ApiTypeModel>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in types)
        {
            var key = NormalizeTypeName(type.FullName);
            if (string.IsNullOrWhiteSpace(key)) continue;
            map[key] = type;
        }
        return map;
    }

    private static IReadOnlyDictionary<string, List<ApiTypeModel>> BuildDerivedTypeMap(
        IReadOnlyList<ApiTypeModel> types,
        IReadOnlyDictionary<string, ApiTypeModel> typeIndex)
    {
        var map = new Dictionary<string, List<ApiTypeModel>>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in types)
        {
            var baseKey = NormalizeTypeName(type.BaseType);
            if (string.IsNullOrWhiteSpace(baseKey)) continue;
            if (!typeIndex.ContainsKey(baseKey)) continue;
            if (!map.TryGetValue(baseKey, out var list))
            {
                list = new List<ApiTypeModel>();
                map[baseKey] = list;
            }
            list.Add(type);
        }

        foreach (var list in map.Values)
        {
            list.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.OrdinalIgnoreCase));
        }
        return map;
    }

    private static List<string> BuildInheritanceChain(ApiTypeModel type, IReadOnlyDictionary<string, ApiTypeModel> typeIndex)
    {
        var chain = new List<string>();
        var current = type.BaseType;
        var guard = 0;
        while (!string.IsNullOrWhiteSpace(current) && guard++ < 32)
        {
            chain.Add(current);
            var key = NormalizeTypeName(current);
            if (!typeIndex.TryGetValue(key, out var baseType) || string.IsNullOrWhiteSpace(baseType.BaseType))
                break;
            if (string.Equals(baseType.BaseType, current, StringComparison.OrdinalIgnoreCase))
                break;
            current = baseType.BaseType;
        }
        if (chain.Count == 0 && string.Equals(type.Kind, "Class", StringComparison.OrdinalIgnoreCase))
            chain.Add("System.Object");
        chain.Reverse();
        return chain;
    }

    private static List<ApiTypeModel> GetDerivedTypes(
        ApiTypeModel type,
        IReadOnlyDictionary<string, List<ApiTypeModel>> derivedMap)
    {
        var key = NormalizeTypeName(type.FullName);
        if (string.IsNullOrWhiteSpace(key)) return new List<ApiTypeModel>();
        return derivedMap.TryGetValue(key, out var list)
            ? list
            : new List<ApiTypeModel>();
    }

    private static List<(string id, string label)> BuildTypeToc(ApiTypeModel type, bool hasInheritance, bool hasDerived)
    {
        var list = new List<(string id, string label)>
        {
            ("overview", "Overview")
        };
        if (hasInheritance)
            list.Add(("inheritance", "Inheritance"));
        if (hasDerived)
            list.Add(("derived-types", "Derived Types"));
        if (!string.IsNullOrWhiteSpace(type.Remarks))
            list.Add(("remarks", "Remarks"));
        if (type.TypeParameters.Count > 0)
            list.Add(("type-parameters", "Type Parameters"));
        if (type.Examples.Count > 0)
            list.Add(("examples", "Examples"));
        if (type.SeeAlso.Count > 0)
            list.Add(("see-also", "See Also"));
        if (type.Constructors.Count > 0)
            list.Add(("constructors", "Constructors"));
        if (type.Methods.Count > 0)
            list.Add(("methods", "Methods"));
        if (type.Properties.Count > 0)
            list.Add(("properties", "Properties"));
        if (type.Fields.Count > 0)
            list.Add((type.Kind == "Enum" ? "values" : "fields", type.Kind == "Enum" ? "Values" : "Fields"));
        if (type.Events.Count > 0)
            list.Add(("events", "Events"));
        if (type.ExtensionMethods.Count > 0)
            list.Add(("extensions", "Extension Methods"));
        return list;
    }

    private static string RenderLinkedText(string text, string baseUrl, IReadOnlyDictionary<string, string> slugMap)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var encoded = System.Web.HttpUtility.HtmlEncode(text);
        var linked = CrefTokenRegex.Replace(encoded, match =>
        {
            var name = match.Groups["name"].Value;
            return LinkifyType(name, baseUrl, slugMap);
        });
        linked = HrefTokenRegex.Replace(linked, match =>
        {
            var href = TryDecodeHrefToken(match.Groups["url"].Value);
            var label = TryDecodeHrefToken(match.Groups["label"].Value);
            if (string.IsNullOrWhiteSpace(href))
                return System.Web.HttpUtility.HtmlEncode(label);

            var safeHref = System.Web.HttpUtility.HtmlAttributeEncode(href);
            var safeLabel = System.Web.HttpUtility.HtmlEncode(string.IsNullOrWhiteSpace(label) ? href : label);
            if (IsExternal(href))
                return $"<a href=\"{safeHref}\" target=\"_blank\" rel=\"noopener\">{safeLabel}</a>";
            return $"<a href=\"{safeHref}\">{safeLabel}</a>";
        });
        return linked;
    }

    private static string StripCrefTokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var cleaned = CrefTokenRegex.Replace(text, match =>
        {
            var name = match.Groups["name"].Value;
            return GetDisplayTypeName(name);
        });
        cleaned = HrefTokenRegex.Replace(cleaned, match =>
        {
            var href = TryDecodeHrefToken(match.Groups["url"].Value);
            var label = TryDecodeHrefToken(match.Groups["label"].Value);
            if (!string.IsNullOrWhiteSpace(label))
                return label;
            return href;
        });
        return cleaned;
    }

    private static string TryDecodeHrefToken(string encoded)
    {
        if (string.IsNullOrWhiteSpace(encoded))
            return string.Empty;
        try
        {
            return Uri.UnescapeDataString(encoded);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string LinkifyType(string? name, string baseUrl, IReadOnlyDictionary<string, string> slugMap)
    {
        if (string.IsNullOrWhiteSpace(name))
            return string.Empty;
        var cleaned = name.Replace("+", ".").Trim();
        var display = GetDisplayTypeName(cleaned);
        if (slugMap.TryGetValue(cleaned, out var slug))
        {
            var href = BuildDocsTypeUrl(baseUrl, slug);
            var safeHref = System.Web.HttpUtility.HtmlAttributeEncode(href);
            return $"<a href=\"{safeHref}\">{System.Web.HttpUtility.HtmlEncode(display)}</a>";
        }
        if (slugMap.TryGetValue(display, out var shortSlug))
        {
            var href = BuildDocsTypeUrl(baseUrl, shortSlug);
            var safeHref = System.Web.HttpUtility.HtmlAttributeEncode(href);
            return $"<a href=\"{safeHref}\">{System.Web.HttpUtility.HtmlEncode(display)}</a>";
        }
        return System.Web.HttpUtility.HtmlEncode(display);
    }

    private static string RenderSourceLink(ApiSourceLink link)
    {
        var suffix = link.Line > 0 ? $":{link.Line}" : string.Empty;
        var label = System.Web.HttpUtility.HtmlEncode($"{link.Path}{suffix}");
        if (!string.IsNullOrWhiteSpace(link.Url))
        {
            var href = System.Web.HttpUtility.HtmlAttributeEncode(link.Url);
            return $"<a href=\"{href}\" target=\"_blank\" rel=\"noopener\">{label}</a>";
        }
        return $"<code>{label}</code>";
    }

    private static string? RenderTypeSourceAction(ApiSourceLink? link)
    {
        if (link is null || string.IsNullOrWhiteSpace(link.Url))
            return null;

        if (TryBuildGitHubEditUrl(link.Url, out var editUrl))
        {
            var href = System.Web.HttpUtility.HtmlAttributeEncode(editUrl);
            return $"<a class=\"type-source-action\" href=\"{href}\" target=\"_blank\" rel=\"noopener\">Edit on GitHub</a>";
        }

        var sourceHref = System.Web.HttpUtility.HtmlAttributeEncode(link.Url);
        return $"<a class=\"type-source-action\" href=\"{sourceHref}\" target=\"_blank\" rel=\"noopener\">View source</a>";
    }

    private static bool TryBuildGitHubEditUrl(string sourceUrl, out string editUrl)
    {
        editUrl = string.Empty;
        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri))
            return false;
        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
            return false;

        var path = uri.AbsolutePath;
        var blobMarker = "/blob/";
        var blobIndex = path.IndexOf(blobMarker, StringComparison.OrdinalIgnoreCase);
        if (blobIndex < 0)
            return false;

        var repoPath = path.Substring(0, blobIndex);
        var filePath = path.Substring(blobIndex + blobMarker.Length);
        if (string.IsNullOrWhiteSpace(repoPath) || string.IsNullOrWhiteSpace(filePath))
            return false;

        editUrl = $"{uri.Scheme}://{uri.Host}{repoPath}/edit/{filePath}";
        return true;
    }

    private static Dictionary<string, object?>? BuildSourceJson(ApiSourceLink? source)
    {
        if (source is null) return null;
        return new Dictionary<string, object?>
        {
            ["path"] = source.Path,
            ["line"] = source.Line,
            ["url"] = source.Url
        };
    }

    private static string GetDisplayTypeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;
        var normalized = name.Replace("{", "<").Replace("}", ">");
        normalized = GenericArityRegex.Replace(normalized, string.Empty);
        var lastDot = normalized.LastIndexOf('.');
        return lastDot >= 0 ? normalized.Substring(lastDot + 1) : normalized;
    }

    private static IReadOnlyList<ApiTypeModel> GetMainTypes(IReadOnlyList<ApiTypeModel> types)
    {
        var results = new List<ApiTypeModel>();
        foreach (var name in MainTypeOrder)
        {
            var type = types.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
            if (type != null)
                results.Add(type);
        }
        return results;
    }

    private static bool IsMainType(string name)
        => MainTypeOrder.Contains(name, StringComparer.OrdinalIgnoreCase);

    private static string GetShortNamespace(string ns)
    {
        if (string.IsNullOrWhiteSpace(ns)) return "(global)";
        var parts = ns.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 0 ? ns : parts[^1];
    }

    private static string GetTypeIcon(string? kind)
        => kind switch
        {
            "Class" => "C",
            "Struct" => "S",
            "Interface" => "I",
            "Enum" => "E",
            "Delegate" => "D",
            _ => "T"
        };

    private static readonly string[] KindOrder = { "class", "struct", "interface", "enum", "delegate" };

      private static List<KindFilter> BuildKindFilters(IReadOnlyList<ApiTypeModel> types)
      {
          var available = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
          foreach (var type in types)
          {
              var kind = NormalizeKind(type.Kind);
              available.TryGetValue(kind, out var count);
              available[kind] = count + 1;
          }
          return KindOrder
              .Where(k => available.ContainsKey(k))
              .Select(k => new KindFilter(k, available[k]))
              .ToList();
      }

      private static string GetKindLabel(string kind, int count)
          => kind switch
          {
              "class" => $"Classes ({count})",
              "struct" => $"Structs ({count})",
              "interface" => $"Interfaces ({count})",
              "enum" => $"Enums ({count})",
              "delegate" => $"Delegates ({count})",
              _ => $"Types ({count})"
          };

      private static string NormalizeKind(string? kind)
          => string.IsNullOrWhiteSpace(kind) ? "class" : kind.ToLowerInvariant();

      private sealed class KindFilter
      {
          public KindFilter(string kind, int count)
          {
              Kind = kind;
              Count = count;
          }
          public string Kind { get; }
          public int Count { get; }
      }

    private static string Truncate(string? value, int length)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        return value.Length <= length ? value : value.Substring(0, length).Trim() + "...";
    }

    private static void GenerateApiSitemap(string outputPath, string baseUrl, IReadOnlyList<ApiTypeModel> types)
    {
        var sb = new StringBuilder();
        var baseTrim = baseUrl.TrimEnd('/');
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        foreach (var type in types)
        {
            sb.AppendLine("  <url>");
            sb.AppendLine($"    <loc>{baseTrim}/types/{type.Slug}.html</loc>");
            sb.AppendLine($"    <lastmod>{today}</lastmod>");
            sb.AppendLine("    <changefreq>monthly</changefreq>");
            sb.AppendLine("    <priority>0.5</priority>");
            sb.AppendLine("  </url>");
        }
        sb.AppendLine("</urlset>");
        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
    }

    private static void GenerateDocsSitemap(string outputPath, string baseUrl, IReadOnlyList<ApiTypeModel> types)
    {
        var sb = new StringBuilder();
        var baseTrim = baseUrl.TrimEnd('/');
        var today = DateTime.UtcNow.ToString("yyyy-MM-dd");
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");
        foreach (var type in types)
        {
            sb.AppendLine("  <url>");
            sb.AppendLine($"    <loc>{baseTrim}/{type.Slug}/</loc>");
            sb.AppendLine($"    <lastmod>{today}</lastmod>");
            sb.AppendLine("    <changefreq>monthly</changefreq>");
            sb.AppendLine("    <priority>0.5</priority>");
            sb.AppendLine("  </url>");
        }
        sb.AppendLine("</urlset>");
        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
    }

      private static string BuildDocsTypeUrl(string baseUrl, string slug)
      {
          var baseTrim = baseUrl.TrimEnd('/');
          return EnsureTrailingSlash($"{baseTrim}/{slug}");
      }

      private static string BuildSidebarClass(string? position)
      {
          if (string.IsNullOrWhiteSpace(position))
              return string.Empty;
          var normalized = position.Trim().ToLowerInvariant();
          return normalized == "right" ? " sidebar-right" : string.Empty;
      }

    private static string EnsureTrailingSlash(string url)
        => url.EndsWith("/", StringComparison.Ordinal) ? url : $"{url}/";

      private static string NormalizeDocsHomeUrl(string? url)
      {
          if (string.IsNullOrWhiteSpace(url))
              return "/docs/";
        var trimmed = url.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return EnsureTrailingSlash(trimmed);
        if (!trimmed.StartsWith("/"))
            trimmed = "/" + trimmed;
          return EnsureTrailingSlash(trimmed);
      }

      private static string ResolveBodyClass(string? value)
      {
          var trimmed = value?.Trim();
          if (string.IsNullOrWhiteSpace(trimmed))
              return "pf-api-docs";
          return trimmed;
      }

    private static string LoadOptionalHtml(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) return string.Empty;
        return File.ReadAllText(full);
    }

    private static void ApplyNavTokens(WebApiDocsOptions options, ref string header, ref string footer)
    {
        if (string.IsNullOrWhiteSpace(options.NavJsonPath)) return;
        var nav = LoadNavConfig(options);
        if (nav is null) return;

        var tokens = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["SITE_NAME"] = nav.SiteName,
            ["BRAND_NAME"] = nav.SiteName,
            ["BRAND_URL"] = nav.BrandUrl,
            ["BRAND_ICON"] = nav.BrandIcon,
            ["NAV_LINKS"] = BuildLinkHtml(nav.Primary),
            ["NAV_ACTIONS"] = BuildActionHtml(nav.Actions),
            ["FOOTER_PRODUCT"] = BuildLinkHtml(nav.FooterProduct),
            ["FOOTER_RESOURCES"] = BuildLinkHtml(nav.FooterResources),
            ["FOOTER_COMPANY"] = BuildLinkHtml(nav.FooterCompany),
            ["YEAR"] = DateTime.UtcNow.Year.ToString()
        };

        if (!string.IsNullOrWhiteSpace(header))
            header = ApplyTemplate(header, tokens);
        if (!string.IsNullOrWhiteSpace(footer))
            footer = ApplyTemplate(footer, tokens);
    }

    private static string BuildLinkHtml(IReadOnlyList<NavItem> items)
    {
        if (items.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Href) || string.IsNullOrWhiteSpace(item.Text))
                continue;
            var href = System.Web.HttpUtility.HtmlEncode(item.Href);
            var text = System.Web.HttpUtility.HtmlEncode(item.Text);
            var target = item.Target;
            var rel = item.Rel;
            if (string.IsNullOrWhiteSpace(target) && item.External)
                target = "_blank";
            if (string.IsNullOrWhiteSpace(rel) && item.External)
                rel = "noopener";

            sb.Append("<a href=\"").Append(href).Append("\"");
            if (!string.IsNullOrWhiteSpace(target))
                sb.Append(" target=\"").Append(System.Web.HttpUtility.HtmlEncode(target)).Append("\"");
            if (!string.IsNullOrWhiteSpace(rel))
                sb.Append(" rel=\"").Append(System.Web.HttpUtility.HtmlEncode(rel)).Append("\"");
            sb.Append(">").Append(text).Append("</a>");
        }
        return sb.ToString();
    }

    private static string BuildActionHtml(IReadOnlyList<NavAction> actions)
    {
        if (actions.Count == 0) return string.Empty;
        var sb = new StringBuilder();
        foreach (var action in actions)
        {
            var isButton = string.Equals(action.Kind, "button", StringComparison.OrdinalIgnoreCase);
            if (!isButton && string.IsNullOrWhiteSpace(action.Href))
                continue;

            var title = action.Title;
            var ariaLabel = string.IsNullOrWhiteSpace(action.AriaLabel) ? title : action.AriaLabel;
            var iconHtml = string.IsNullOrWhiteSpace(action.IconHtml) ? null : action.IconHtml;
            var text = string.IsNullOrWhiteSpace(action.Text) ? null : action.Text;
            var hasIcon = !string.IsNullOrWhiteSpace(iconHtml);
            if (text is null && !hasIcon && !string.IsNullOrWhiteSpace(title))
                text = title;

            if (isButton)
            {
                sb.Append("<button type=\"button\"");
                if (!string.IsNullOrWhiteSpace(action.CssClass))
                    sb.Append(" class=\"").Append(System.Web.HttpUtility.HtmlEncode(action.CssClass)).Append("\"");
                if (!string.IsNullOrWhiteSpace(title))
                    sb.Append(" title=\"").Append(System.Web.HttpUtility.HtmlEncode(title)).Append("\"");
                if (!string.IsNullOrWhiteSpace(ariaLabel))
                    sb.Append(" aria-label=\"").Append(System.Web.HttpUtility.HtmlEncode(ariaLabel)).Append("\"");
                sb.Append(">");
                if (hasIcon)
                    sb.Append(iconHtml);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    if (hasIcon) sb.Append(" ");
                    sb.Append(System.Web.HttpUtility.HtmlEncode(text));
                }
                sb.Append("</button>");
                continue;
            }

            var href = System.Web.HttpUtility.HtmlEncode(action.Href ?? string.Empty);
            var external = action.External || IsExternal(action.Href ?? string.Empty);
            var target = action.Target;
            var rel = action.Rel;
            if (external && string.IsNullOrWhiteSpace(target))
                target = "_blank";
            if (external && string.IsNullOrWhiteSpace(rel))
                rel = "noopener";

            sb.Append("<a href=\"").Append(href).Append("\"");
            if (!string.IsNullOrWhiteSpace(action.CssClass))
                sb.Append(" class=\"").Append(System.Web.HttpUtility.HtmlEncode(action.CssClass)).Append("\"");
            if (!string.IsNullOrWhiteSpace(target))
                sb.Append(" target=\"").Append(System.Web.HttpUtility.HtmlEncode(target)).Append("\"");
            if (!string.IsNullOrWhiteSpace(rel))
                sb.Append(" rel=\"").Append(System.Web.HttpUtility.HtmlEncode(rel)).Append("\"");
            if (!string.IsNullOrWhiteSpace(title))
                sb.Append(" title=\"").Append(System.Web.HttpUtility.HtmlEncode(title)).Append("\"");
            if (!string.IsNullOrWhiteSpace(ariaLabel))
                sb.Append(" aria-label=\"").Append(System.Web.HttpUtility.HtmlEncode(ariaLabel)).Append("\"");
            sb.Append(">");
            if (hasIcon)
                sb.Append(iconHtml);
            if (!string.IsNullOrWhiteSpace(text))
            {
                if (hasIcon) sb.Append(" ");
                sb.Append(System.Web.HttpUtility.HtmlEncode(text));
            }
            sb.Append("</a>");
        }
        return sb.ToString();
    }

    private static string LoadEmbeddedRaw(string fileName)
    {
        var assembly = typeof(WebApiDocsGenerator).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith($"Assets.ApiDocs.{fileName}", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(resourceName)) return string.Empty;
        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null) return string.Empty;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string LoadTemplate(WebApiDocsOptions options, string fileName, string? explicitPath)
    {
        var content = LoadFileText(explicitPath);
        if (!string.IsNullOrWhiteSpace(content)) return content;
        if (!string.IsNullOrWhiteSpace(options.TemplateRootPath))
        {
            var candidate = Path.Combine(Path.GetFullPath(options.TemplateRootPath), fileName);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        return LoadEmbeddedRaw(fileName);
    }

    private static string LoadAsset(WebApiDocsOptions options, string fileName, string? explicitPath)
    {
        var content = LoadFileText(explicitPath);
        if (!string.IsNullOrWhiteSpace(content)) return content;
        if (!string.IsNullOrWhiteSpace(options.TemplateRootPath))
        {
            var candidate = Path.Combine(Path.GetFullPath(options.TemplateRootPath), fileName);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
        }
        return LoadEmbeddedRaw(fileName);
    }

    private static string LoadFileText(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) return string.Empty;
        return File.ReadAllText(full);
    }

    private static NavConfig? LoadNavConfig(WebApiDocsOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.NavJsonPath)) return null;
        var path = Path.GetFullPath(options.NavJsonPath);
        if (!File.Exists(path)) return null;

        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var nav = new NavConfig
        {
            SiteName = options.SiteName ?? string.Empty,
            BrandUrl = string.IsNullOrWhiteSpace(options.BrandUrl) ? "/" : options.BrandUrl,
            BrandIcon = string.IsNullOrWhiteSpace(options.BrandIcon) ? "/codeglyphx-qr-icon.png" : options.BrandIcon
        };

        if (root.TryGetProperty("Name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
            nav.SiteName = nameProp.GetString() ?? nav.SiteName;
        if (root.TryGetProperty("siteName", out var siteProp) && siteProp.ValueKind == JsonValueKind.String)
            nav.SiteName = siteProp.GetString() ?? nav.SiteName;

        if (root.TryGetProperty("Head", out var headProp) && headProp.ValueKind == JsonValueKind.Object)
        {
            if (headProp.TryGetProperty("Links", out var linksProp) && linksProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var link in linksProp.EnumerateArray())
                {
                    if (!link.TryGetProperty("Rel", out var relProp) || relProp.ValueKind != JsonValueKind.String)
                        continue;
                    var rel = relProp.GetString() ?? string.Empty;
                    if (!rel.Equals("icon", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!link.TryGetProperty("Href", out var hrefProp) || hrefProp.ValueKind != JsonValueKind.String)
                        continue;
                    var href = hrefProp.GetString();
                    if (!string.IsNullOrWhiteSpace(href))
                    {
                        nav.BrandIcon = href;
                        break;
                    }
                }
            }
        }

        if (root.TryGetProperty("Navigation", out var navProp) && navProp.ValueKind == JsonValueKind.Object)
        {
            ParseSiteNavigation(navProp, nav);
            return nav;
        }

        if (root.TryGetProperty("primary", out var primaryProp) && primaryProp.ValueKind == JsonValueKind.Array)
        {
            nav.Primary = ParseNavItems(primaryProp);
        }

        if (root.TryGetProperty("footer", out var footerProp) && footerProp.ValueKind == JsonValueKind.Object)
        {
            if (footerProp.TryGetProperty("product", out var productProp) && productProp.ValueKind == JsonValueKind.Array)
                nav.FooterProduct = ParseNavItems(productProp);
            if (footerProp.TryGetProperty("resources", out var resourcesProp) && resourcesProp.ValueKind == JsonValueKind.Array)
                nav.FooterResources = ParseNavItems(resourcesProp);
            if (footerProp.TryGetProperty("company", out var companyProp) && companyProp.ValueKind == JsonValueKind.Array)
                nav.FooterCompany = ParseNavItems(companyProp);
        }

        if (root.TryGetProperty("actions", out var actionsProp) && actionsProp.ValueKind == JsonValueKind.Array)
            nav.Actions = ParseSiteNavActions(actionsProp);

        return nav;
    }

    private static void ParseSiteNavigation(JsonElement navElement, NavConfig nav)
    {
        if (navElement.TryGetProperty("Menus", out var menusProp) && menusProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var menu in menusProp.EnumerateArray())
            {
                if (!menu.TryGetProperty("Name", out var nameProp) || nameProp.ValueKind != JsonValueKind.String)
                    continue;
                var name = nameProp.GetString() ?? string.Empty;
                if (!menu.TryGetProperty("Items", out var itemsProp) || itemsProp.ValueKind != JsonValueKind.Array)
                    continue;

                var items = ParseSiteNavItems(itemsProp);
                if (name.Equals("main", StringComparison.OrdinalIgnoreCase))
                    nav.Primary = items;
                else if (name.Equals("footer-product", StringComparison.OrdinalIgnoreCase))
                    nav.FooterProduct = items;
                else if (name.Equals("footer-resources", StringComparison.OrdinalIgnoreCase))
                    nav.FooterResources = items;
                else if (name.Equals("footer-company", StringComparison.OrdinalIgnoreCase))
                    nav.FooterCompany = items;
            }
        }

        if (navElement.TryGetProperty("Actions", out var actionsProp) && actionsProp.ValueKind == JsonValueKind.Array)
            nav.Actions = ParseSiteNavActions(actionsProp);
    }

    private static List<NavItem> ParseNavItems(JsonElement itemsProp)
    {
        var list = new List<NavItem>();
        foreach (var item in itemsProp.EnumerateArray())
        {
            var href = item.TryGetProperty("href", out var hrefProp) && hrefProp.ValueKind == JsonValueKind.String
                ? hrefProp.GetString()
                : null;
            var text = item.TryGetProperty("text", out var textProp) && textProp.ValueKind == JsonValueKind.String
                ? textProp.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(text))
                continue;
            list.Add(new NavItem(href!, text!, IsExternal(href!)));
        }
        return list;
    }

    private static List<NavItem> ParseSiteNavItems(JsonElement itemsProp)
    {
        var list = new List<NavItem>();
        foreach (var item in itemsProp.EnumerateArray())
        {
            var href = item.TryGetProperty("Url", out var urlProp) && urlProp.ValueKind == JsonValueKind.String
                ? urlProp.GetString()
                : null;
            var text = item.TryGetProperty("Title", out var titleProp) && titleProp.ValueKind == JsonValueKind.String
                ? titleProp.GetString()
                : null;
            if (string.IsNullOrWhiteSpace(href) || string.IsNullOrWhiteSpace(text))
                continue;

            var target = item.TryGetProperty("Target", out var targetProp) && targetProp.ValueKind == JsonValueKind.String
                ? targetProp.GetString()
                : null;
            var rel = item.TryGetProperty("Rel", out var relProp) && relProp.ValueKind == JsonValueKind.String
                ? relProp.GetString()
                : null;
            var external = item.TryGetProperty("External", out var extProp) && extProp.ValueKind == JsonValueKind.True;
            external |= IsExternal(href!);
            list.Add(new NavItem(href!, text!, external, target, rel));
        }
        return list;
    }

    private static List<NavAction> ParseSiteNavActions(JsonElement itemsProp)
    {
        var list = new List<NavAction>();
        foreach (var item in itemsProp.EnumerateArray())
        {
            var href = ReadString(item, "Url", "href");
            var title = ReadString(item, "Title", "title");
            var text = ReadString(item, "Text", "text");
            var iconHtml = ReadString(item, "IconHtml", "iconHtml", "Icon", "icon");
            var cssClass = ReadString(item, "CssClass", "class");
            var kind = ReadString(item, "Kind", "kind");
            var ariaLabel = ReadString(item, "AriaLabel", "ariaLabel", "aria");
            var target = ReadString(item, "Target", "target");
            var rel = ReadString(item, "Rel", "rel");
            var external = ReadBool(item, "External", "external");
            if (!string.IsNullOrWhiteSpace(href))
                external |= IsExternal(href);

            list.Add(new NavAction(href, text, title, ariaLabel, iconHtml, cssClass, kind, external, target, rel));
        }
        return list;
    }

    private static string? ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
                return prop.GetString();
        }
        return null;
    }

    private static bool ReadBool(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.True)
                return true;
        }
        return false;
    }

    private static bool IsExternal(string href)
    {
        if (string.IsNullOrWhiteSpace(href)) return false;
        return Uri.TryCreate(href, UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }

    private static string WrapStyle(string content)
        => string.IsNullOrWhiteSpace(content) ? string.Empty : $"<style>{content}</style>";

    private static string WrapScript(string content)
        => string.IsNullOrWhiteSpace(content) ? string.Empty : $"<script>{content}</script>";

    private static string ApplyTemplate(string template, IReadOnlyDictionary<string, string?> replacements)
    {
        if (string.IsNullOrWhiteSpace(template)) return string.Empty;
        var result = template;
        foreach (var kvp in replacements)
        {
            result = result.Replace($"{{{{{kvp.Key}}}}}", kvp.Value ?? string.Empty);
        }
        return result;
    }

    private sealed class ApiDocModel
    {
        public string? AssemblyName { get; set; }
        public string? AssemblyVersion { get; set; }
        public Dictionary<string, ApiTypeModel> Types { get; } = new(StringComparer.Ordinal);
    }

    private sealed class ApiTypeModel
    {
        public string Name { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Namespace { get; set; } = string.Empty;
        public string? Assembly { get; set; }
        public ApiSourceLink? Source { get; set; }
        public string? BaseType { get; set; }
        public List<string> Interfaces { get; } = new();
        public List<string> Attributes { get; } = new();
        public string? Summary { get; set; }
        public string? Remarks { get; set; }
        public List<ApiTypeParameterModel> TypeParameters { get; } = new();
        public List<ApiExampleModel> Examples { get; } = new();
        public List<string> SeeAlso { get; } = new();
        public string Kind { get; set; } = "Class";
        public string Slug { get; set; } = string.Empty;
        public bool IsStatic { get; set; }
        public bool IsAbstract { get; set; }
        public bool IsSealed { get; set; }
        public List<ApiMemberModel> Methods { get; } = new();
        public List<ApiMemberModel> Constructors { get; } = new();
        public List<ApiMemberModel> Properties { get; } = new();
        public List<ApiMemberModel> Fields { get; } = new();
        public List<ApiMemberModel> Events { get; } = new();
        public List<ApiMemberModel> ExtensionMethods { get; } = new();
    }

    private sealed class ApiMemberModel
    {
        public string Name { get; set; } = string.Empty;
        public string? DisplayName { get; set; }
        public string? Summary { get; set; }
        public string? Kind { get; set; }
        public string? Signature { get; set; }
        public string? ReturnType { get; set; }
        public string? DeclaringType { get; set; }
        public bool IsInherited { get; set; }
        public bool IsStatic { get; set; }
        public string? Access { get; set; }
        public List<string> Modifiers { get; } = new();
        public string? Value { get; set; }
        public string? ValueSummary { get; set; }
        public bool IsConstructor { get; set; }
        public bool IsExtension { get; set; }
        public List<string> Attributes { get; } = new();
        public List<ApiTypeParameterModel> TypeParameters { get; } = new();
        public List<ApiExampleModel> Examples { get; } = new();
        public List<ApiExceptionModel> Exceptions { get; } = new();
        public List<string> SeeAlso { get; } = new();
        public List<ApiParameterModel> Parameters { get; set; } = new();
        public string? Returns { get; set; }
        public ApiSourceLink? Source { get; set; }
    }

    private sealed class ApiParameterModel
    {
        public string Name { get; set; } = string.Empty;
        public string? Type { get; set; }
        public string? Summary { get; set; }
        public bool IsOptional { get; set; }
        public string? DefaultValue { get; set; }
    }

    private sealed class ApiTypeParameterModel
    {
        public string Name { get; set; } = string.Empty;
        public string? Summary { get; set; }
    }

    private sealed class ApiExampleModel
    {
        public string Kind { get; set; } = "text";
        public string Text { get; set; } = string.Empty;
    }

    private sealed class ApiSourceLink
    {
        public string Path { get; set; } = string.Empty;
        public int Line { get; set; }
        public string? Url { get; set; }
    }

    private sealed class SourceLinkContext : IDisposable
    {
        private readonly MetadataReaderProvider _provider;
        private readonly Stream _stream;
        private readonly MetadataReader _reader;
        private readonly string? _sourceRoot;
        private readonly string? _pattern;

        private SourceLinkContext(MetadataReaderProvider provider, Stream stream, string? sourceRoot, string? pattern)
        {
            _provider = provider;
            _stream = stream;
            _reader = provider.GetMetadataReader();
            _sourceRoot = sourceRoot;
            _pattern = pattern;
        }

        public static SourceLinkContext? Create(WebApiDocsOptions options, Assembly assembly, List<string> warnings)
        {
            if (string.IsNullOrWhiteSpace(options.SourceUrlPattern) && string.IsNullOrWhiteSpace(options.SourceRootPath))
                return null;

            var assemblyPath = options.AssemblyPath;
            if (string.IsNullOrWhiteSpace(assemblyPath))
                assemblyPath = assembly.Location;
            if (string.IsNullOrWhiteSpace(assemblyPath))
            {
                warnings.Add("Source links disabled: assembly path not available.");
                return null;
            }

            var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
            if (!File.Exists(pdbPath))
            {
                warnings.Add($"Source links disabled: PDB not found at {pdbPath}.");
                return null;
            }

            try
            {
                var stream = File.OpenRead(pdbPath);
                var provider = MetadataReaderProvider.FromPortablePdbStream(stream);

                string? root = null;
                if (!string.IsNullOrWhiteSpace(options.SourceRootPath))
                {
                    root = Path.GetFullPath(options.SourceRootPath);
                }
                else if (!string.IsNullOrWhiteSpace(options.SourceUrlPattern))
                {
                    // If the project lives in a subfolder of a repo, using the git root as SourceRootPath
                    // keeps generated URLs consistent (and avoids missing prefixes like "IntelligenceX/...").
                    root = TryFindGitRoot(assemblyPath);
                }

                var pattern = options.SourceUrlPattern;
                if (string.IsNullOrWhiteSpace(root) && !string.IsNullOrWhiteSpace(pattern))
                {
                    warnings.Add("SourceUrlPattern set without SourceRootPath (and git root not found); source URLs will be omitted.");
                    pattern = null;
                }

                return new SourceLinkContext(provider, stream, root, pattern);
            }
            catch (Exception ex)
            {
                warnings.Add($"Source links disabled: {ex.Message}");
                return null;
            }
        }

        public ApiSourceLink? TryGetSource(Type type)
        {
            foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                var link = TryGetSource(ctor);
                if (link is not null) return link;
            }
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (method.IsSpecialName) continue;
                var link = TryGetSource(method);
                if (link is not null) return link;
            }
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                var link = TryGetSource(property);
                if (link is not null) return link;
            }
            foreach (var evt in type.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                var link = TryGetSource(evt);
                if (link is not null) return link;
            }
            return null;
        }

        public ApiSourceLink? TryGetSource(MethodBase method)
        {
            if (method is null || method.MetadataToken == 0) return null;
            try
            {
                var handle = MetadataTokens.MethodDefinitionHandle(method.MetadataToken);
                var debugInfo = _reader.GetMethodDebugInformation(handle);
                foreach (var sp in debugInfo.GetSequencePoints())
                {
                    if (sp.IsHidden) continue;
                    var document = _reader.GetDocument(sp.Document);
                    var path = _reader.GetString(document.Name);
                    return BuildSourceLink(path, sp.StartLine);
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Source mapping failed for {method.DeclaringType?.FullName}.{method.Name}: {ex.GetType().Name}: {ex.Message}");
            }
            return null;
        }

        public ApiSourceLink? TryGetSource(PropertyInfo property)
        {
            var accessor = property.GetGetMethod(true) ?? property.GetSetMethod(true);
            return accessor is null ? null : TryGetSource(accessor);
        }

        public ApiSourceLink? TryGetSource(EventInfo evt)
        {
            var accessor = evt.GetAddMethod(true) ?? evt.GetRemoveMethod(true);
            return accessor is null ? null : TryGetSource(accessor);
        }

        public ApiSourceLink? TryGetSource(FieldInfo field)
        {
            return null;
        }

        private ApiSourceLink? BuildSourceLink(string path, int line)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var resolved = path;
            if (!string.IsNullOrWhiteSpace(_sourceRoot))
            {
                try
                {
                    resolved = Path.GetRelativePath(_sourceRoot, path);
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"Source path relativize failed: {ex.GetType().Name}: {ex.Message}");
                    resolved = path;
                }
            }
            resolved = resolved.Replace('\\', '/');
            var url = string.IsNullOrWhiteSpace(_pattern)
                ? null
                : _pattern.Replace("{path}", resolved).Replace("{line}", line.ToString());
            return new ApiSourceLink { Path = resolved, Line = line, Url = url };
        }

        public void Dispose()
        {
            _provider.Dispose();
            _stream.Dispose();
        }

        private static string? TryFindGitRoot(string path)
        {
            try
            {
                var current = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
                while (!string.IsNullOrWhiteSpace(current))
                {
                    var git = Path.Combine(current, ".git");
                    if (Directory.Exists(git) || File.Exists(git))
                        return current;

                    var parent = Path.GetDirectoryName(current);
                    if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                        break;
                    current = parent;
                }
            }
            catch
            {
                // best-effort
            }
            return null;
        }
    }

    private sealed class ApiExceptionModel
    {
        public string Type { get; set; } = string.Empty;
        public string? Summary { get; set; }
    }

    private sealed class NavConfig
    {
        public string SiteName { get; set; } = "Site";
        public string BrandUrl { get; set; } = "/";
        public string BrandIcon { get; set; } = "/codeglyphx-qr-icon.png";
        public List<NavItem> Primary { get; set; } = new();
        public List<NavAction> Actions { get; set; } = new();
        public List<NavItem> FooterProduct { get; set; } = new();
        public List<NavItem> FooterResources { get; set; } = new();
        public List<NavItem> FooterCompany { get; set; } = new();
    }

    private sealed class NavAction
    {
        public NavAction(
            string? href,
            string? text,
            string? title,
            string? ariaLabel,
            string? iconHtml,
            string? cssClass,
            string? kind,
            bool external,
            string? target,
            string? rel)
        {
            Href = href;
            Text = text;
            Title = title;
            AriaLabel = ariaLabel;
            IconHtml = iconHtml;
            CssClass = cssClass;
            Kind = kind;
            External = external;
            Target = target;
            Rel = rel;
        }

        public string? Href { get; }
        public string? Text { get; }
        public string? Title { get; }
        public string? AriaLabel { get; }
        public string? IconHtml { get; }
        public string? CssClass { get; }
        public string? Kind { get; }
        public bool External { get; }
        public string? Target { get; }
        public string? Rel { get; }
    }

    private sealed class NavItem
    {
        public NavItem(string href, string text, bool external, string? target = null, string? rel = null)
        {
            Href = href;
            Text = text;
            External = external;
            Target = target;
            Rel = rel;
        }

        public string Href { get; }
        public string Text { get; }
        public bool External { get; }
        public string? Target { get; }
        public string? Rel { get; }
    }
}
