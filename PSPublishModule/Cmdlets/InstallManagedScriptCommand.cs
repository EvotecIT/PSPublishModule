using System;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Installs script resources through the managed C# resource engine.
/// </summary>
/// <example>
/// <summary>Install the latest stable script from the default public gallery endpoint.</summary>
/// <code>Install-ManagedScript -Name Invoke-CompanyTask -Scope CurrentUser</code>
/// </example>
[Cmdlet(VerbsLifecycle.Install, "ManagedScript", SupportsShouldProcess = true)]
[OutputType(typeof(ManagedScriptInstallResult), typeof(ManagedScriptInstallPlan))]
public sealed class InstallManagedScriptCommand : AsyncPSCmdlet
{
    /// <summary>Script resource names to install.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true)]
    [Alias("ScriptName")]
    [ValidateNotNullOrEmpty]
    public string[] Name { get; set; } = Array.Empty<string>();

    /// <summary>Repository URL, NuGet v3 service index, flat-container URL, or local folder feed.</summary>
    [Parameter(Position = 1)]
    [Alias("Source", "RepositoryUri")]
    [ValidateNotNullOrEmpty]
    public string Repository { get; set; } = ManagedModuleCommandSupport.DefaultScriptRepositorySource;

    /// <summary>Friendly repository name used in output.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string RepositoryName { get; set; } = ManagedModuleCommandSupport.DefaultRepositoryName;

    /// <summary>Saved repository profile name.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? ProfileName { get; set; }

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

    /// <summary>Install scope used when ScriptRoot is not supplied.</summary>
    [Parameter]
    public ManagedScriptInstallScope Scope { get; set; } = ManagedScriptInstallScope.CurrentUser;

    /// <summary>PowerShell path family used when resolving default CurrentUser or AllUsers script roots.</summary>
    [Parameter]
    public ManagedModuleShellEdition ShellEdition { get; set; } = ManagedModuleShellEdition.Auto;

    /// <summary>Explicit script root. When supplied, Scope is treated as Custom.</summary>
    [Parameter]
    [Alias("Path")]
    [ValidateNotNullOrEmpty]
    public string? ScriptRoot { get; set; }

    /// <summary>Optional package cache directory.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? PackageCacheDirectory { get; set; }

    /// <summary>Expected SHA256 hash of the script package before it is extracted and installed.</summary>
    [Parameter]
    [Alias("PackageSha256", "Sha256")]
    [ValidateNotNullOrEmpty]
    public string? ExpectedPackageSha256 { get; set; }

    /// <summary>Optional typed repository/package trust policy.</summary>
    [Parameter]
    public ManagedModuleTrustPolicy? TrustPolicy { get; set; }

    /// <summary>Require the selected repository profile to be marked trusted.</summary>
    [Parameter]
    public SwitchParameter RequireTrustedRepository { get; set; }

    /// <summary>Allowed package author values from package metadata.</summary>
    [Parameter]
    [Alias("RequiredAuthor", "TrustedAuthor")]
    [ValidateNotNullOrEmpty]
    public string[] AllowedAuthor { get; set; } = Array.Empty<string>();

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

    /// <summary>Optional HTTP proxy used for repository requests.</summary>
    [Parameter]
    [ValidateNotNull]
    public Uri? Proxy { get; set; }

    /// <summary>Optional proxy credential used with Proxy.</summary>
    [Parameter]
    public PSCredential? ProxyCredential { get; set; }

    /// <summary>Reinstall the script version when it already exists.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>Accept package licenses when packages declare license acceptance is required.</summary>
    [Parameter]
    public SwitchParameter AcceptLicense { get; set; }

    /// <summary>Return an inspectable install plan without writing files.</summary>
    [Parameter]
    public SwitchParameter Plan { get; set; }

    /// <summary>Do not add the resolved script root to the current process PATH after installation.</summary>
    [Parameter]
    public SwitchParameter NoPathUpdate { get; set; }

