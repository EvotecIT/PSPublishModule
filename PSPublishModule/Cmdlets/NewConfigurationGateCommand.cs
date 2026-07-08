using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Sets the high-level module pipeline mode for an F5-friendly build DSL.
/// </summary>
/// <example>
/// <summary>Run only manifest refresh work.</summary>
/// <code>New-ConfigurationGate -Mode Manifest</code>
/// </example>
/// <example>
/// <summary>Regenerate command Markdown and external help without packaging or installing.</summary>
/// <code>New-ConfigurationGate -Mode Documentation</code>
/// </example>
/// <example>
/// <summary>Build locally without publishing configured destinations.</summary>
/// <code>New-ConfigurationGate -Mode Build</code>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationGate")]
[OutputType(typeof(ConfigurationGateSegment))]
public sealed class NewConfigurationGateCommand : PSCmdlet
{
    /// <summary>
    /// High-level run mode. Use Manifest for PSD1 refresh, Documentation for generated command docs and external help, Build for local build/package work, and Publish for release publishing.
    /// </summary>
    [Parameter(Mandatory = true, Position = 0)]
    [Alias("Type")]
    public ConfigurationGateMode Mode { get; set; }

    /// <summary>Emits the gate configuration segment.</summary>
    protected override void ProcessRecord()
    {
        WriteObject(new ConfigurationGateSegment
        {
            Configuration = new GateConfiguration
            {
                Mode = Mode
            }
        });
    }
}
