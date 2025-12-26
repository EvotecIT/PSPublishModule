using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Enables or disables creation of documentation from the module using PlatyPS or HelpOut.
/// </summary>
[Cmdlet(VerbsCommon.New, "ConfigurationDocumentation")]
public sealed class NewConfigurationDocumentationCommand : PSCmdlet
{
    /// <summary>Enables creation of documentation from the module.</summary>
    [Parameter] public SwitchParameter Enable { get; set; }

    /// <summary>Removes all files from the documentation folder before creating new documentation.</summary>
    [Parameter] public SwitchParameter StartClean { get; set; }

    /// <summary>Updates the documentation right after running New-MarkdownHelp due to PlatyPS bugs.</summary>
    [Parameter] public SwitchParameter UpdateWhenNew { get; set; }

    /// <summary>Path to the folder where documentation will be created.</summary>
    [Parameter(Mandatory = true)] public string Path { get; set; } = string.Empty;

    /// <summary>Path to the readme file that will be used for the documentation.</summary>
    [Parameter(Mandatory = true)] public string PathReadme { get; set; } = string.Empty;

    /// <summary>Tool to use for documentation generation.</summary>
    [Parameter] public PowerForge.DocumentationTool Tool { get; set; } = PowerForge.DocumentationTool.PlatyPS;

    /// <summary>Emits documentation configuration for the build pipeline.</summary>
    protected override void ProcessRecord()
    {
        var toolModule = Tool == PowerForge.DocumentationTool.PlatyPS ? "PlatyPS" : "HelpOut";
        var modules = InvokeCommand.InvokeScript($"Get-Module -Name '{toolModule}' -ListAvailable");
        if (modules is null || modules.Count == 0)
        {
            WriteWarning($"Module {toolModule} is not installed. Please install it ussing Install-Module {toolModule} -Force -Verbose");
            return;
        }

        WriteObject(new ConfigurationDocumentationSegment
        {
            Configuration = new DocumentationConfiguration
            {
                Path = Path,
                PathReadme = PathReadme
            }
        });

        if (Enable.IsPresent || StartClean.IsPresent || UpdateWhenNew.IsPresent)
        {
            WriteObject(new ConfigurationBuildDocumentationSegment
            {
                Configuration = new BuildDocumentationConfiguration
                {
                    Enable = Enable.IsPresent,
                    StartClean = StartClean.IsPresent,
                    UpdateWhenNew = UpdateWhenNew.IsPresent,
                    Tool = Tool
                }
            });
        }
    }
}
