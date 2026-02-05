using System;
using System.Management.Automation;
using PowerForge;
using PSPublishModule.Services;

namespace PSPublishModule;

/// <summary>
/// Provides a way to configure required, external, or approved modules used in the project.
/// </summary>
/// <remarks>
/// <para>
/// Emits module dependency configuration segments. These are later used to patch the module manifest and (optionally)
/// install/package dependencies during a build.
/// </para>
/// <para>
/// RequiredModule entries are written to the manifest <c>RequiredModules</c>. ExternalModule entries are written to
/// <c>PrivateData.PSData.ExternalModuleDependencies</c> (not packaged into artefacts).
/// </para>
/// <para>
/// Version/Guid values set to <c>Auto</c> or <c>Latest</c> are resolved from installed modules; when
/// <c>ResolveMissingModulesOnline</c> is enabled, repository results are used without installing.
/// </para>
/// </remarks>
/// <example>
/// <summary>Add a required module dependency</summary>
/// <prefix>PS&gt; </prefix>
/// <code>New-ConfigurationModule -Type RequiredModule -Name 'Pester' -Version '5.6.1'</code>
/// <para>Declares a required dependency that is written into the manifest.</para>
/// </example>
/// <example>
/// <summary>Add an external module (required but not packaged)</summary>
/// <prefix>PS&gt; </prefix>
/// <code>New-ConfigurationModule -Type ExternalModule -Name 'Az.Accounts' -Version 'Latest'</code>
/// <para>Declares a dependency that is expected to be installed separately (not bundled into artefacts).</para>
/// </example>
/// <example>
/// <summary>Pin an exact required version</summary>
/// <prefix>PS&gt; </prefix>
/// <code>New-ConfigurationModule -Type RequiredModule -Name 'PSWriteColor' -RequiredVersion '1.0.0'</code>
/// <para>Uses RequiredVersion when an exact match is required.</para>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationModule")]
public sealed class NewConfigurationModuleCommand : PSCmdlet
{
    /// <summary>Choose between RequiredModule, ExternalModule and ApprovedModule.</summary>
    [Parameter] public PowerForge.ModuleDependencyKind Type { get; set; } = PowerForge.ModuleDependencyKind.RequiredModule;

    /// <summary>Name of the PowerShell module(s) that your module depends on.</summary>
    [Parameter(Mandatory = true)] public string[] Name { get; set; } = Array.Empty<string>();

    /// <summary>Minimum version of the dependency module (or 'Auto'/'Latest').</summary>
    [Parameter]
    [ArgumentCompleter(typeof(AutoLatestCompleter))]
    public string? Version { get; set; }

    /// <summary>Minimum version of the dependency module (preferred over -Version).</summary>
    [Parameter]
    [ArgumentCompleter(typeof(AutoLatestCompleter))]
    public string? MinimumVersion { get; set; }

    /// <summary>Required version of the dependency module (exact match).</summary>
    [Parameter] public string? RequiredVersion { get; set; }

    /// <summary>GUID of the dependency module (or 'Auto').</summary>
    [Parameter]
    [ArgumentCompleter(typeof(AutoLatestCompleter))]
    public string? Guid { get; set; }

    /// <summary>Emits module dependency configuration objects.</summary>
    protected override void ProcessRecord()
    {
        if (!string.IsNullOrWhiteSpace(Version) && !string.IsNullOrWhiteSpace(MinimumVersion))
            throw new PSArgumentException("You cannot use both Version and MinimumVersion at the same time for the same module. Please choose one or the other (New-ConfigurationModule).");

        var min = string.IsNullOrWhiteSpace(MinimumVersion) ? Version : MinimumVersion;

        if (!string.IsNullOrWhiteSpace(RequiredVersion) && !string.IsNullOrWhiteSpace(min))
            throw new PSArgumentException("You cannot use both RequiredVersion and a minimum version for the same module. Please choose one or the other (New-ConfigurationModule).");

        foreach (var n in Name)
        {
            var cfg = new ConfigurationModuleSegment
            {
                Kind = Type,
                Configuration = new ModuleDependencyConfiguration
                {
                    ModuleName = n,
                    ModuleVersion = null,
                    MinimumVersion = min,
                    RequiredVersion = RequiredVersion,
                    Guid = Guid
                }
            };

            WriteObject(cfg);
        }
    }
}
