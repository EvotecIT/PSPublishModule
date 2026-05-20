using System;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Gets saved private module repository profiles.
/// </summary>
/// <remarks>
/// <para>
/// Profiles contain non-secret private gallery settings. Use this cmdlet to review the local repository name,
/// Azure Artifacts feed identity, bootstrap mode, and profile store path before connecting or publishing.
/// </para>
/// </remarks>
/// <example>
/// <summary>List all saved private gallery profiles</summary>
/// <code>Get-ModuleRepositoryProfile</code>
/// <para>Returns all profiles saved in the current user's PSPublishModule profile store.</para>
/// </example>
/// <example>
/// <summary>Inspect one saved profile</summary>
/// <code>Get-ModuleRepositoryProfile -Name Company</code>
/// <para>Returns the saved Azure Artifacts profile named <c>Company</c>.</para>
/// </example>
[Cmdlet(VerbsCommon.Get, "ModuleRepositoryProfile")]
[Alias("Get-GalleryProfile")]
[OutputType(typeof(ModuleRepositoryProfileResult))]
public sealed class GetModuleRepositoryProfileCommand : PSCmdlet
{
    /// <summary>Optional profile name. When omitted, all profiles are returned.</summary>
    [Parameter(Position = 0)]
    [Alias("ProfileName")]
    public string? Name { get; set; }

    /// <summary>Profile store scope to read. The default reads user profiles first, then machine-wide profiles.</summary>
    [Parameter]
    public ModuleRepositoryProfileScope Scope { get; set; } = ModuleRepositoryProfileScope.All;

    /// <summary>Gets saved profiles.</summary>
    protected override void ProcessRecord()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            var profiles = ModuleRepositoryProfileCommandSupport.GetUniqueProfilesWithStores(Scope)
                .Select(resolved => ModuleRepositoryProfileResultMapper.ToCmdletResult(
                    resolved.Profile,
                    resolved.Store.Path,
                    resolved.Store.Scope))
                .ToArray();
            WriteObject(profiles, enumerateCollection: true);
            return;
        }

        var stores = ModuleRepositoryProfileStore.GetStores(Scope);
        foreach (var store in stores)
        {
            var profile = store.GetProfile(Name!);
            if (profile is not null)
            {
                WriteObject(ModuleRepositoryProfileResultMapper.ToCmdletResult(profile, store.Path, store.Scope));
                return;
            }
        }

        WriteError(new ErrorRecord(
            new InvalidOperationException($"Module repository profile '{Name}' was not found."),
            "ModuleRepositoryProfileNotFound",
            ErrorCategory.ObjectNotFound,
            Name));
    }
}
