using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Installs PowerShell modules through the managed C# module engine.
/// </summary>
/// <remarks>
/// <para>
/// This command is the first managed install surface. It uses PowerForge repository lookup, package download, and
/// safe archive extraction directly instead of invoking PowerShellGet or PSResourceGet.
/// </para>
/// </remarks>
/// <example>
/// <summary>Install the latest stable module from the default public gallery endpoint</summary>
/// <code>Install-ManagedModule -Name Company.Tools</code>
/// </example>
/// <example>
/// <summary>Install an exact module version from a local feed into an explicit root</summary>
/// <code>Install-ManagedModule -Name Company.Tools -Version 1.2.0 -Repository C:\Packages -Scope Custom -ModuleRoot C:\Modules</code>
/// </example>
[Cmdlet(VerbsLifecycle.Install, "ManagedModule", SupportsShouldProcess = true)]
[OutputType(typeof(ManagedModuleInstallResult))]
public sealed class InstallManagedModuleCommand : PSCmdlet
{
    /// <summary>Module names to install.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true)]
    [Alias("ModuleName")]
    [ValidateNotNullOrEmpty]
    public string[] Name { get; set; } = Array.Empty<string>();

    /// <summary>Repository URL, NuGet v3 service index, flat-container URL, or local folder feed.</summary>
    [Parameter(Position = 1)]
    [Alias("Source", "RepositoryUri")]
    [ValidateNotNullOrEmpty]
    public string Repository { get; set; } = ManagedModuleCommandSupport.DefaultRepositorySource;

    /// <summary>Friendly repository name used in output.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string RepositoryName { get; set; } = ManagedModuleCommandSupport.DefaultRepositoryName;

    /// <summary>Exact package version to install. When omitted, the latest repository version is used.</summary>
    [Parameter]
    [Alias("RequiredVersion")]
    [ValidateNotNullOrEmpty]
    public string? Version { get; set; }

    /// <summary>Minimum package version to install when Version is omitted.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? MinimumVersion { get; set; }

    /// <summary>Maximum package version to install when Version is omitted.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? MaximumVersion { get; set; }

    /// <summary>NuGet-style version range policy used when Version is omitted.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? VersionPolicy { get; set; }

    /// <summary>Include prerelease versions when resolving the latest version.</summary>
    [Parameter]
    [Alias("AllowPrerelease")]
    public SwitchParameter Prerelease { get; set; }

    /// <summary>Install scope used when ModuleRoot is not supplied.</summary>
    [Parameter]
    public ManagedModuleInstallScope Scope { get; set; } = ManagedModuleInstallScope.CurrentUser;

    /// <summary>PowerShell path family used when resolving default CurrentUser or AllUsers module roots.</summary>
    [Parameter]
    public ManagedModuleShellEdition ShellEdition { get; set; } = ManagedModuleShellEdition.Auto;

    /// <summary>Explicit module root. Use with Scope Custom.</summary>
    [Parameter]
    [Alias("Path")]
    [ValidateNotNullOrEmpty]
    public string? ModuleRoot { get; set; }

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

    /// <summary>Reinstall the module version when it already exists.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>Skip installing dependencies declared by the package.</summary>
    [Parameter]
    [Alias("SkipDependenciesCheck")]
    public SwitchParameter SkipDependencyCheck { get; set; }

    /// <summary>Installs the requested modules.</summary>
    protected override void ProcessRecord()
    {
        var moduleRoot = ManagedModuleCommandSupport.ResolveProviderPath(this, ModuleRoot);
        var repository = ManagedModuleCommandSupport.CreateRepository(this, RepositoryName, Repository);
        var credential = ManagedModuleCommandSupport.ResolveCredential(this, Credential, CredentialUserName, CredentialSecret, CredentialSecretFilePath);
        var logger = new CmdletLogger(this, MyInvocation.BoundParameters.ContainsKey("Verbose"));
        var service = new ManagedModuleInstallService(logger);

        foreach (var moduleName in Name)
        {
            if (!ShouldProcess(moduleName, $"Install managed module from repository '{repository.Name}'"))
                continue;

            var result = service.InstallAsync(
                    new ManagedModuleInstallRequest
                    {
                        Repository = repository,
                        Name = moduleName,
                        Version = Version,
                        MinimumVersion = MinimumVersion,
                        MaximumVersion = MaximumVersion,
                        VersionPolicy = VersionPolicy,
                        IncludePrerelease = Prerelease.IsPresent,
                        Scope = string.IsNullOrWhiteSpace(moduleRoot) ? Scope : ManagedModuleInstallScope.Custom,
                        ShellEdition = ShellEdition,
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
