using System;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Tests saved private module repository profiles and local authentication prerequisites.
/// </summary>
/// <remarks>
/// <para>
/// This cmdlet performs a non-mutating readiness check for private gallery onboarding. It reads the saved profile,
/// resolves Azure Artifacts feed endpoints, and reports whether local PSResourceGet, PowerShellGet, and Azure
/// Artifacts Credential Provider prerequisites are ready for Entra-first module install/update flows.
/// </para>
/// </remarks>
/// <example>
/// <summary>Test one saved profile</summary>
/// <code>Test-ModuleRepositoryProfile -ProfileName Company</code>
/// <para>Returns profile and local prerequisite readiness for the saved <c>Company</c> profile.</para>
/// </example>
/// <example>
/// <summary>Test all saved profiles</summary>
/// <code>Test-ModuleRepositoryProfile</code>
/// <para>Returns readiness information for every saved private gallery profile.</para>
/// </example>
[Cmdlet(VerbsDiagnostic.Test, "ModuleRepositoryProfile")]
[Alias("Test-GalleryProfile")]
[OutputType(typeof(ModuleRepositoryProfileReadinessResult))]
public sealed class TestModuleRepositoryProfileCommand : PSCmdlet
{
    /// <summary>Optional profile name. When omitted, all saved profiles are tested.</summary>
    [Parameter(Position = 0)]
    [Alias("Name", "Profile")]
    public string? ProfileName { get; set; }

    /// <summary>Runs the readiness test.</summary>
    protected override void ProcessRecord()
    {
        var store = new ModuleRepositoryProfileStore();
        var host = new CmdletPrivateGalleryHost(this);
        var service = new PrivateGalleryService(host);
        var status = service.GetBootstrapPrerequisiteStatus();

        if (string.IsNullOrWhiteSpace(ProfileName))
        {
            var results = store.GetProfiles()
                .Select(profile => ModuleRepositoryProfileReadinessMapper.ToCmdletResult(profile, store.Path, status))
                .ToArray();
            WriteObject(results, enumerateCollection: true);
            return;
        }

        var profile = store.GetProfile(ProfileName!);
        if (profile is null)
        {
            WriteObject(ModuleRepositoryProfileReadinessMapper.ToMissingProfileResult(ProfileName!, store.Path));
            return;
        }

        WriteObject(ModuleRepositoryProfileReadinessMapper.ToCmdletResult(profile, store.Path, status));
    }
}
