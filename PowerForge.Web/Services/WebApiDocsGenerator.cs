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
    /// <summary>
    /// Legacy flat alias behavior for docs template type pages (`/api/&lt;slug&gt;.html`).
    /// Supported values: <c>noindex</c> (default), <c>redirect</c>, <c>omit</c>.
    /// </summary>
    public string? LegacyAliasMode { get; set; }
    /// <summary>Whether Prism assets should be injected for API code highlighting.</summary>
    public bool InjectPrismAssets { get; set; } = true;
    /// <summary>Optional Prism configuration inherited from site settings.</summary>
    public PrismSpec? Prism { get; set; }
    /// <summary>Optional global asset policy mode (used when Prism source is not explicitly set).</summary>
    public string? AssetPolicyMode { get; set; }
    /// <summary>Output format hint (json, html, hybrid, both).</summary>
    public string? Format { get; set; }
    /// <summary>Optional stylesheet href for HTML output.</summary>
    public string? CssHref { get; set; }
    /// <summary>
    /// Optional critical CSS HTML injected into the &lt;head&gt; of generated API pages.
    /// Intended to match site-wide critical styles so API reference pages don't look "unstyled" during initial paint.
    /// </summary>
    public string? CriticalCssHtml { get; set; }
    /// <summary>
    /// Optional path to a critical CSS file to inline into the &lt;head&gt; of generated API pages.
    /// If <see cref="CriticalCssHtml"/> is set, it takes precedence.
    /// </summary>
    public string? CriticalCssPath { get; set; }
    /// <summary>Optional path to header HTML fragment.</summary>
    public string? HeaderHtmlPath { get; set; }
    /// <summary>Optional path to footer HTML fragment.</summary>
    public string? FooterHtmlPath { get; set; }
    /// <summary>Optional nav config path (site.json or site-nav.json) for header/footer tokens.</summary>
    public string? NavJsonPath { get; set; }
    /// <summary>
    /// Optional navigation context path used when <see cref="NavJsonPath"/> points to a site-nav.json that contains profile definitions.
    /// When set, PowerForge can select the most appropriate Navigation.Profile for API docs header/footer tokens.
    /// </summary>
    public string? NavContextPath { get; set; }
    /// <summary>Optional navigation context collection used for profile matching.</summary>
    public string? NavContextCollection { get; set; }
    /// <summary>Optional navigation context layout used for profile matching.</summary>
    public string? NavContextLayout { get; set; }
    /// <summary>Optional navigation context project slug used for profile matching.</summary>
    public string? NavContextProject { get; set; }
    /// <summary>
    /// Optional navigation surface name used when NavJsonPath points to site-nav.json with "surfaces".
    /// When set, API docs nav injection prefers that surface over context-based inference.
    /// </summary>
    public string? NavSurfaceName { get; set; }
    /// <summary>Optional site display name override.</summary>
    public string? SiteName { get; set; }
    /// <summary>Optional social preview image URL override for generated API HTML pages.</summary>
    public string? SocialImage { get; set; }
    /// <summary>Optional social preview image width for generated API HTML pages.</summary>
    public int? SocialImageWidth { get; set; }
    /// <summary>Optional social preview image height for generated API HTML pages.</summary>
    public int? SocialImageHeight { get; set; }
    /// <summary>Optional Twitter card override for generated API HTML pages.</summary>
    public string? SocialTwitterCard { get; set; }
    /// <summary>Optional Twitter site handle override (for example: @myproject).</summary>
    public string? SocialTwitterSite { get; set; }
    /// <summary>Optional Twitter creator handle override (for example: @author).</summary>
    public string? SocialTwitterCreator { get; set; }
    /// <summary>When true, generate per-page API social card PNG files from page content.</summary>
    public bool AutoGenerateSocialCards { get; set; }
    /// <summary>Output URL prefix for generated API social card files.</summary>
    public string? SocialCardPath { get; set; } = "/assets/social/generated/api";
    /// <summary>Width of generated API social card PNG files.</summary>
    public int SocialCardWidth { get; set; } = 1200;
    /// <summary>Height of generated API social card PNG files.</summary>
    public int SocialCardHeight { get; set; } = 630;
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
    /// <summary>
    /// Optional path prefix prepended to resolved source paths before URL token expansion.
    /// Useful when generated source paths need a stable repo-relative prefix in mixed-repo layouts.
    /// </summary>
    public string? SourcePathPrefix { get; set; }
    /// <summary>Optional source URL pattern (use {path} and {line}).</summary>
    public string? SourceUrlPattern { get; set; }
    /// <summary>
    /// Optional source URL mapping rules used for mixed-source API docs.
    /// The first rule with the longest matching <see cref="WebApiDocsSourceUrlMapping.PathPrefix"/> wins.
    /// </summary>
    public List<WebApiDocsSourceUrlMapping> SourceUrlMappings { get; } = new();
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
    /// <summary>
    /// Optional preferred type names shown in the API "Quick Start" and sidebar "Main API" sections.
    /// Values are matched case-insensitively against type simple names.
    /// </summary>
    public List<string> QuickStartTypeNames { get; } = new();
    /// <summary>
    /// Controls type display labels in docs output and JSON metadata.
    /// Supported values: <c>short</c>, <c>namespace-suffix</c>, <c>full</c>.
    /// </summary>
    public string? DisplayNameMode { get; set; }
    /// <summary>
    /// Generates a machine-readable coverage report with API documentation completeness stats.
    /// </summary>
    public bool GenerateCoverageReport { get; set; } = true;
    /// <summary>
    /// Optional coverage report output path. Relative paths are resolved under <see cref="OutputPath"/>.
    /// Defaults to <c>coverage.json</c> when not set.
    /// </summary>
    public string? CoverageReportPath { get; set; }
    /// <summary>
    /// Generates a DocFX-compatible xref map for API symbols.
    /// </summary>
    public bool GenerateXrefMap { get; set; } = true;
    /// <summary>
    /// Includes member-level entries (methods/properties/fields/events/parameters) in generated xref maps.
    /// </summary>
    public bool GenerateMemberXrefs { get; set; } = true;
    /// <summary>
    /// Optional member xref kinds filter. Empty means all supported kinds.
    /// Supported values: constructors, methods, properties, fields, events, extensions, parameters.
    /// </summary>
    public List<string> MemberXrefKinds { get; } = new();
    /// <summary>
    /// Optional cap for emitted member xref entries per type/command. 0 means unlimited.
    /// </summary>
    public int MemberXrefMaxPerType { get; set; }
    /// <summary>
    /// Optional xref map output path. Relative paths are resolved under <see cref="OutputPath"/>.
    /// Defaults to <c>xrefmap.json</c> when not set.
    /// </summary>
    public string? XrefMapPath { get; set; }
    /// <summary>
    /// Enables fallback code examples for PowerShell commands when help XML does not provide examples.
    /// </summary>
    public bool GeneratePowerShellFallbackExamples { get; set; } = true;
    /// <summary>
    /// Optional path to PowerShell example scripts (file or directory). When omitted, generator probes common module paths.
    /// </summary>
    public string? PowerShellExamplesPath { get; set; }
    /// <summary>
    /// Maximum number of fallback code examples imported per PowerShell command.
    /// </summary>
    public int PowerShellFallbackExampleLimitPerCommand { get; set; } = 2;
}

