using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Defines a command import configuration for the build pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Used by the build pipeline to declare which commands should be imported from an external module at build time.
/// This helps make build scripts deterministic and explicit about their dependencies.
/// </para>
/// </remarks>
/// <example>
/// <summary>Reference a single command from a module</summary>
/// <prefix>PS&gt; </prefix>
/// <code>New-ConfigurationCommand -ModuleName 'Pester' -CommandName 'Invoke-Pester'</code>
/// <para>Declares a dependency on <c>Invoke-Pester</c> from the Pester module.</para>
/// </example>
/// <example>
/// <summary>Reference multiple commands</summary>
/// <prefix>PS&gt; </prefix>
/// <code>New-ConfigurationCommand -ModuleName 'PSWriteColor' -CommandName 'Write-Color','Write-Text'</code>
/// <para>Declares multiple command references from the same module.</para>
/// </example>
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
        WriteObject(new ConfigurationCommandSegment
        {
            Configuration = new CommandConfiguration
            {
                ModuleName = ModuleName,
                CommandName = CommandName
            }
        });
    }
}
