using System;
using System.Management.Automation;
using PowerForge;
using PSPublishModule.Services;

namespace PSPublishModule;

/// <summary>
/// Provides a way to configure required, external, or approved modules used in the project.
/// </summary>
[Cmdlet(VerbsCommon.New, "ConfigurationModule")]
public sealed class NewConfigurationModuleCommand : PSCmdlet
{
    /// <summary>Choose between RequiredModule, ExternalModule and ApprovedModule.</summary>
    [Parameter] public PowerForge.ModuleDependencyKind Type { get; set; } = PowerForge.ModuleDependencyKind.RequiredModule;

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
            var cfg = new ConfigurationModuleSegment
            {
                Kind = Type,
                Configuration = new ModuleDependencyConfiguration
                {
                    ModuleName = n,
                    ModuleVersion = Version,
                    RequiredVersion = RequiredVersion,
                    Guid = Guid
                }
            };

            WriteObject(cfg);
        }
    }
}
