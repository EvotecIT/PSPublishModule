using System.Management.Automation;

namespace PSPublishModule;

/// <summary>
/// Reserved placeholder for future execution-time configuration.
/// </summary>
[Cmdlet(VerbsCommon.New, "ConfigurationExecute")]
public sealed class NewConfigurationExecuteCommand : PSCmdlet
{
    /// <summary>Does nothing and returns no output (backward compatibility).</summary>
    protected override void ProcessRecord()
    {
        // Intentionally empty.
    }
}

