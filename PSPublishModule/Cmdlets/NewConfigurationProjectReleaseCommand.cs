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
    /// Emits a <see cref="ConfigurationProjectRelease"/> object.
    /// </summary>
    protected override void ProcessRecord()
    {
        WriteObject(new ConfigurationProjectRelease
        {
            Configuration = string.IsNullOrWhiteSpace(Configuration) ? "Release" : Configuration.Trim()
        });
    }
}
