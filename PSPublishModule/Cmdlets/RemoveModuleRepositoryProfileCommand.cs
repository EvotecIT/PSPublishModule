using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Removes a saved private module repository profile.
/// </summary>
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
