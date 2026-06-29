using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Removes a saved managed module repository profile.
/// </summary>
/// <remarks>
/// <para>
/// Removing a profile deletes only PSPublishModule's non-secret repository settings. It does not unregister native
/// PowerShell repository state or clear external credential-provider token caches.
/// </para>
/// </remarks>
/// <example>
/// <summary>Remove a saved repository profile</summary>
/// <code>Remove-ManagedModuleRepository -Name Company</code>
/// <para>Deletes the saved <c>Company</c> profile from the current user's profile store.</para>
/// </example>
/// <example>
/// <summary>Remove a profile and return whether it existed</summary>
/// <code>Remove-ManagedModuleRepository -Name Company -PassThru</code>
/// <para>Returns <c>True</c> when the profile was removed, otherwise <c>False</c>.</para>
/// </example>
[Cmdlet(VerbsCommon.Remove, "ManagedModuleRepository", SupportsShouldProcess = true)]
[OutputType(typeof(bool))]
public sealed class RemoveManagedModuleRepositoryCommand : PSCmdlet
{
    /// <summary>Profile name to remove.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [Alias("ProfileName")]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    /// <summary>Returns true when a profile was removed, otherwise false.</summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>Profile store scope to remove from.</summary>
    [Parameter]
    public ModuleRepositoryProfileScope Scope { get; set; } = ModuleRepositoryProfileScope.User;

    /// <summary>Removes the profile.</summary>
    protected override void ProcessRecord()
    {
        if (Scope == ModuleRepositoryProfileScope.All)
            throw new ArgumentException("Remove-ManagedModuleRepository requires User or Machine scope.", nameof(Scope));

        var store = new ModuleRepositoryProfileStore(Scope);
        var normalizedName = ModuleRepositoryProfileStore.NormalizeName(Name);

        if (!ShouldProcess(normalizedName, "Remove module repository profile"))
            return;

        var removed = store.RemoveProfile(normalizedName);
        if (PassThru)
            WriteObject(removed);
    }
}
