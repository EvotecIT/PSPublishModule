using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Creates a managed workstation bootstrap package for private module repository onboarding.
/// </summary>
/// <remarks>
/// <para>
/// The generated package contains a non-secret profile JSON file and a PowerShell script that imports the profile,
/// installs requested prerequisites, connects to the private gallery, and optionally installs approved modules through
/// <c>Install-PrivateModule</c>. The package does not contain PATs, passwords, Entra tokens, or Azure Artifacts
/// Credential Provider session caches.
/// </para>
/// </remarks>
/// <example>
/// <summary>Create a workstation bootstrap package</summary>
/// <code>New-ModuleRepositoryBootstrap -ProfileName Company -OutputDirectory .\CompanyGallery -InstallModule ModuleA, ModuleB -Force</code>
/// <para>Writes profiles.json and Initialize-PrivateGallery.ps1 for managed desktop rollout.</para>
/// </example>
[Cmdlet(VerbsCommon.New, "ModuleRepositoryBootstrap", SupportsShouldProcess = true)]
[Alias("New-GalleryBootstrap")]
[OutputType(typeof(ModuleRepositoryBootstrapScriptPackage))]
public sealed class NewModuleRepositoryBootstrapCommand : PSCmdlet
{
    /// <summary>Optional saved profile names to include. When omitted, all saved profiles are included.</summary>
    [Parameter(Position = 0)]
    [Alias("Name", "Profile")]
    public string[]? ProfileName { get; set; }

    /// <summary>Destination directory for the generated bootstrap package.</summary>
    [Parameter(Mandatory = true, Position = 1)]
    [ValidateNotNullOrEmpty]
    public string OutputDirectory { get; set; } = string.Empty;

    /// <summary>Generated bootstrap script file name.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string ScriptName { get; set; } = "Initialize-PrivateGallery.ps1";

    /// <summary>Generated non-secret profile JSON file name.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string ProfileFileName { get; set; } = "profiles.json";

    /// <summary>Optional module names pre-populated into the generated bootstrap script.</summary>
    [Parameter]
    [Alias("ModuleName")]
    public string[]? InstallModule { get; set; }

    /// <summary>Overwrite existing generated files.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>Profile store scope to read. The default reads user profiles first, then machine-wide profiles.</summary>
    [Parameter]
    public ModuleRepositoryProfileScope Scope { get; set; } = ModuleRepositoryProfileScope.All;

    /// <summary>Creates the bootstrap package.</summary>
    protected override void ProcessRecord()
    {
        var profiles = ProfileName is null || ProfileName.Length == 0
            ? ModuleRepositoryProfileCommandSupport.GetUniqueProfilesWithStores(Scope)
                .Select(resolved => resolved.Profile)
                .ToArray()
            : ModuleRepositoryProfileCommandSupport.ResolveUniqueProfiles(ProfileName, Scope);
        var resolvedOutputDirectory = SessionState.Path.GetUnresolvedProviderPathFromPSPath(OutputDirectory);
        if (!ShouldProcess(resolvedOutputDirectory, $"Create private gallery bootstrap package for {profiles.Length} profile(s)"))
            return;

        var result = ModuleRepositoryBootstrapScriptBuilder.WritePackage(new ModuleRepositoryBootstrapScriptOptions
        {
            OutputDirectory = resolvedOutputDirectory,
            ScriptName = ScriptName,
            ProfileFileName = ProfileFileName,
            Profiles = profiles,
            InstallModules = InstallModule ?? System.Array.Empty<string>(),
            Force = Force.IsPresent
        });

        WriteObject(result);
    }
}
