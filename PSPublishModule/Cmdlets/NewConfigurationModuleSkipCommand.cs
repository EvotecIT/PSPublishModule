using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Provides a way to ignore certain commands or modules during build process and continue module building on errors.
/// </summary>
/// <remarks>
/// <para>
/// Useful for optional dependencies where you want builds to succeed even if a tool module is not available
/// (e.g. optional analyzers, formatters, helpers).
/// </para>
/// </remarks>
/// <example>
/// <summary>Ignore an optional module and continue on missing dependency</summary>
/// <prefix>PS&gt; </prefix>
/// <code>New-ConfigurationModuleSkip -IgnoreModuleName 'PSScriptAnalyzer' -Force</code>
/// <para>Prevents build failure when the module is not installed in the environment.</para>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationModuleSkip")]
public sealed class NewConfigurationModuleSkipCommand : PSCmdlet
{
    /// <summary>Ignore module name(s). If the module is not available it will be ignored.</summary>
    [Parameter] public string[]? IgnoreModuleName { get; set; }

    /// <summary>Ignore function name(s). If the function is not available it will be ignored.</summary>
    [Parameter] public string[]? IgnoreFunctionName { get; set; }

    /// <summary>Force build process to continue even if the module or command is not available.</summary>
    [Parameter] public SwitchParameter Force { get; set; }

    /// <summary>Emits module-skip configuration for the build pipeline.</summary>
    protected override void ProcessRecord()
    {
        var inner = new ModuleSkipConfiguration
        {
            IgnoreModuleName = IgnoreModuleName,
            IgnoreFunctionName = IgnoreFunctionName,
            Force = Force.IsPresent
        };

        WriteObject(new ConfigurationModuleSkipSegment { Configuration = inner });
    }
}
