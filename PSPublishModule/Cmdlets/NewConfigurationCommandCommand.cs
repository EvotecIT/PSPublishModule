using System.Collections.Specialized;
using System.Management.Automation;

namespace PSPublishModule;

/// <summary>
/// Defines a command import configuration for the build pipeline.
/// </summary>
[Cmdlet(VerbsCommon.New, "ConfigurationCommand")]
public sealed class NewConfigurationCommandCommand : PSCmdlet
{
    /// <summary>Name of the module that contains the commands.</summary>
    [Parameter] public string? ModuleName { get; set; }

    /// <summary>One or more command names to reference from the module.</summary>
    [Parameter] public string[]? CommandName { get; set; }

    /// <summary>Emits a configuration object for command references.</summary>
    protected override void ProcessRecord()
    {
        var cfg = new OrderedDictionary
        {
            ["Type"] = "Command",
            ["Configuration"] = new OrderedDictionary
            {
                ["ModuleName"] = ModuleName,
                ["CommandName"] = CommandName
            }
        };

        WriteObject(cfg);
    }
}

