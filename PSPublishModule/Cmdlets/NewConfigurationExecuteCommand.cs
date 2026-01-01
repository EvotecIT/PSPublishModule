using System.Management.Automation;

namespace PSPublishModule;

/// <summary>
/// Reserved placeholder for future execution-time configuration.
/// </summary>
/// <remarks>
/// <para>
/// This cmdlet currently exists only for backward compatibility with older build scripts.
/// It emits no configuration segment and has no effect.
/// </para>
/// </remarks>
/// <example>
/// <summary>Call the placeholder cmdlet (no output)</summary>
/// <prefix>PS&gt; </prefix>
/// <code>New-ConfigurationExecute</code>
/// <para>No action is performed and nothing is emitted to the pipeline.</para>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationExecute")]
public sealed class NewConfigurationExecuteCommand : PSCmdlet
{
    /// <summary>Does nothing and returns no output (backward compatibility).</summary>
    protected override void ProcessRecord()
    {
        // Intentionally empty.
    }
}
