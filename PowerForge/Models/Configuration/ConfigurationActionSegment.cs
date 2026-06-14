using System.Collections.Generic;

namespace PowerForge;

/// <summary>
/// Stable lifecycle points where a module pipeline action can run.
/// </summary>
public enum ModulePipelineActionStage
{
    /// <summary>Run before module dependency installation and tool preflight.</summary>
    BeforeDependencies,
    /// <summary>Run after module dependency installation and tool preflight.</summary>
    AfterDependencies,
    /// <summary>Run before version synchronization or platform version preparation.</summary>
    BeforeVersioning,
    /// <summary>Run after version synchronization or platform version preparation.</summary>
    AfterVersioning,
    /// <summary>Run before source files are copied into the staging directory.</summary>
    BeforeStaging,
    /// <summary>Run after source files are copied into the staging directory.</summary>
    AfterStaging,
    /// <summary>Run before the staged module build step.</summary>
    BeforeBuild,
    /// <summary>Run after the staged module build step.</summary>
    AfterBuild,
    /// <summary>Run before manifest refresh and delivery metadata updates.</summary>
    BeforeManifest,
    /// <summary>Run after manifest refresh and delivery metadata updates.</summary>
    AfterManifest,
    /// <summary>Run before documentation generation.</summary>
    BeforeDocumentation,
    /// <summary>Run after documentation generation.</summary>
    AfterDocumentation,
    /// <summary>Run before formatting and signing preparation.</summary>
    BeforeFormatting,
    /// <summary>Run after formatting completes.</summary>
    AfterFormatting,
    /// <summary>Run before validation checks.</summary>
    BeforeValidation,
    /// <summary>Run after validation checks.</summary>
    AfterValidation,
    /// <summary>Run before import and test steps.</summary>
    BeforeTests,
    /// <summary>Run after import and test steps.</summary>
    AfterTests,
    /// <summary>Run before module signing.</summary>
    BeforeSigning,
    /// <summary>Run after module signing.</summary>
    AfterSigning,
    /// <summary>Run before artifact creation.</summary>
    BeforeArtefacts,
    /// <summary>Run after artifact creation.</summary>
    AfterArtefacts,
    /// <summary>Run before publish steps.</summary>
    BeforePublish,
    /// <summary>Run after publish steps.</summary>
    AfterPublish,
    /// <summary>Run before module installation.</summary>
    BeforeInstall,
    /// <summary>Run after module installation.</summary>
    AfterInstall
}

/// <summary>
/// Configuration segment that runs a PowerShell action at a stable module pipeline lifecycle point.
/// </summary>
public sealed class ConfigurationActionSegment : IConfigurationSegment
{
    /// <inheritdoc />
    public string Type => "Execute";

    /// <summary>Action configuration payload.</summary>
    public ModulePipelineActionConfiguration Configuration { get; set; } = new();
}

/// <summary>
/// PowerShell action configuration for module pipeline lifecycle execution.
/// </summary>
public sealed class ModulePipelineActionConfiguration
{
    /// <summary>Enables or disables the action. Defaults to true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Friendly action name used in progress and result output.</summary>
    public string? Name { get; set; }

    /// <summary>Lifecycle point where the action runs.</summary>
    public ModulePipelineActionStage At { get; set; } = ModulePipelineActionStage.AfterBuild;

    /// <summary>Path to a PowerShell script file. Relative paths resolve from the project root.</summary>
    public string? FilePath { get; set; }

    /// <summary>Inline PowerShell script text to execute.</summary>
    public string? InlineScript { get; set; }

    /// <summary>Optional working directory. Relative paths resolve from the project root.</summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>Environment variable overrides passed to the action process.</summary>
    public Dictionary<string, string?>? Environment { get; set; }

    /// <summary>Timeout in seconds. Defaults to five minutes.</summary>
    public int? TimeoutSeconds { get; set; }

    /// <summary>When true, logs a failed action and lets the pipeline continue.</summary>
    public bool ContinueOnError { get; set; }

    /// <summary>When true, prefer Windows PowerShell before pwsh on Windows.</summary>
    public bool PreferWindowsPowerShell { get; set; }
}