/// <summary>Path-based source URL mapping for API source/edit links.</summary>
public sealed class WebApiDocsSourceUrlMapping
{
    /// <summary>
    /// Relative path prefix used to match discovered source paths (for example: <c>HtmlForgeX.Email/</c>).
    /// Matching is case-insensitive and slash-normalized.
    /// </summary>
    public string PathPrefix { get; set; } = string.Empty;

    /// <summary>
    /// URL pattern used when the prefix matches. Supports tokens:
    /// <c>{path}</c>, <c>{line}</c>, <c>{root}</c>, <c>{pathNoRoot}</c>, and <c>{pathNoPrefix}</c>.
    /// </summary>
    public string UrlPattern { get; set; } = string.Empty;

    /// <summary>
    /// When true, trims <see cref="PathPrefix"/> from <c>{path}</c> for this rule.
    /// </summary>
    public bool StripPathPrefix { get; set; }
}

/// <summary>Generates API documentation artifacts from XML docs.</summary>
public static partial class WebApiDocsGenerator
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);
    private static readonly Regex GenericArityRegex = new("`{1,2}\\d+", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex WhitespaceRegex = new("\\s+", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex SlugNonAlnumRegex = new("[^a-z0-9]+", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex SlugDashRegex = new("-{2,}", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex CrefTokenRegex = new("\\[\\[cref:(?<name>[^\\]]+)\\]\\]", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex HrefTokenRegex = new("\\[\\[href:(?<url>[^|\\]]+)\\|(?<label>[^\\]]*)\\]\\]", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex AboutTokenRegex = new("\\babout_[A-Za-z0-9_.-]+\\b", RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, RegexTimeout);
    private static readonly Regex ParagraphSplitRegex = new("\\n\\s*\\n", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);
    private static readonly Regex ParagraphLineBreakNormalizeRegex = new("\\s*\\n\\s*", RegexOptions.Compiled | RegexOptions.CultureInvariant, RegexTimeout);

    // Minimal selector contract: enough to catch "API generator added new structure but theme CSS didn't follow".
    private static readonly string[] RequiredSelectorsSimple = { ".pf-api", ".pf-api-search", ".pf-api-types", ".pf-api-type", ".pf-api-section" };
    private static readonly string[] RequiredSelectorsDocs =
    {
        ".api-layout",
        ".api-sidebar",
        ".api-content",
        ".api-overview",
        ".type-chips",
        ".type-chip",
        ".chip-icon",
        ".sidebar-count",
        ".sidebar-toggle",
        ".type-item",
        ".filter-button",
        ".member-card",
        ".member-signature",
        ".member-header pre.member-signature",
        ".member-card pre::-webkit-scrollbar",
        ".member-card pre::-webkit-scrollbar-track",
        ".member-card pre::-webkit-scrollbar-thumb"
    };
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
            ? ParsePowerShellHelp(helpPath, warnings, options)
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
        var typeDisplayNames = BuildTypeDisplayNameMap(types, options, warnings);
        var typeAliasMap = BuildTypeAliasMap(types, typeDisplayNames);

        ValidateSourceUrlPatternConsistency(options, types, warnings);
        ValidateConfiguredQuickStartTypes(types, options, warnings);
        ValidateDuplicateMemberSignatures(types, warnings);

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
            ["types"] = types.Select(t =>
            {
                var displayName = ResolveTypeDisplayName(t, typeDisplayNames);
                var aliases = GetTypeAliases(t, displayName, typeAliasMap);
                return new Dictionary<string, object?>
                {
                    ["name"] = t.Name,
                    ["displayName"] = displayName,
                    ["aliases"] = aliases,
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
                };
            }).ToList()
        };

        var indexPath = Path.Combine(outputPath, "index.json");
        WriteJson(indexPath, index);

        var search = types.Select(t =>
        {
            var displayName = ResolveTypeDisplayName(t, typeDisplayNames);
            var aliases = GetTypeAliases(t, displayName, typeAliasMap);
            return new Dictionary<string, object?>
            {
                ["title"] = t.FullName,
                ["displayName"] = displayName,
                ["aliases"] = aliases,
                ["summary"] = t.Summary ?? string.Empty,
                ["kind"] = t.Kind,
                ["namespace"] = t.Namespace,
                ["slug"] = t.Slug,
                ["url"] = $"{options.BaseUrl.TrimEnd('/')}/types/{t.Slug}.json"
            };
        }).ToList();

        var searchPath = Path.Combine(outputPath, "search.json");
        WriteJson(searchPath, search);

        var typesDir = Path.Combine(outputPath, "types");
        Directory.CreateDirectory(typesDir);
        foreach (var type in types)
        {
            var displayName = ResolveTypeDisplayName(type, typeDisplayNames);
            var aliases = GetTypeAliases(type, displayName, typeAliasMap);
            var typeModel = new Dictionary<string, object?>
            {
                ["name"] = type.Name,
                ["displayName"] = displayName,
                ["aliases"] = aliases,
                ["commandAliases"] = type.Aliases,
                ["fullName"] = type.FullName,
                ["namespace"] = type.Namespace,
                ["inputTypes"] = type.InputTypes,
                ["outputTypes"] = type.OutputTypes,
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
                    ["parameterSetName"] = m.ParameterSetName,
                    ["includesCommonParameters"] = m.IncludesCommonParameters,
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
                        ["aliases"] = p.Aliases,
                        ["possibleValues"] = p.PossibleValues,
                        ["isOptional"] = p.IsOptional,
                        ["defaultValue"] = p.DefaultValue,
                        ["position"] = p.Position,
                        ["pipelineInput"] = p.PipelineInput
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
                        ["aliases"] = p.Aliases,
                        ["possibleValues"] = p.PossibleValues,
                        ["isOptional"] = p.IsOptional,
                        ["defaultValue"] = p.DefaultValue,
                        ["position"] = p.Position,
                        ["pipelineInput"] = p.PipelineInput
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
                        ["aliases"] = p.Aliases,
                        ["possibleValues"] = p.PossibleValues,
                        ["isOptional"] = p.IsOptional,
                        ["defaultValue"] = p.DefaultValue,
                        ["position"] = p.Position,
                        ["pipelineInput"] = p.PipelineInput
                    }).ToList()
                }).ToList()
            };

            var typePath = Path.Combine(typesDir, $"{type.Slug}.json");
            WriteJson(typePath, typeModel);
        }

        var format = (options.Format ?? "json").Trim().ToLowerInvariant();
        if (format is "hybrid" or "html" or "both")
        {
            GenerateHtml(outputPath, options, types, warnings);
            ValidateCssContract(outputPath, options, warnings);
        }

        var coveragePath = WriteCoverageReport(outputPath, options, types, assemblyName, assemblyVersion, warnings);
        var xrefPath = WriteXrefMap(outputPath, options, types, assemblyName, assemblyVersion, warnings);

        var normalizedWarnings = warnings
            .Where(static w => !string.IsNullOrWhiteSpace(w))
            .Select(NormalizeWarningCode)
            .ToArray();

        return new WebApiDocsResult
        {
            OutputPath = outputPath,
            IndexPath = indexPath,
            SearchPath = searchPath,
            TypesPath = typesDir,
            CoveragePath = coveragePath,
            XrefPath = xrefPath,
            TypeCount = types.Count,
            UsedReflectionFallback = usedReflectionFallback,
            Warnings = normalizedWarnings
        };
    }

    private static string NormalizeWarningCode(string warning)
    {
        if (string.IsNullOrWhiteSpace(warning))
            return string.Empty;

        var trimmed = warning.TrimStart();
        if (trimmed.StartsWith("[", StringComparison.Ordinal))
            return warning;

        if (trimmed.StartsWith("API docs nav required:", StringComparison.OrdinalIgnoreCase))
            return "[PFWEB.APIDOCS.NAV.REQUIRED] " + warning;

        if (trimmed.StartsWith("API docs CSS contract:", StringComparison.OrdinalIgnoreCase))
            return "[PFWEB.APIDOCS.CSS.CONTRACT] " + warning;

        if (trimmed.StartsWith("API docs: quickStartTypes", StringComparison.OrdinalIgnoreCase))
            return "[PFWEB.APIDOCS.QUICKSTART] " + warning;
        if (trimmed.StartsWith("API docs display names:", StringComparison.OrdinalIgnoreCase))
            return "[PFWEB.APIDOCS.DISPLAY] " + warning;
        if (trimmed.StartsWith("API docs member signatures:", StringComparison.OrdinalIgnoreCase))
            return "[PFWEB.APIDOCS.MEMBER.SIGNATURES] " + warning;
        if (trimmed.StartsWith("API docs coverage:", StringComparison.OrdinalIgnoreCase))
            return "[PFWEB.APIDOCS.COVERAGE] " + warning;
        if (trimmed.StartsWith("API docs xref:", StringComparison.OrdinalIgnoreCase))
            return "[PFWEB.APIDOCS.XREF] " + warning;

        if (trimmed.StartsWith("API docs: using embedded header/footer", StringComparison.OrdinalIgnoreCase))
            return "[PFWEB.APIDOCS.NAV.FALLBACK] " + warning;

        if (trimmed.StartsWith("API docs nav:", StringComparison.OrdinalIgnoreCase))
            return "[PFWEB.APIDOCS.NAV] " + warning;

        if (trimmed.StartsWith("API docs:", StringComparison.OrdinalIgnoreCase))
            return "[PFWEB.APIDOCS] " + warning;

        if (trimmed.StartsWith("XML docs not found:", StringComparison.OrdinalIgnoreCase))
            return "[PFWEB.APIDOCS.INPUT.XML] " + warning;

        if (trimmed.StartsWith("PowerShell help not found:", StringComparison.OrdinalIgnoreCase))
            return "[PFWEB.APIDOCS.INPUT.HELP] " + warning;

        if (trimmed.StartsWith("Assembly not found:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Assembly load failed:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Assembly inspection failed:", StringComparison.OrdinalIgnoreCase))
            return "[PFWEB.APIDOCS.INPUT.ASSEMBLY] " + warning;

        if (trimmed.StartsWith("Reflection fallback", StringComparison.OrdinalIgnoreCase))
            return "[PFWEB.APIDOCS.REFLECTION] " + warning;

        if (trimmed.StartsWith("Source links disabled:", StringComparison.OrdinalIgnoreCase))
            return "[PFWEB.APIDOCS.SOURCE] " + warning;
        if (trimmed.StartsWith("SourceUrlPattern repo", StringComparison.OrdinalIgnoreCase))
            return "[PFWEB.APIDOCS.SOURCE] " + warning;
        if (trimmed.StartsWith("API docs source:", StringComparison.OrdinalIgnoreCase))
            return "[PFWEB.APIDOCS.SOURCE] " + warning;

        if (trimmed.StartsWith("Failed to parse PowerShell help:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Multiple PowerShell help files found", StringComparison.OrdinalIgnoreCase))
            return "[PFWEB.APIDOCS.POWERSHELL] " + warning;

        return warning;
    }
}
