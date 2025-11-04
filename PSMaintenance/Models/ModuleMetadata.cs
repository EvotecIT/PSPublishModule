using System.Collections.Generic;

namespace PSMaintenance;

/// <summary>
/// Indicates whether a dependency is a Required (manifest) dependency or an External reference.
/// </summary>
internal enum ModuleDependencyKind { Required, External }

/// <summary>
/// Represents a module dependency with optional version/Guid and a list of child dependencies.
/// </summary>
internal sealed class ModuleDependency
{
    /// <summary>Indicates whether the dependency is Required (manifest) or External.</summary>
    public ModuleDependencyKind Kind { get; set; } = ModuleDependencyKind.Required;
    /// <summary>Dependency module name.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Requested or discovered version of the dependency (if available).</summary>
    public string? Version { get; set; }
    /// <summary>Dependency module Guid when provided by the manifest.</summary>
    public string? Guid { get; set; }
    /// <summary>Secondary dependencies of this dependency (one level of nesting for visualization).</summary>
    public List<ModuleDependency> Children { get; } = new List<ModuleDependency>();
}

/// <summary>
/// Aggregates module manifest information and exporter options used to build the HTML page.
/// </summary>
internal sealed class ModuleInfoModel
{
    /// <summary>Module name (from manifest Name/ModuleName).</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Module version as a string.</summary>
    public string Version { get; set; } = string.Empty;
    /// <summary>Human‑readable module description from the manifest.</summary>
    public string? Description { get; set; }
    /// <summary>Author field from the manifest.</summary>
    public string? Author { get; set; }
    /// <summary>Minimum PowerShell engine version required by the module.</summary>
    public string? PowerShellVersion { get; set; }
    /// <summary>Project or repository URL (PSData.ProjectUri).</summary>
    public string? ProjectUri { get; set; }
    /// <summary>Optional icon URL (PSData.IconUri).</summary>
    public string? IconUri { get; set; }
    /// <summary>Whether the module requires license acceptance (PSData.RequireLicenseAcceptance).</summary>
    public bool? RequireLicenseAcceptance { get; set; }
    /// <summary>Declared and external dependencies used for the Dependencies tab and diagram.</summary>
    public List<ModuleDependency> Dependencies { get; } = new List<ModuleDependency>();

    // Exporter options
    /// <summary>Skip rendering the Commands tab.</summary>
    public bool SkipCommands { get; set; }
    /// <summary>Skip building dependency tables/diagram.</summary>
    public bool SkipDependencies { get; set; }
    /// <summary>Maximum number of commands to render in the Commands tab.</summary>
    public int MaxCommands { get; set; } = 100;
    /// <summary>Per‑command Get‑Help timeout in seconds.</summary>
    public int HelpTimeoutSeconds { get; set; } = 3;
    /// <summary>Render Get‑Help output as fenced code to preserve monospace formatting.</summary>
    public bool HelpAsCode { get; set; }
    /// <summary>Source for examples: Auto (Raw then Maml), Raw (Out‑String EXAMPLES), or Maml (structured).</summary>
    public ExamplesMode ExamplesMode { get; set; } = ExamplesMode.Auto;
    /// <summary>Selection policy for standard tabs when both Local and Remote exist (PreferLocal/PreferRemote/All).</summary>
    public DocumentationMode Mode { get; set; } = DocumentationMode.All;
    /// <summary>Whether remote repository documents were requested (Online mode).</summary>
    public bool Online { get; set; }
    /// <summary>Show both variants even if identical (affects Local vs Remote collapse and similar cases).</summary>
    public bool ShowDuplicates { get; set; }
    /// <summary>Examples render layout: ProseFirst (default), MamlDefault, or AllAsCode.</summary>
    public ExamplesLayout ExamplesLayout { get; set; } = ExamplesLayout.ProseFirst;
}
