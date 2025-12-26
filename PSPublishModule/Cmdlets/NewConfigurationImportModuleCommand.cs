using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates a configuration for importing PowerShell modules.
/// </summary>
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
