using System;
using System.Collections;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Registers a managed module repository profile using PSResourceGet-shaped parameters.
/// </summary>
[Cmdlet(VerbsLifecycle.Register, "ManagedModuleRepository", DefaultParameterSetName = ParameterSetByName, SupportsShouldProcess = true)]
[OutputType(typeof(ModuleRepositoryProfileResult))]
public sealed class RegisterManagedModuleRepositoryCommand : PSCmdlet
{
    private const string ParameterSetByName = "Name";
    private const string ParameterSetPSGallery = "PSGallery";
    private const string ParameterSetRepository = "Repository";

    /// <summary>Repository profile name.</summary>
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = ParameterSetByName)]
    [Alias("ProfileName")]
    [ValidateNotNullOrEmpty]
    public string? Name { get; set; }

    /// <summary>Repository URI or local feed path.</summary>
    [Parameter(Mandatory = true, Position = 1, ParameterSetName = ParameterSetByName)]
    [Alias("RepositoryUri")]
    [ValidateNotNullOrEmpty]
    public string? Uri { get; set; }

    /// <summary>Registers the built-in PowerShell Gallery profile.</summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetPSGallery)]
    public SwitchParameter PSGallery { get; set; }

    /// <summary>Repository definitions shaped like Register-PSResourceRepository -Repository input.</summary>
    [Parameter(Mandatory = true, ValueFromPipeline = true, ParameterSetName = ParameterSetRepository)]
    [ValidateNotNullOrEmpty]
    public Hashtable[]? Repository { get; set; }

    /// <summary>Marks the repository profile as trusted.</summary>
    [Parameter(ParameterSetName = ParameterSetByName)]
    [Parameter(ParameterSetName = ParameterSetPSGallery)]
    public SwitchParameter Trusted { get; set; }

    /// <summary>Repository priority.</summary>
    [Parameter(ParameterSetName = ParameterSetByName)]
    [Parameter(ParameterSetName = ParameterSetPSGallery)]
    public int? Priority { get; set; }

    /// <summary>Repository API version metadata. ContainerRegistry is handled by Microsoft Artifact Registry onboarding.</summary>
    [Parameter(ParameterSetName = ParameterSetByName)]
    public RepositoryApiVersion ApiVersion { get; set; } = RepositoryApiVersion.Auto;

    /// <summary>Replaces an existing managed repository profile with the same name.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>Returns the registered profile. The command is quiet by default, like Register-PSResourceRepository.</summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>Profile store scope to write.</summary>
    [Parameter]
    public ModuleRepositoryProfileScope Scope { get; set; } = ModuleRepositoryProfileScope.User;

    /// <summary>Registers the profile.</summary>
    protected override void ProcessRecord()
    {
        if (Scope == ModuleRepositoryProfileScope.All)
            throw new ArgumentException("Register-ManagedModuleRepository requires User or Machine scope.", nameof(Scope));

        var store = new ModuleRepositoryProfileStore(Scope);
        var profiles = ResolveProfiles();
        foreach (var profile in profiles)
        {
            if (store.GetProfile(profile.Name) is not null && !Force.IsPresent)
                throw new InvalidOperationException($"Managed module repository profile '{profile.Name}' already exists. Use Force to replace it.");

            if (!ShouldProcess(profile.Name, "Register managed module repository profile"))
                continue;

            var saved = store.SaveProfile(profile);
            if (PassThru)
                WriteObject(ModuleRepositoryProfileResultMapper.ToCmdletResult(saved, store.Path, store.Scope));
        }
    }

    private ModuleRepositoryProfile[] ResolveProfiles()
    {
        if (ParameterSetName == ParameterSetPSGallery)
            return new[] { ManagedModuleRepositoryProfileFactory.CreatePowerShellGalleryProfile(Trusted.IsPresent, Priority) };

        if (ParameterSetName == ParameterSetRepository)
        {
            return (Repository ?? Array.Empty<Hashtable>())
                .SelectMany(ManagedModuleRepositoryProfileFactory.CreateFromRepositoryHashtable)
                .ToArray();
        }

        return new[]
        {
            ManagedModuleRepositoryProfileFactory.CreateNuGetProfile(
                Name!,
                Uri!,
                Trusted.IsPresent,
                Priority,
                ApiVersion)
        };
    }
}
