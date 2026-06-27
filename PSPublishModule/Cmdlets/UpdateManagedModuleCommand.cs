using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Updates installed PowerShell modules through the managed C# module engine.
/// </summary>
/// <remarks>
/// <para>
/// This command inspects the selected module root and updates only when the repository contains a newer selected
/// version, or installs the target when the selected scope has no copy.
/// </para>
/// </remarks>
/// <example>
/// <summary>Update a module in the current user's module path</summary>
/// <code>Update-ManagedModule -Name Company.Tools</code>
/// </example>
/// <example>
/// <summary>Update a module in an explicit module root from a local feed</summary>
/// <code>Update-ManagedModule -Name Company.Tools -Repository C:\Packages -Path C:\Modules</code>
/// </example>
[Cmdlet(VerbsData.Update, "ManagedModule", SupportsShouldProcess = true)]
[Alias("Update-PublicModule")]
[OutputType(typeof(ManagedModuleUpdateResult))]
public sealed class UpdateManagedModuleCommand : PSCmdlet
{
    /// <summary>Module names to update.</summary>
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

    /// <summary>Exact target version. When omitted, the latest repository version is used.</summary>
    [Parameter]
    [Alias("RequiredVersion")]
    [ValidateNotNullOrEmpty]
    public string? Version { get; set; }

    /// <summary>Minimum target version when Version is omitted.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? MinimumVersion { get; set; }

    /// <summary>Maximum target version when Version is omitted.</summary>
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

    /// <summary>Scope to inspect and update when ModuleRoot is not supplied.</summary>
    [Parameter]
    public ManagedModuleInstallScope Scope { get; set; } = ManagedModuleInstallScope.CurrentUser;

    /// <summary>PowerShell path family used when resolving default CurrentUser or AllUsers module roots.</summary>
    [Parameter]
    public ManagedModuleShellEdition ShellEdition { get; set; } = ManagedModuleShellEdition.Auto;

    /// <summary>Explicit module root. When supplied, the command updates that root.</summary>
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

    /// <summary>Reinstall the target version when it is already installed.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>Skip installing dependencies declared by the package.</summary>
    [Parameter]
    [Alias("SkipDependenciesCheck")]
    public SwitchParameter SkipDependencyCheck { get; set; }

    /// <summary>Updates requested modules.</summary>
    protected override void ProcessRecord()
    {
        var moduleRoot = ManagedModuleCommandSupport.ResolveProviderPath(this, ModuleRoot);
        var repository = ManagedModuleCommandSupport.CreateRepository(this, RepositoryName, Repository);
        var credential = ManagedModuleCommandSupport.ResolveCredential(this, Credential, CredentialUserName, CredentialSecret, CredentialSecretFilePath);
        var logger = new CmdletLogger(this, MyInvocation.BoundParameters.ContainsKey("Verbose"));
        var service = new ManagedModuleUpdateService(logger);

        foreach (var moduleName in Name)
        {
            if (!ShouldProcess(moduleName, $"Update managed module from repository '{repository.Name}'"))
                continue;

            var result = service.UpdateAsync(
                    new ManagedModuleUpdateRequest
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
