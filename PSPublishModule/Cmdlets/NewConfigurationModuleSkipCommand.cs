using System.Collections.Specialized;
using System.Management.Automation;
using PSPublishModule.Services;

namespace PSPublishModule;

/// <summary>
/// Provides a way to ignore certain commands or modules during build process and continue module building on errors.
/// </summary>
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
        var inner = new OrderedDictionary
        {
            ["IgnoreModuleName"] = IgnoreModuleName,
            ["IgnoreFunctionName"] = IgnoreFunctionName,
            ["Force"] = Force.IsPresent
        };
        EmptyValuePruner.RemoveEmptyValues(inner);

        var cfg = new OrderedDictionary
        {
            ["Type"] = "ModuleSkip",
            ["Configuration"] = inner
        };

        WriteObject(cfg);
    }
}

