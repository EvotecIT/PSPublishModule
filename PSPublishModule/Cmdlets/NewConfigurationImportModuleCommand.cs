using System.Collections.Specialized;
using System.Management.Automation;

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
        var importModules = new OrderedDictionary();
        if (MyInvocation.BoundParameters.ContainsKey(nameof(ImportSelf)))
            importModules["Self"] = ImportSelf.IsPresent;
        if (MyInvocation.BoundParameters.ContainsKey(nameof(ImportRequiredModules)))
            importModules["RequiredModules"] = ImportRequiredModules.IsPresent;
        if (MyInvocation.BoundParameters.ContainsKey("Verbose"))
            importModules["Verbose"] = true;

        var cfg = new OrderedDictionary
        {
            ["Type"] = "ImportModules",
            ["ImportModules"] = importModules
        };

        WriteObject(cfg);
    }
}

