using System.Management.Automation;
using System.Runtime.InteropServices;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Configures running Pester tests as part of the build.
/// </summary>
[Cmdlet(VerbsCommon.New, "ConfigurationTest")]
public sealed class NewConfigurationTestCommand : PSCmdlet
{
    /// <summary>Path to the folder containing Pester tests.</summary>
    [Parameter(Mandatory = true)] public string TestsPath { get; set; } = string.Empty;

    /// <summary>Enable test execution in the build.</summary>
    [Parameter] public SwitchParameter Enable { get; set; }

    /// <summary>Force running tests even if caching would skip them.</summary>
    [Parameter] public SwitchParameter Force { get; set; }

    /// <summary>Emits test configuration (AfterMerge) when enabled.</summary>
    protected override void ProcessRecord()
    {
        if (!Enable.IsPresent) return;

        var normalized = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? TestsPath.Replace('/', '\\')
            : TestsPath.Replace('\\', '/');

        WriteObject(new ConfigurationTestSegment
        {
            Configuration = new TestConfiguration
            {
                When = TestExecutionWhen.AfterMerge,
                TestsPath = normalized,
                Force = Force.IsPresent
            }
        });
    }
}
