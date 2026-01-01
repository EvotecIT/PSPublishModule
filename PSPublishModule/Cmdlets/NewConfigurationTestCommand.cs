using System.Management.Automation;
using System.Runtime.InteropServices;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Configures running Pester tests as part of the build.
/// </summary>
/// <remarks>
/// <para>
/// Emits a test configuration segment that instructs the pipeline to run Pester tests after the module is merged/built.
/// Use this when you want builds to fail fast on test failures.
/// </para>
/// </remarks>
/// <example>
/// <summary>Enable Pester tests during the build</summary>
/// <prefix>PS&gt; </prefix>
/// <code>New-ConfigurationTest -Enable -TestsPath 'Tests'</code>
/// <para>Runs tests from the Tests folder after the build/merge step.</para>
/// </example>
/// <example>
/// <summary>Force test execution (ignore caching)</summary>
/// <prefix>PS&gt; </prefix>
/// <code>New-ConfigurationTest -Enable -TestsPath 'Tests' -Force</code>
/// <para>Useful in CI when you always want a fresh test run.</para>
/// </example>
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
