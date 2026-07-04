using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Imports managed module repository profiles from a non-secret profile file.
/// </summary>
[Cmdlet(VerbsData.Import, "ManagedModuleRepository", SupportsShouldProcess = true)]
[OutputType(typeof(ModuleRepositoryProfileResult))]
public sealed class ImportManagedModuleRepositoryCommand : PSCmdlet
{
    /// <summary>Path to a profile file exported by Get-ManagedModuleRepository -ExportPath.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [Alias("RequiredResourceFile")]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <summary>Replaces existing managed repository profiles with matching names.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>Returns imported profiles. The command is quiet by default, like Import-PSGetRepository.</summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>Profile store scope to import into.</summary>
    [Parameter]
    public ModuleRepositoryProfileScope Scope { get; set; } = ModuleRepositoryProfileScope.User;

    /// <summary>Imports the profile file.</summary>
    protected override void ProcessRecord()
    {
        if (Scope == ModuleRepositoryProfileScope.All)
            throw new ArgumentException("Import-ManagedModuleRepository requires User or Machine scope.", nameof(Scope));

        var resolvedPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path);
        var profiles = ModuleRepositoryProfileStore.ReadProfilesFile(resolvedPath);
        var store = new ModuleRepositoryProfileStore(Scope);
        if (!ShouldProcess(store.Path, $"Import {profiles.Length} managed module repository profile(s) from '{resolvedPath}'"))
            return;

        var imported = store.ImportProfiles(profiles, Force.IsPresent);
        if (PassThru)
        {
            foreach (var profile in imported)
                WriteObject(ModuleRepositoryProfileResultMapper.ToCmdletResult(profile, store.Path, store.Scope));
        }
    }
}
