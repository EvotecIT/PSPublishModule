using System;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Gets saved private module repository profiles.
/// </summary>
[Cmdlet(VerbsCommon.Get, "ModuleRepositoryProfile")]
[Alias("Get-GalleryProfile")]
[OutputType(typeof(ModuleRepositoryProfileResult))]
public sealed class GetModuleRepositoryProfileCommand : PSCmdlet
{
    /// <summary>Optional profile name. When omitted, all profiles are returned.</summary>
    [Parameter(Position = 0)]
    [Alias("ProfileName")]
    public string? Name { get; set; }

    /// <summary>Gets saved profiles.</summary>
    protected override void ProcessRecord()
    {
        var store = new ModuleRepositoryProfileStore();
        if (string.IsNullOrWhiteSpace(Name))
        {
            var profiles = store.GetProfiles()
                .Select(profile => ModuleRepositoryProfileResultMapper.ToCmdletResult(profile, store.Path))
                .ToArray();
            WriteObject(profiles, enumerateCollection: true);
            return;
        }

        var profile = store.GetProfile(Name!);
        if (profile is null)
        {
            WriteError(new ErrorRecord(
                new InvalidOperationException($"Module repository profile '{Name}' was not found."),
                "ModuleRepositoryProfileNotFound",
                ErrorCategory.ObjectNotFound,
                Name));
            return;
        }

        WriteObject(ModuleRepositoryProfileResultMapper.ToCmdletResult(profile, store.Path));
    }
}
