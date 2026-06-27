using System;
using System.IO;
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
    private const string DefaultRepositorySource = "https://www.powershellgallery.com/api/v3/index.json";

    /// <summary>Module names to install.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true)]
    [Alias("ModuleName")]
    [ValidateNotNullOrEmpty]
    public string[] Name { get; set; } = Array.Empty<string>();

    /// <summary>Repository URL, NuGet v3 service index, flat-container URL, or local folder feed.</summary>
    [Parameter(Position = 1)]
    [Alias("Source", "RepositoryUri")]
    [ValidateNotNullOrEmpty]
    public string Repository { get; set; } = DefaultRepositorySource;

    /// <summary>Friendly repository name used in output.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string RepositoryName { get; set; } = "PSGallery";

    /// <summary>Exact package version to install. When omitted, the latest repository version is used.</summary>
    [Parameter]
    [Alias("RequiredVersion")]
    [ValidateNotNullOrEmpty]
    public string? Version { get; set; }

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

    /// <summary>Installs the requested modules.</summary>
    protected override void ProcessRecord()
    {
        var moduleRoot = ResolveModuleRoot();
        var repository = new ManagedModuleRepository(ResolveRepositoryName(), ResolveRepositorySource());
        var credential = ResolveCredential();
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
                        IncludePrerelease = Prerelease.IsPresent,
                        Scope = string.IsNullOrWhiteSpace(moduleRoot) ? Scope : ManagedModuleInstallScope.Custom,
                        ShellEdition = ShellEdition,
                        ModuleRoot = moduleRoot,
                        PackageCacheDirectory = ResolvePath(PackageCacheDirectory),
                        Credential = credential,
                        Force = Force.IsPresent
                    })
                .GetAwaiter()
                .GetResult();

            WriteObject(result);
        }
    }

    private string ResolveRepositoryName()
    {
        if (!string.Equals(RepositoryName, "PSGallery", StringComparison.OrdinalIgnoreCase))
            return RepositoryName;

        return ManagedModuleRepositoryKindForSource(Repository) == ManagedModuleRepositoryKind.LocalFolder
            ? "Local"
            : RepositoryName;
    }

    private string ResolveRepositorySource()
        => ManagedModuleRepositoryKindForSource(Repository) == ManagedModuleRepositoryKind.LocalFolder
            ? ResolvePath(Repository) ?? Repository
            : Repository;

    private string? ResolveModuleRoot()
    {
        if (string.IsNullOrWhiteSpace(ModuleRoot))
            return null;

        return SessionState.Path.GetUnresolvedProviderPathFromPSPath(ModuleRoot);
    }

    private string? ResolvePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var resolvedPath = path!;
        return ManagedModuleRepositoryKindForSource(resolvedPath) == ManagedModuleRepositoryKind.LocalFolder
            ? SessionState.Path.GetUnresolvedProviderPathFromPSPath(resolvedPath)
            : resolvedPath;
    }

    private RepositoryCredential? ResolveCredential()
    {
        var secret = CredentialSecret;
        if (string.IsNullOrWhiteSpace(secret) && !string.IsNullOrWhiteSpace(CredentialSecretFilePath))
        {
            var path = SessionState.Path.GetUnresolvedProviderPathFromPSPath(CredentialSecretFilePath);
            secret = File.ReadAllText(path).Trim();
        }

        return string.IsNullOrWhiteSpace(CredentialUserName) && string.IsNullOrWhiteSpace(secret)
            ? null
            : new RepositoryCredential
            {
                UserName = CredentialUserName,
                Secret = secret
            };
    }

    private static ManagedModuleRepositoryKind ManagedModuleRepositoryKindForSource(string source)
        => new ManagedModuleRepository("Repository", source).Kind;
}
