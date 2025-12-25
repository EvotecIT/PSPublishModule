using System;
using System.Collections.Specialized;
using System.Management.Automation;
using PSPublishModule.Services;

namespace PSPublishModule;

/// <summary>
/// Provides a way to configure required, external, or approved modules used in the project.
/// </summary>
[Cmdlet(VerbsCommon.New, "ConfigurationModule")]
public sealed class NewConfigurationModuleCommand : PSCmdlet
{
    /// <summary>Choose between RequiredModule, ExternalModule and ApprovedModule.</summary>
    [Parameter] public ModuleDependencyKind Type { get; set; } = ModuleDependencyKind.RequiredModule;

    /// <summary>Name of the PowerShell module(s) that your module depends on.</summary>
    [Parameter(Mandatory = true)] public string[] Name { get; set; } = Array.Empty<string>();

    /// <summary>Minimum version of the dependency module (or 'Latest').</summary>
    [Parameter]
    [ArgumentCompleter(typeof(AutoLatestCompleter))]
    public string? Version { get; set; }

    /// <summary>Required version of the dependency module (exact match).</summary>
    [Parameter] public string? RequiredVersion { get; set; }

    /// <summary>GUID of the dependency module (or 'Auto').</summary>
    [Parameter]
    [ArgumentCompleter(typeof(AutoLatestCompleter))]
    public string? Guid { get; set; }

    /// <summary>Emits module dependency configuration objects.</summary>
    protected override void ProcessRecord()
    {
        if (!string.IsNullOrWhiteSpace(Version) && !string.IsNullOrWhiteSpace(RequiredVersion))
            throw new PSArgumentException("You cannot use both Version and RequiredVersion at the same time for the same module. Please choose one or the other (New-ConfigurationModule) ");

        foreach (var n in Name)
        {
            object configuration;
            if (Type == ModuleDependencyKind.ApprovedModule)
            {
                configuration = n;
            }
            else
            {
                var moduleInfo = new OrderedDictionary
                {
                    ["ModuleName"] = n,
                    ["ModuleVersion"] = Version,
                    ["RequiredVersion"] = RequiredVersion,
                    ["Guid"] = Guid
                };
                EmptyValuePruner.RemoveEmptyValues(moduleInfo);

                if (moduleInfo.Count == 1 && moduleInfo.Contains("ModuleName"))
                    configuration = n;
                else
                    configuration = moduleInfo;
            }

            var cfg = new OrderedDictionary
            {
                ["Type"] = Type.ToString(),
                ["Configuration"] = configuration
            };

            WriteObject(cfg);
        }
    }
}
