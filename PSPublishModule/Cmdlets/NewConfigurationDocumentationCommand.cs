using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Enables or disables creation of documentation from the module using PowerForge.
/// </summary>
[Cmdlet(VerbsCommon.New, "ConfigurationDocumentation")]
public sealed class NewConfigurationDocumentationCommand : PSCmdlet
{
    /// <summary>Enables creation of documentation from the module.</summary>
    [Parameter] public SwitchParameter Enable { get; set; }

    /// <summary>Removes all files from the documentation folder before creating new documentation.</summary>
    [Parameter] public SwitchParameter StartClean { get; set; }

    /// <summary>
    /// When enabled, generated documentation is also synced back to the project folder
    /// (not only to the staging build output).
    /// </summary>
    [Parameter] public SwitchParameter UpdateWhenNew { get; set; }

    /// <summary>Disable external help (MAML) generation.</summary>
    [Parameter] public SwitchParameter SkipExternalHelp { get; set; }

    /// <summary>Culture folder for generated external help (default: en-US).</summary>
    [Parameter] public string ExternalHelpCulture { get; set; } = "en-US";

    /// <summary>Optional file name override for external help (default: &lt;ModuleName&gt;-help.xml).</summary>
    [Parameter] public string ExternalHelpFileName { get; set; } = string.Empty;

    /// <summary>Path to the folder where documentation will be created.</summary>
    [Parameter(Mandatory = true)] public string Path { get; set; } = string.Empty;

    /// <summary>Path to the readme file that will be used for the documentation.</summary>
    [Parameter(Mandatory = true)] public string PathReadme { get; set; } = string.Empty;

    /// <summary>Documentation engine (legacy parameter; kept for compatibility).</summary>
    [Parameter(DontShow = true)] public PowerForge.DocumentationTool Tool { get; set; } = PowerForge.DocumentationTool.PowerForge;

    /// <summary>Emits documentation configuration for the build pipeline.</summary>
    protected override void ProcessRecord()
    {
        WriteObject(new ConfigurationDocumentationSegment
        {
            Configuration = new DocumentationConfiguration
            {
                Path = Path,
                PathReadme = PathReadme
            }
        });

        var emitBuildSegment =
            Enable.IsPresent ||
            StartClean.IsPresent ||
            UpdateWhenNew.IsPresent ||
            SkipExternalHelp.IsPresent ||
            MyInvocation.BoundParameters.ContainsKey(nameof(ExternalHelpCulture)) ||
            MyInvocation.BoundParameters.ContainsKey(nameof(ExternalHelpFileName));

        if (emitBuildSegment)
        {
            WriteObject(new ConfigurationBuildDocumentationSegment
            {
                Configuration = new BuildDocumentationConfiguration
                {
                    Enable = Enable.IsPresent,
                    StartClean = StartClean.IsPresent,
                    UpdateWhenNew = UpdateWhenNew.IsPresent,
                    Tool = PowerForge.DocumentationTool.PowerForge,
                    GenerateExternalHelp = !SkipExternalHelp.IsPresent,
                    ExternalHelpCulture = ExternalHelpCulture,
                    ExternalHelpFileName = ExternalHelpFileName
                }
            });
        }
    }
}
