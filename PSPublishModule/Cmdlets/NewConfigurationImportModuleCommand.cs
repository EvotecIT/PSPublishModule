using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates a configuration for importing PowerShell modules.
/// </summary>
/// <remarks>
/// <para>
/// Controls which modules are imported during a pipeline run (the module under build itself and/or its RequiredModules).
/// This is primarily used by test and documentation steps that execute PowerShell code.
/// </para>
/// </remarks>
/// <example>
/// <summary>Import the module under build and its RequiredModules</summary>
/// <prefix>PS&gt; </prefix>
/// <code>New-ConfigurationImportModule -ImportSelf -ImportRequiredModules</code>
/// <para>Ensures the pipeline imports the module and required dependencies before running tests or generating docs.</para>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationImportModule")]
public sealed class NewConfigurationImportModuleCommand : PSCmdlet
{
    /// <summary>Indicates whether to import the current module itself.</summary>
    [Parameter] public SwitchParameter ImportSelf { get; set; }

    /// <summary>Indicates whether to import required modules from the manifest.</summary>
    [Parameter] public SwitchParameter ImportRequiredModules { get; set; }

    /// <summary>Emits import configuration for the build pipeline.</summary>
    protected override void ProcessRecord()
    {
        var importModules = new ImportModulesConfiguration
        {
            Self = MyInvocation.BoundParameters.ContainsKey(nameof(ImportSelf)) ? ImportSelf.IsPresent : null,
            RequiredModules = MyInvocation.BoundParameters.ContainsKey(nameof(ImportRequiredModules)) ? ImportRequiredModules.IsPresent : null,
            Verbose = MyInvocation.BoundParameters.ContainsKey("Verbose") ? true : null
        };

        WriteObject(new ConfigurationImportModulesSegment { ImportModules = importModules });
    }
}
