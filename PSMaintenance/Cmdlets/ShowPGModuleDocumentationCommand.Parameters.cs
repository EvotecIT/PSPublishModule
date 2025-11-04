// ReSharper disable All
using System;
using System.Management.Automation;

namespace PSMaintenance;

public sealed partial class ShowModuleDocumentationCommand
{
    /// <summary>Module name to display documentation for.</summary>
    [Parameter(ParameterSetName = "ByName", Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    [Alias("ModuleName")]
    public string? Name { get; set; }

    /// <summary>Module object to display documentation for. Alternative to <c>-Name</c>.</summary>
    [Parameter(ParameterSetName = "ByModule", ValueFromPipeline = true)]
    [Alias("InputObject", "ModuleInfo")]
    public PSModuleInfo? Module { get; set; }

    /// <summary>Exact version to select when multiple module versions are installed.</summary>
    public Version? RequiredVersion { get; set; }

    /// <summary>Direct path to a documentation folder containing README/CHANGELOG/etc.</summary>
    [Parameter(ParameterSetName = "ByPath")]
    public string? DocsPath { get; set; }

    /// <summary>Path to a module root (folder that contains the module manifest). Useful for unpacked builds.</summary>
    [Parameter(ParameterSetName = "ByBase")]
    public string? ModuleBase { get; set; }
    /// <summary>Show a specific file by name (relative to module root or Internals) or full path.</summary>
    [Parameter] public string? File { get; set; }
    /// <summary>Prefer Internals folder over module root when both contain the same file kind.</summary>
    [Parameter] public SwitchParameter PreferInternals { get; set; }
    /// <summary>Open the chosen document in the default shell handler instead of rendering to console.</summary>
    [Parameter] public SwitchParameter Open { get; set; }
    /// <summary>Select JSON renderer for fenced JSON blocks: Auto, Spectre, or System.</summary>
    [Parameter]
    [ValidateSet("Auto","Spectre","System")]
    public string JsonRenderer { get; set; } = "Auto";
    /// <summary>Default language for unlabeled code fences (Auto, PowerShell, Json, None).</summary>
    [Parameter]
    [ValidateSet("Auto","PowerShell","Json","None")]
    public string DefaultCodeLanguage { get; set; } = "Auto";
    /// <summary>Heading rulers style. <c>H1AndH2</c> draws rules for H1/H2, <c>H1</c> for H1 only, <c>None</c> disables.</summary>
    [Parameter]
    [ValidateSet("None","H1","H1AndH2")]
    public string HeadingRules { get; set; } = "H1AndH2";
    /// <summary>Output location for the generated HTML. Accepts a file path or an existing directory. Defaults to temp when omitted.</summary>
    [Parameter]
    [Alias("Path","ExportHtmlPath")]
    public string? OutputPath { get; set; }
    /// <summary>Do not open the generated HTML (default is to open after export).</summary>
    [Parameter]
    public SwitchParameter DoNotShow { get; set; }
    /// <summary>Disable code tokenizers and render code fences as plain text.</summary>
    [Parameter]
    public SwitchParameter DisableTokenizer { get; set; }

    // High-level selection (-Type) removed for Show-ModuleDocumentation; full HTML is rendered by default.

    // Performance/scope controls
    /// <summary>Skip building the dependency list and graph.</summary>
    [Parameter] public SwitchParameter SkipDependencies { get; set; }
    /// <summary>Skip building the Commands tab (fast export).</summary>
    [Parameter] public SwitchParameter SkipCommands { get; set; }
    /// <summary>Convenience switch equal to -SkipRemote -SkipDependencies -SkipCommands.</summary>
    [Parameter] public SwitchParameter Fast { get; set; }
    /// <summary>Limit the number of commands rendered in the Commands tab. Default 100.</summary>
    [Parameter] public int MaxCommands { get; set; } = 100;
    /// <summary>Per-command Get-Help timeout in seconds. Default 3.</summary>
    [Parameter] public int HelpTimeoutSeconds { get; set; } = 60;
    /// <summary>Render command help inside fenced code blocks for uniform monospace formatting.</summary>
    [Parameter] public SwitchParameter HelpAsCode { get; set; }

    /// <summary>
    /// Controls how Examples are sourced. Raw = parse the EXAMPLES section from Get-Help text; Maml = use structured help object; Auto = Raw then Maml.
    /// </summary>
    [Parameter]
    [ValidateSet("Auto","Raw","Maml")]
    public string ExamplesMode { get; set; } = "Auto";

    /// <summary>
    /// Controls how examples are rendered: ProseFirst (remarks then code), MamlDefault (code then remarks), or AllAsCode.
    /// Default is ProseFirst.
    /// </summary>
    [Parameter]
    [ValidateSet("MamlDefault","ProseFirst","AllAsCode")]
    public string ExamplesLayout { get; set; } = "ProseFirst";

    // Remote repository support (enable with -Online). Legacy switches removed; scripts using them will be mapped internally if needed.
    /// <summary>
    /// Branch name to use when fetching remote docs. If omitted, the provider default branch is used.
    /// </summary>
    [Parameter]
    public string? RepositoryBranch { get; set; }
    /// <summary>
    /// Personal Access Token for private repositories. Alternatively set environment variables:
    /// GitHub: PG_GITHUB_TOKEN or GITHUB_TOKEN; Azure DevOps: PG_AZDO_PAT or AZURE_DEVOPS_EXT_PAT.
    /// </summary>
    [Parameter]
    public string? RepositoryToken { get; set; }
    /// <summary>
    /// Repository-relative folders to enumerate and display (e.g., 'docs', 'articles').
    /// Only .md/.markdown/.txt files are rendered.
    /// </summary>
    [Parameter]
    public string[]? RepositoryPaths { get; set; }

    /// <summary>Enable repository access (fetch remote docs using manifest defaults or -Repository* overrides).</summary>
    [Parameter] public SwitchParameter Online { get; set; }
    /// <summary>
    /// Selection policy for standard tabs when Local and Remote exist (applies only when -Online):
    /// PreferLocal (default), PreferRemote, or All.
    /// </summary>
    [Parameter]
    [ValidateSet("PreferLocal","PreferRemote","All")]
    public string Mode { get; set; } = "All";
    /// <summary>Show both variants even when content is identical (root vs internals and local vs remote).</summary>
    [Parameter] public SwitchParameter ShowDuplicates { get; set; }
}
