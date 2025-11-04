// ReSharper disable All
using System;
using System.Management.Automation;

namespace PSMaintenance;

public sealed partial class InstallModuleDocumentationCommand
{
    /// <summary>Module name to install documentation for.</summary>
    [Parameter(ParameterSetName = "ByName", Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true)]
    [Alias("ModuleName")]
    public string? Name { get; set; }

    /// <summary>Module object to install documentation for. Alternative to <c>-Name</c>.</summary>
    [Parameter(ParameterSetName = "ByModule", ValueFromPipeline = true)]
    [Alias("InputObject", "ModuleInfo")]
    public PSModuleInfo? Module { get; set; }

    /// <summary>Exact version to select when multiple module versions are installed.</summary>
    [Parameter]
    public Version? RequiredVersion { get; set; }

    /// <summary>Destination folder where documentation will be written.</summary>
    [Parameter(Mandatory = true)]
    public string Path { get; set; } = string.Empty;

    /// <summary>Output folder structure strategy. Default is ModuleAndVersion.</summary>
    [Parameter]
    public DocumentationLayout Layout { get; set; } = DocumentationLayout.ModuleAndVersion;

    /// <summary>Behavior when the destination folder already exists. Default is Merge.</summary>
    [Parameter]
    public OnExistsOption OnExists { get; set; } = OnExistsOption.Merge;

    /// <summary>Legacy toggle equivalent to selecting ModuleAndVersion when set; Direct when not set.</summary>
    [Parameter] public SwitchParameter CreateVersionSubfolder { get; set; }
    /// <summary>Overwrite files during merge or overwrite operations.</summary>
    [Parameter] public SwitchParameter Force { get; set; }
    /// <summary>Plan only; output the resolved destination without copying files.</summary>
    [Parameter] public SwitchParameter ListOnly { get; set; }
    /// <summary>Open the resulting folder or README after installation.</summary>
    [Parameter] public SwitchParameter Open { get; set; }
    /// <summary>Suppress IntroText display during installation.</summary>
    [Parameter] public SwitchParameter NoIntro { get; set; }
}
