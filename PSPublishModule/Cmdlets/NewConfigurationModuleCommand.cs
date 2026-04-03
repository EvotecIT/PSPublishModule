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
/// Use <c>RequiredModule</c> for dependencies that should appear in the manifest and can also be bundled into build
/// artefacts when <c>New-ConfigurationArtefact -AddRequiredModules</c> is enabled. Use <c>ExternalModule</c> for
/// dependencies that must exist on the target system but should not be bundled into artefacts.
/// </para>
/// <para>
/// <c>RequiredModule</c> entries are written to the manifest <c>RequiredModules</c>. <c>ExternalModule</c> entries
/// are written to <c>PrivateData.PSData.ExternalModuleDependencies</c>. <c>ApprovedModule</c> entries are used by
/// merge/missing-function workflows and are not emitted as manifest dependencies.
/// </para>
/// <para>
/// Built-in <c>Microsoft.PowerShell.*</c> modules are ignored during manifest refresh because they are inbox runtime
/// modules, not gallery-resolvable dependencies.
/// </para>
/// <para>
/// Version and Guid values set to <c>Auto</c> or <c>Latest</c> are resolved from installed modules by default. When
/// <c>New-ConfigurationBuild -ResolveMissingModulesOnline</c> is enabled, repository results can be used without
/// installing the dependency first.
/// </para>
/// <para>
/// Choose only one versioning style per dependency: a minimum version (<c>-Version</c> or
/// <c>-MinimumVersion</c>) or an exact version (<c>-RequiredVersion</c>). Mixing them for the same module is treated
/// as invalid input.
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
/// <example>
/// <summary>Declare an approved helper module for merge-time reuse</summary>
/// <prefix>PS&gt; </prefix>
/// <code>New-ConfigurationModule -Type ApprovedModule -Name 'PSSharedGoods','PSWriteColor'</code>
/// <para>Allows approved helper functions to be copied into the built module when they are actually used.</para>
/// </example>
/// <example>
/// <summary>Declare a dependency whose version can be resolved online later</summary>
/// <prefix>PS&gt; </prefix>
/// <code>New-ConfigurationModule -Type RequiredModule -Name 'Pester' -Version 'Latest' -Guid 'Auto'</code>
/// <para>Pairs well with New-ConfigurationBuild -ResolveMissingModulesOnline when the module is not installed locally.</para>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationModule")]
public sealed class NewConfigurationModuleCommand : PSCmdlet
{
    /// <summary>
    /// Choose between <c>RequiredModule</c>, <c>ExternalModule</c>, and <c>ApprovedModule</c>.
    /// <c>RequiredModule</c> is used for manifest and optional packaging, <c>ExternalModule</c> is install-only, and
    /// <c>ApprovedModule</c> is merge-only.
    /// </summary>
    [Parameter] public PowerForge.ModuleDependencyKind Type { get; set; } = PowerForge.ModuleDependencyKind.RequiredModule;

    /// <summary>
    /// Name of the PowerShell module(s) that your module depends on. Multiple names emit one configuration segment per
    /// module using the same dependency settings.
    /// </summary>
    [Parameter(Mandatory = true)] public string[] Name { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Minimum version of the dependency module (or <c>Auto</c>/<c>Latest</c>). This is treated the same as
    /// <c>-MinimumVersion</c> and cannot be combined with <c>-RequiredVersion</c>.
    /// </summary>
    [Parameter]
    [ArgumentCompleter(typeof(AutoLatestCompleter))]
    public string? Version { get; set; }

    /// <summary>
    /// Minimum version of the dependency module (preferred over <c>-Version</c>). Use this when any newer compatible
    /// version is acceptable.
    /// </summary>
    [Parameter]
    [ArgumentCompleter(typeof(AutoLatestCompleter))]
    public string? MinimumVersion { get; set; }

    /// <summary>
    /// Required version of the dependency module (exact match). Use this when consumers and packaging must resolve the
    /// exact same version.
    /// </summary>
    [Parameter] public string? RequiredVersion { get; set; }

    /// <summary>
    /// GUID of the dependency module (or <c>Auto</c>). This is most useful when you want manifest validation to lock
    /// onto a specific module identity across repositories.
    /// </summary>
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
