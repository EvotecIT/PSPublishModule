using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Unregisters a saved managed module repository profile.
/// </summary>
[Cmdlet(VerbsLifecycle.Unregister, "ManagedModuleRepository", SupportsShouldProcess = true)]
[OutputType(typeof(bool))]
public sealed class UnregisterManagedModuleRepositoryCommand : PSCmdlet
{
    /// <summary>Profile name to unregister.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [Alias("ProfileName")]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    /// <summary>Returns true when a profile was removed, otherwise false.</summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>Profile store scope to unregister from.</summary>
    [Parameter]
    public ModuleRepositoryProfileScope Scope { get; set; } = ModuleRepositoryProfileScope.User;

    /// <summary>Unregisters the profile.</summary>
    protected override void ProcessRecord()
    {
        if (Scope == ModuleRepositoryProfileScope.All)
            throw new ArgumentException("Unregister-ManagedModuleRepository requires User or Machine scope.", nameof(Scope));

        var store = new ModuleRepositoryProfileStore(Scope);
        var normalizedName = ModuleRepositoryProfileStore.NormalizeName(Name);

        if (!ShouldProcess(normalizedName, "Unregister managed module repository profile"))
            return;

        var removed = store.RemoveProfile(normalizedName);
        if (PassThru)
            WriteObject(removed);
    }
}
