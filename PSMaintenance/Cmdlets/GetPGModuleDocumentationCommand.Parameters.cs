// ReSharper disable All
using System;
using System.Management.Automation;

namespace PSMaintenance;

/// <summary>
/// Parameters for Get-ModuleDocumentation (console rendering of module documentation).
/// </summary>
public sealed partial class GetModuleDocumentationCommand
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

    /// <summary>High-level selection of which documents to show. Overrides granular switches when specified.</summary>
    [Parameter]
    public DocumentationSelection Type { get; set; } = DocumentationSelection.Default;

    /// <summary>Show README.*.</summary>
    [Parameter] public SwitchParameter Readme { get; set; }
    /// <summary>Show CHANGELOG.*.</summary>
    [Parameter] public SwitchParameter Changelog { get; set; }
    /// <summary>Show LICENSE.*.</summary>
    [Parameter] public SwitchParameter License { get; set; }
    /// <summary>Show configured IntroText/IntroFile (from Delivery metadata).</summary>
    [Parameter] public SwitchParameter Intro { get; set; }
    /// <summary>Show configured UpgradeText/UpgradeFile (from Delivery metadata or UPGRADE.*).</summary>
    [Parameter] public SwitchParameter Upgrade { get; set; }
    /// <summary>Convenience switch to show Intro, README, CHANGELOG and LICENSE in order.</summary>
    [Parameter] public SwitchParameter All { get; set; }
    /// <summary>List discovered documentation files (without rendering).</summary>
    [Parameter] public SwitchParameter List { get; set; }
    /// <summary>Prefer Internals folder over module root when both contain the same file kind.</summary>
    [Parameter] public SwitchParameter PreferInternals { get; set; }
    /// <summary>Print raw file content without Markdown rendering.</summary>
    [Parameter] public SwitchParameter Raw { get; set; }
    /// <summary>Show a specific file by name (relative to module root or Internals) or full path.</summary>
    [Parameter] public string? File { get; set; }

    /// <summary>Select JSON renderer for fenced JSON blocks: Auto, Spectre, or System.</summary>
    [Parameter]
    [ValidateSet("Auto","Spectre","System")]
    public string JsonRenderer { get; set; } = "Auto";
    /// <summary>Default language for unlabeled code fences (Auto, PowerShell, Json, None).</summary>
    [Parameter]
    [ValidateSet("Auto","PowerShell","Json","None")]
    public string DefaultCodeLanguage { get; set; } = "Auto";

    // Repository support
    /// <summary>Pull documentation directly from the module repository when local files are absent.</summary>
    [Parameter] public SwitchParameter FromRepository { get; set; }
    /// <summary>Prefer remote repository documents even if local files exist.</summary>
    [Parameter] public SwitchParameter PreferRepository { get; set; }
    /// <summary>Branch name to use when fetching remote docs. If omitted, the provider default branch is used.</summary>
    [Parameter] public string? RepositoryBranch { get; set; }
    /// <summary>Personal Access Token for private repositories. Env fallbacks: PG_GITHUB_TOKEN/GITHUB_TOKEN or PG_AZDO_PAT/AZURE_DEVOPS_EXT_PAT.</summary>
    [Parameter] public string? RepositoryToken { get; set; }
    /// <summary>Repository-relative folders to enumerate and display (e.g., 'docs', 'articles'). Only .md/.markdown/.txt files are rendered.</summary>
    [Parameter] public string[]? RepositoryPaths { get; set; }
}
