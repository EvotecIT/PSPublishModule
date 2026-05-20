using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Exports saved private module repository profiles to a non-secret JSON file.
/// </summary>
/// <remarks>
/// <para>
/// Use this cmdlet when preparing managed desktop rollout artifacts for private galleries. The exported file contains
/// feed identity, registration preferences, trust, priority, and authentication mode metadata only. It does not contain
/// PATs, passwords, Entra tokens, or Azure Artifacts Credential Provider session caches.
/// </para>
/// </remarks>
/// <example>
/// <summary>Export all saved profiles</summary>
/// <code>Export-ModuleRepositoryProfile -Path .\profiles.json -Force</code>
/// <para>Writes every saved private gallery profile to a JSON file that can be deployed to workstations.</para>
/// </example>
/// <example>
/// <summary>Export one profile</summary>
/// <code>Export-ModuleRepositoryProfile -Name Company -Path .\Company.profile.json -PassThru</code>
/// <para>Exports only the Company profile and returns the exported profile object.</para>
/// </example>
[Cmdlet(VerbsData.Export, "ModuleRepositoryProfile", SupportsShouldProcess = true)]
[Alias("Export-GalleryProfile")]
[OutputType(typeof(ModuleRepositoryProfileResult))]
public sealed class ExportModuleRepositoryProfileCommand : PSCmdlet
{
    /// <summary>Optional profile names to export. When omitted, all saved profiles are exported.</summary>
    [Parameter(Position = 0)]
    [Alias("ProfileName")]
    public string[]? Name { get; set; }

    /// <summary>Destination JSON file path.</summary>
    [Parameter(Mandatory = true, Position = 1)]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <summary>Overwrite an existing destination file.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>Returns the exported profile objects.</summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>Profile store scope to export.</summary>
    [Parameter]
    public ModuleRepositoryProfileScope Scope { get; set; } = ModuleRepositoryProfileScope.User;

    /// <summary>Exports selected profiles.</summary>
    protected override void ProcessRecord()
    {
        if (Scope == ModuleRepositoryProfileScope.All)
            throw new ArgumentException("Export-ModuleRepositoryProfile requires User or Machine scope.", nameof(Scope));

        var store = new ModuleRepositoryProfileStore(Scope);
        var profiles = store.GetProfiles(Name);
        var resolvedPath = SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path);

        if (File.Exists(resolvedPath) && !Force)
        {
            throw new IOException($"File '{resolvedPath}' already exists. Use -Force to overwrite it.");
        }

        if (!ShouldProcess(resolvedPath, $"Export {profiles.Length} module repository profile(s)"))
            return;

        store.WriteProfilesFile(resolvedPath, profiles);
        if (PassThru)
        {
            var results = profiles
                .Select(profile => ModuleRepositoryProfileResultMapper.ToCmdletResult(profile, store.Path, store.Scope))
                .ToArray();
            WriteObject(results, enumerateCollection: true);
        }
    }
}
