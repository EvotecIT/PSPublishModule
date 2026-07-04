using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Resets managed module repository profiles to PSPublishModule defaults.
/// </summary>
[Cmdlet(VerbsCommon.Reset, "ManagedModuleRepository", SupportsShouldProcess = true)]
[OutputType(typeof(ModuleRepositoryProfileResult))]
public sealed class ResetManagedModuleRepositoryCommand : PSCmdlet
{
    /// <summary>Returns the default profile written by the reset operation.</summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>Profile store scope to reset.</summary>
    [Parameter]
    public ModuleRepositoryProfileScope Scope { get; set; } = ModuleRepositoryProfileScope.User;

    /// <summary>Resets the selected profile store to its default profile set.</summary>
    protected override void ProcessRecord()
    {
        if (Scope == ModuleRepositoryProfileScope.All)
            throw new ArgumentException("Reset-ManagedModuleRepository requires User or Machine scope.", nameof(Scope));

        var store = new ModuleRepositoryProfileStore(Scope);
        if (!ShouldProcess(store.Path, "Reset managed module repository profiles"))
            return;

        var profiles = store.ReplaceProfiles(new[]
        {
            ManagedModuleRepositoryProfileFactory.CreatePowerShellGalleryProfile(trusted: false, priority: null)
        });

        if (PassThru)
        {
            foreach (var profile in profiles)
                WriteObject(ModuleRepositoryProfileResultMapper.ToCmdletResult(profile, store.Path, store.Scope));
        }
    }
}
