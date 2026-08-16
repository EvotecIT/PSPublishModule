using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates opt-in source-state and provenance protections for module releases.
/// </summary>
/// <remarks>
/// All protections are disabled unless explicitly selected. Generating provenance also requires a clean source
/// snapshot and protects it from changes through packaging.
/// </remarks>
/// <example>
/// <summary>Require clean, unchanged release inputs without embedding provenance</summary>
/// <code>New-ConfigurationReleaseProtection -RequireSourceUnchanged</code>
/// </example>
/// <example>
/// <summary>Generate signed release provenance</summary>
/// <code>New-ConfigurationReleaseProtection -GenerateProvenance</code>
/// </example>
[Cmdlet(VerbsCommon.New, "ConfigurationReleaseProtection")]
[OutputType(typeof(ConfigurationReleaseProtectionSegment))]
public sealed class NewConfigurationReleaseProtectionCommand : PSCmdlet
{
    /// <summary>Requires a clean Git source snapshot when the release pipeline is planned.</summary>
    [Parameter]
    public SwitchParameter RequireCleanSource { get; set; }

    /// <summary>Requires release inputs to remain unchanged after planning and implies a clean source snapshot.</summary>
    [Parameter]
    public SwitchParameter RequireSourceUnchanged { get; set; }

    /// <summary>
    /// Embeds signed source provenance in eligible signed GitHub module artefacts and implies both source checks.
    /// </summary>
    [Parameter]
    public SwitchParameter GenerateProvenance { get; set; }

    /// <summary>Emits a release protection configuration segment.</summary>
    protected override void ProcessRecord()
    {
        WriteObject(new ConfigurationReleaseProtectionSegment
        {
            Configuration = new ReleaseProtectionConfiguration
            {
                RequireCleanSource = RequireCleanSource.IsPresent,
                RequireSourceUnchanged = RequireSourceUnchanged.IsPresent,
                GenerateProvenance = GenerateProvenance.IsPresent
            }
        });
    }
}
