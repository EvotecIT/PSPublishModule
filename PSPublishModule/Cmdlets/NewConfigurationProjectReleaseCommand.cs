using System;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates release-level defaults for a PowerShell-authored project build.
/// </summary>
[Cmdlet(VerbsCommon.New, "ConfigurationProjectRelease")]
[OutputType(typeof(ConfigurationProjectRelease))]
public sealed class NewConfigurationProjectReleaseCommand : PSCmdlet
{
    /// <summary>
    /// Build configuration used by the generated release object.
    /// </summary>
    [Parameter]
    public string Configuration { get; set; } = "Release";

    /// <summary>
    /// Enables tool/app GitHub release publishing by default for this release object.
    /// </summary>
    [Parameter]
    public SwitchParameter PublishToolGitHub { get; set; }

    /// <summary>
    /// Skips restore operations by default for DotNetPublish-backed tool/app flows.
    /// </summary>
    [Parameter]
    public SwitchParameter SkipRestore { get; set; }

    /// <summary>
    /// Skips build operations by default for DotNetPublish-backed tool/app flows.
    /// </summary>
    [Parameter]
    public SwitchParameter SkipBuild { get; set; }

    /// <summary>
    /// Optional default release output selection.
    /// </summary>
    [Parameter]
    public ConfigurationProjectReleaseOutputType[]? ToolOutput { get; set; }

    /// <summary>
    /// Optional default release output exclusion.
    /// </summary>
    [Parameter]
    public ConfigurationProjectReleaseOutputType[]? SkipToolOutput { get; set; }

    /// <summary>
    /// Emits a <see cref="ConfigurationProjectRelease"/> object.
    /// </summary>
    protected override void ProcessRecord()
    {
        WriteObject(new ConfigurationProjectRelease
        {
            Configuration = string.IsNullOrWhiteSpace(Configuration) ? "Release" : Configuration.Trim(),
            PublishToolGitHub = PublishToolGitHub.IsPresent,
            SkipRestore = SkipRestore.IsPresent,
            SkipBuild = SkipBuild.IsPresent,
            ToolOutput = ToolOutput?.Distinct().ToArray() ?? Array.Empty<ConfigurationProjectReleaseOutputType>(),
            SkipToolOutput = SkipToolOutput?.Distinct().ToArray() ?? Array.Empty<ConfigurationProjectReleaseOutputType>()
        });
    }
}
