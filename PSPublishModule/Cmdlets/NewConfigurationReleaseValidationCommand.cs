using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>Creates a reusable package and runtime validation configuration.</summary>
/// <para>Use typed objects or PowerShell hashtables for product expectations. The same configuration can be executed directly or exported as JSON.</para>
/// <example>
/// <summary>Check an installed tool from a local package feed</summary>
/// <code>
/// New-ConfigurationReleaseValidation -Tools @{
///     PackageId = 'Example.Tool'; PackageRoot = 'Artifacts/packages'; CommandName = 'example'
///     Commands = @(@{ Name = 'Version'; FileName = '{ToolPath}'; Arguments = @('--version'); ExpectedOutput = '{Version}' })
/// }
/// </code>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationReleaseValidation")]
[OutputType(typeof(ReleaseValidationSpec))]
public sealed class NewConfigurationReleaseValidationCommand : PSCmdlet
{
    /// <para>Root for relative input paths.</para>
    [Parameter] public string ProjectRoot { get; set; } = ".";
    /// <para>NuGet package identities, payloads, dependencies, and signing expectations.</para>
    [Parameter] public PackageSetValidation? Packages { get; set; }
    /// <para>Module archives or directories and optional product smoke scripts.</para>
    [Parameter] public ModuleArtifactValidation[] Modules { get; set; } = Array.Empty<ModuleArtifactValidation>();
    /// <para>CLI manifest and publish matrix expectations.</para>
    [Parameter] public CliArtifactValidation? CliArtifacts { get; set; }
    /// <para>Local .NET tool package installation checks.</para>
    [Parameter] public DotNetToolValidation[] Tools { get; set; } = Array.Empty<DotNetToolValidation>();
    /// <para>Product-owned projects to run against the staged packages.</para>
    [Parameter] public PackageConsumerValidation[] Consumers { get; set; } = Array.Empty<PackageConsumerValidation>();
    /// <para>Additional product commands and output expectations.</para>
    [Parameter] public ReleaseCommandValidation[] Commands { get; set; } = Array.Empty<ReleaseCommandValidation>();
    /// <para>Include the shared JSON schema reference when exporting.</para>
    [Parameter] public SwitchParameter IncludeSchema { get; set; }

    /// <summary>Emits the typed configuration; validation remains in the shared engine.</summary>
    protected override void ProcessRecord() => WriteObject(new ReleaseValidationSpec
    {
        ProjectRoot = ProjectRoot, Packages = Packages, Modules = Modules, CliArtifacts = CliArtifacts,
        Tools = Tools, Consumers = Consumers, Commands = Commands,
        Schema = IncludeSchema ? "https://raw.githubusercontent.com/EvotecIT/PSPublishModule/main/Schemas/powerforge.release-validation.schema.json" : null
    });
}
