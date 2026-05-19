using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Removes a saved private module repository profile.
/// </summary>
/// <remarks>
/// <para>
/// Removing a profile deletes only PSPublishModule's non-secret feed settings. It does not unregister any PowerShell
/// repository and does not clear Azure Artifacts Credential Provider token caches.
/// </para>
/// </remarks>
/// <example>
/// <summary>Remove a saved private gallery profile</summary>
/// <code>Remove-ModuleRepositoryProfile -Name Company</code>
/// <para>Deletes the saved <c>Company</c> profile from the current user's profile store.</para>
/// </example>
/// <example>
/// <summary>Remove a profile and return whether it existed</summary>
/// <code>Remove-ModuleRepositoryProfile -Name Company -PassThru</code>
/// <para>Returns <c>True</c> when the profile was removed, otherwise <c>False</c>.</para>
/// </example>
[Cmdlet(VerbsCommon.Remove, "ModuleRepositoryProfile", SupportsShouldProcess = true)]
[Alias("Remove-GalleryProfile")]
[OutputType(typeof(bool))]
public sealed class RemoveModuleRepositoryProfileCommand : PSCmdlet
{
    /// <summary>Profile name to remove.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [Alias("ProfileName")]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    /// <summary>Returns true when a profile was removed, otherwise false.</summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>Removes the profile.</summary>
    protected override void ProcessRecord()
    {
        var store = new ModuleRepositoryProfileStore();
        var normalizedName = ModuleRepositoryProfileStore.NormalizeName(Name);

        if (!ShouldProcess(normalizedName, "Remove module repository profile"))
            return;

        var removed = store.RemoveProfile(normalizedName);
        if (PassThru)
            WriteObject(removed);
    }
}
