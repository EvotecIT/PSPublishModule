// ReSharper disable All
using System;
using PowerForge;
using System.Management.Automation;

namespace PSPublishModule;

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

    /// <summary>Base destination folder where documentation will be written. The final folder also depends on <see cref="Layout"/>.</summary>
    [Parameter(Mandatory = true)]
    public string Path { get; set; } = string.Empty;

    /// <summary>Output folder structure strategy. The default, <see cref="DocumentationLayout.ModuleAndVersion"/>, keeps each module version in its own subfolder.</summary>
    [Parameter]
    public DocumentationLayout Layout { get; set; } = DocumentationLayout.ModuleAndVersion;

    /// <summary>Behavior when the destination folder already exists. The default, <see cref="OnExistsOption.Merge"/>, adds missing files and preserves existing files unless <c>-Force</c> is used. <see cref="OnExistsOption.Refresh"/> overwrites package files without deleting unrelated local files.</summary>
    [Parameter]
    public OnExistsOption OnExists { get; set; } = OnExistsOption.Merge;

    /// <summary>Legacy toggle equivalent to selecting ModuleAndVersion when set; Direct when not set.</summary>
    [Parameter] public SwitchParameter CreateVersionSubfolder { get; set; }
    /// <summary>Allow replacement of existing files during merge, and clear read-only attributes when overwrite needs to delete an existing destination.</summary>
    [Parameter] public SwitchParameter Force { get; set; }
    /// <summary>Plan only; output the resolved destination path without copying files or changing the destination.</summary>
    [Parameter] public SwitchParameter ListOnly { get; set; }
    /// <summary>Open the resulting folder or README after installation.</summary>
    [Parameter] public SwitchParameter Open { get; set; }
    /// <summary>Suppress delivery IntroText or IntroFile output during installation.</summary>
    [Parameter] public SwitchParameter NoIntro { get; set; }
}
