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

    /// <summary>Profile store scope to test. The default reads user profiles first, then machine-wide profiles.</summary>
    [Parameter]
    public ModuleRepositoryProfileScope Scope { get; set; } = ModuleRepositoryProfileScope.All;

    /// <summary>Runs the readiness test.</summary>
    protected override void ProcessRecord()
    {
        var host = new CmdletPrivateGalleryHost(this);
        var service = new PrivateGalleryService(host);
        var status = service.GetBootstrapPrerequisiteStatus();

        if (string.IsNullOrWhiteSpace(ProfileName))
        {
            var results = ModuleRepositoryProfileCommandSupport.GetUniqueProfilesWithStores(Scope)
                .Select(resolved => ModuleRepositoryProfileReadinessMapper.ToCmdletResult(
                    resolved.Profile,
                    resolved.Store.Path,
                    status,
                    resolved.Store.Scope))
                .ToArray();
            WriteObject(results, enumerateCollection: true);
            return;
        }

        var stores = ModuleRepositoryProfileStore.GetStores(Scope);
        foreach (var store in stores)
        {
            var profile = store.GetProfile(ProfileName!);
            if (profile is not null)
            {
                WriteObject(ModuleRepositoryProfileReadinessMapper.ToCmdletResult(profile, store.Path, status, store.Scope));
                return;
            }
        }

        var primaryStore = stores.First();
        WriteObject(ModuleRepositoryProfileReadinessMapper.ToMissingProfileResult(ProfileName!, primaryStore.Path, primaryStore.Scope));
    }
}
