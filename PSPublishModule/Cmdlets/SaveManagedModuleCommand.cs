using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Saves modules from a managed repository to an explicit module root.
/// </summary>
/// <remarks>
/// <para>
/// This command uses the same managed C# repository and archive engine as <c>Install-ManagedModule</c>, but requires
/// an explicit destination root instead of installing into the default PowerShell module paths.
/// </para>
/// </remarks>
/// <example>
/// <summary>Save the latest stable module from the default public gallery endpoint</summary>
/// <code>Save-ManagedModule -Name Company.Tools -Path C:\Modules</code>
/// </example>
/// <example>
/// <summary>Save an exact version from a local feed</summary>
/// <code>Save-ManagedModule -Name Company.Tools -RequiredVersion 1.2.0 -Repository C:\Packages -Path C:\Modules</code>
/// </example>
[Cmdlet(VerbsData.Save, "ManagedModule", SupportsShouldProcess = true)]
[OutputType(typeof(ManagedModuleInstallResult))]
public sealed class SaveManagedModuleCommand : PSCmdlet
{
    /// <summary>Module names to save.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true)]
    [Alias("ModuleName")]
    [ValidateNotNullOrEmpty]
    public string[] Name { get; set; } = Array.Empty<string>();

    /// <summary>Destination module root.</summary>
    [Parameter(Mandatory = true, Position = 1)]
    [Alias("DestinationPath", "ModuleRoot")]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <summary>Repository URL, NuGet v3 service index, flat-container URL, or local folder feed.</summary>
    [Parameter]
    [Alias("Source", "RepositoryUri")]
    [ValidateNotNullOrEmpty]
    public string Repository { get; set; } = ManagedModuleCommandSupport.DefaultRepositorySource;

    /// <summary>Friendly repository name used in output.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string RepositoryName { get; set; } = ManagedModuleCommandSupport.DefaultRepositoryName;

    /// <summary>Exact package version to save. When omitted, the latest repository version is used.</summary>
    [Parameter]
    [Alias("RequiredVersion")]
    [ValidateNotNullOrEmpty]
    public string? Version { get; set; }

    /// <summary>Minimum package version to save when Version is omitted.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? MinimumVersion { get; set; }

    /// <summary>Maximum package version to save when Version is omitted.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? MaximumVersion { get; set; }

    /// <summary>Include prerelease versions when resolving the latest version.</summary>
    [Parameter]
    [Alias("AllowPrerelease")]
    public SwitchParameter Prerelease { get; set; }

    /// <summary>Optional package cache directory.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? PackageCacheDirectory { get; set; }

    /// <summary>Optional repository credential.</summary>
    [Parameter]
    public PSCredential? Credential { get; set; }

    /// <summary>Optional repository credential username.</summary>
    [Parameter]
    [Alias("UserName")]
    public string? CredentialUserName { get; set; }

    /// <summary>Optional repository credential secret.</summary>
    [Parameter]
    [Alias("Password", "Token")]
    public string? CredentialSecret { get; set; }

    /// <summary>Optional path to a file containing the repository credential secret.</summary>
    [Parameter]
    [Alias("CredentialPath", "TokenPath")]
    public string? CredentialSecretFilePath { get; set; }

    /// <summary>Overwrite an existing saved version.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>Skip installing dependencies declared by the package.</summary>
    [Parameter]
    [Alias("SkipDependenciesCheck")]
    public SwitchParameter SkipDependencyCheck { get; set; }

    /// <summary>Saves requested modules.</summary>
    protected override void ProcessRecord()
    {
        var moduleRoot = ManagedModuleCommandSupport.ResolveProviderPath(this, Path)!;
        var repository = ManagedModuleCommandSupport.CreateRepository(this, RepositoryName, Repository);
        var credential = ManagedModuleCommandSupport.ResolveCredential(this, Credential, CredentialUserName, CredentialSecret, CredentialSecretFilePath);
        var logger = new CmdletLogger(this, MyInvocation.BoundParameters.ContainsKey("Verbose"));
        var service = new ManagedModuleInstallService(logger);

        foreach (var moduleName in Name)
        {
            if (!ShouldProcess(moduleName, $"Save managed module to '{moduleRoot}'"))
                continue;

            var result = service.InstallAsync(
                    new ManagedModuleInstallRequest
                    {
                        Repository = repository,
                        Name = moduleName,
                        Version = Version,
                        MinimumVersion = MinimumVersion,
                        MaximumVersion = MaximumVersion,
                        IncludePrerelease = Prerelease.IsPresent,
                        Scope = ManagedModuleInstallScope.Custom,
                        ModuleRoot = moduleRoot,
                        PackageCacheDirectory = ManagedModuleCommandSupport.ResolveProviderPath(this, PackageCacheDirectory),
                        Credential = credential,
                        Force = Force.IsPresent,
                        SkipDependencyCheck = SkipDependencyCheck.IsPresent
                    })
                .GetAwaiter()
                .GetResult();

            WriteObject(result);
        }
    }
}
