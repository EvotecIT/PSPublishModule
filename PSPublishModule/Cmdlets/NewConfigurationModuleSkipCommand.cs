using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Provides a way to ignore certain commands or modules during build-time dependency validation.
/// </summary>
/// <remarks>
/// <para>
/// Missing module-backed commands fail the build by default. Use this command for optional dependencies where you
/// want selected missing modules or commands to be ignored. <c>-Force</c> keeps the legacy broad opt-out behavior.
/// </para>
/// </remarks>
/// <example>
/// <summary>Ignore an optional module and continue on missing dependency</summary>
/// <prefix>PS&gt; </prefix>
/// <code>New-ConfigurationModuleSkip -IgnoreModuleName 'PSScriptAnalyzer' -Force</code>
/// <para>Prevents build failure when the module is not installed in the environment.</para>
/// </example>
/// <example>
/// <summary>Ignore specific functions for cross-platform builds</summary>
/// <prefix>PS&gt; </prefix>
/// <code>New-ConfigurationModuleSkip -IgnoreModuleName 'Microsoft.PowerShell.Security' -IgnoreFunctionName 'Get-AuthenticodeSignature','Set-AuthenticodeSignature' -Force</code>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationModuleSkip")]
public sealed class NewConfigurationModuleSkipCommand : PSCmdlet
{
    /// <summary>Ignore module name(s) during missing-command validation.</summary>
    [Parameter] public string[]? IgnoreModuleName { get; set; }

    /// <summary>Ignore command/function name(s) during missing-command validation.</summary>
    [Parameter] public string[]? IgnoreFunctionName { get; set; }

    /// <summary>Force the build process to continue even if modules or commands are not available.</summary>
    [Parameter] public SwitchParameter Force { get; set; }

    /// <summary>Fail build when unresolved commands are detected during merge. This is the default and is retained for compatibility.</summary>
    [Parameter] public SwitchParameter FailOnMissingCommands { get; set; }

    /// <summary>Emits module-skip configuration for the build pipeline.</summary>
    protected override void ProcessRecord()
    {
        var inner = new ModuleSkipConfiguration
        {
            IgnoreModuleName = IgnoreModuleName,
            IgnoreFunctionName = IgnoreFunctionName,
            Force = Force.IsPresent,
            FailOnMissingCommands = FailOnMissingCommands.IsPresent
        };

        WriteObject(new ConfigurationModuleSkipSegment { Configuration = inner });
    }
}
