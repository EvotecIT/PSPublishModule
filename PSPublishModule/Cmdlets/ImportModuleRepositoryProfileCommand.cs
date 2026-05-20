using System;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Imports private module repository profiles from a non-secret JSON file.
/// </summary>
/// <remarks>
/// <para>
/// Use this cmdlet on managed workstations, build agents, or administrator consoles to load private gallery profiles
/// that were created with <c>Export-ModuleRepositoryProfile</c>. Imported profiles still contain only feed identity
/// and local behavior settings; authentication remains owned by PSResourceGet and the Azure Artifacts Credential
/// Provider.
/// </para>
/// </remarks>
/// <example>
/// <summary>Import managed private gallery profiles</summary>
/// <code>Import-ModuleRepositoryProfile -Path .\profiles.json</code>
/// <para>Imports profiles from the JSON file into the current user's PSPublishModule profile store.</para>
/// </example>
/// <example>
/// <summary>Refresh existing managed profiles</summary>
/// <code>Import-ModuleRepositoryProfile -Path .\profiles.json -Overwrite</code>
/// <para>Replaces matching profile names with the definitions from the JSON file.</para>
/// </example>
[Cmdlet(VerbsData.Import, "ModuleRepositoryProfile", SupportsShouldProcess = true)]
[Alias("Import-GalleryProfile")]
[OutputType(typeof(ModuleRepositoryProfileResult))]
public sealed class ImportModuleRepositoryProfileCommand : PSCmdlet
{
    /// <summary>Source JSON file path.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <summary>Replace saved profiles with the same name.</summary>
    [Parameter]
    public SwitchParameter Overwrite { get; set; }

    /// <summary>Profile store scope to write. Use Machine from an elevated/admin deployment to share non-secret feed settings with all users.</summary>
    [Parameter]
    public ModuleRepositoryProfileScope Scope { get; set; } = ModuleRepositoryProfileScope.User;

    /// <summary>Imports profiles from the file.</summary>
    protected override void ProcessRecord()
    {
        if (Scope == ModuleRepositoryProfileScope.All)
            throw new ArgumentException("Import-ModuleRepositoryProfile requires User or Machine scope.", nameof(Scope));

        var store = new ModuleRepositoryProfileStore(Scope);
        var resolvedPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path);
        var profiles = ModuleRepositoryProfileStore.ReadProfilesFile(resolvedPath);

        if (!ShouldProcess(store.Path, $"Import {profiles.Length} module repository profile(s) from '{resolvedPath}'"))
            return;

        var imported = store.ImportProfiles(profiles, Overwrite);
        var results = imported
            .Select(profile => ModuleRepositoryProfileResultMapper.ToCmdletResult(profile, store.Path, store.Scope))
            .ToArray();
        WriteObject(results, enumerateCollection: true);
    }
}