    /// <summary>Installs requested scripts.</summary>
    protected override async Task ProcessRecordAsync()
    {
        var scriptRoot = ManagedModuleCommandSupport.ResolveProviderPath(this, ScriptRoot);
        var scriptRootWasBound = MyInvocation.BoundParameters.ContainsKey(nameof(ScriptRoot));
        var packageCacheDirectory = ManagedModuleCommandSupport.ResolveProviderPath(this, PackageCacheDirectory);
        var repository = ManagedModuleCommandSupport.CreateScriptRepository(
            this,
            RepositoryName,
            Repository,
            ProfileName,
            MyInvocation.BoundParameters.ContainsKey("Repository"));
        var credential = ManagedModuleCommandSupport.ResolveCredential(this, Credential, CredentialUserName, CredentialSecret, CredentialSecretFilePath);
        var trustPolicy = ManagedModuleCommandSupport.CreateTrustPolicy(TrustPolicy, RequireTrustedRepository.IsPresent, AllowedAuthor);
        var logger = new CmdletLogger(this, MyInvocation.BoundParameters.ContainsKey("Verbose"));
        var repositoryClient = ManagedModuleCommandSupport.CreateRepositoryClient(this, logger, Proxy, ProxyCredential);
        var service = new ManagedScriptResourceService(logger, repositoryClient);
        ManagedModuleCommandSupport.ValidateSinglePackageHashTarget(ExpectedPackageSha256, Name);

        foreach (var scriptName in Name)
        {
            var request = new ManagedScriptInstallRequest
            {
                Repository = repository,
                Name = scriptName,
                Scope = string.IsNullOrWhiteSpace(scriptRoot) ? Scope : ManagedScriptInstallScope.Custom,
                ShellEdition = ShellEdition,
                ScriptRoot = scriptRoot,
                Version = Version,
                MinimumVersion = MinimumVersion,
                MaximumVersion = MaximumVersion,
                VersionPolicy = VersionPolicy,
                IncludePrerelease = Prerelease.IsPresent,
                PackageCacheDirectory = packageCacheDirectory,
                ExpectedPackageSha256 = ExpectedPackageSha256,
                TrustPolicy = trustPolicy,
                Credential = credential,
                Force = Force.IsPresent,
                AcceptLicense = AcceptLicense.IsPresent
            };

            if (Plan.IsPresent)
            {
                WriteObject(await service.PlanInstallAsync(request, CancelToken).ConfigureAwait(false));
                continue;
            }

            if (!ShouldProcess(scriptName, $"Install managed script from repository '{repository.Name}'"))
                continue;

            var result = await service.InstallAsync(request, CancelToken).ConfigureAwait(false);
            if (ShouldUpdateProcessPath(scriptRootWasBound, NoPathUpdate.IsPresent, result))
                EnsureProcessPathContains(result.ScriptRoot);
            WriteObject(result);
        }
    }

    internal static bool ShouldUpdateProcessPath(bool scriptRootWasBound, bool noPathUpdate, ManagedScriptInstallResult result)
        => !scriptRootWasBound &&
           !noPathUpdate &&
           result is not null &&
           !string.IsNullOrWhiteSpace(result.ScriptRoot);

    internal static bool EnsureProcessPathContains(string scriptRoot)
    {
        if (string.IsNullOrWhiteSpace(scriptRoot))
            return false;

        var resolved = Path.GetFullPath(scriptRoot.Trim().Trim('"'))
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var parts = current.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static part => part.Trim().Trim('"'))
            .Where(static part => !string.IsNullOrWhiteSpace(part))
            .Select(part =>
            {
                try
                {
                    return Path.GetFullPath(part).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
                catch
                {
                    return part.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
            });

        if (parts.Any(part => string.Equals(part, resolved, PathComparison)))
            return false;

        var updated = string.IsNullOrWhiteSpace(current)
            ? resolved
            : current.TrimEnd(Path.PathSeparator) + Path.PathSeparator + resolved;
        Environment.SetEnvironmentVariable("PATH", updated);
        return true;
    }

    internal static StringComparison PathComparison
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}
