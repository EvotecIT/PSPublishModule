using System;
using System.IO;
using System.Linq;
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
[OutputType(typeof(ManagedModuleUpdateResult), typeof(ManagedModuleUpdatePlan))]
public sealed class UpdateManagedModuleCommand : PSCmdlet
{
    /// <summary>Module names to update.</summary>
    [Parameter(Position = 0, ValueFromPipelineByPropertyName = true)]
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

    /// <summary>Saved repository profile name.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? ProfileName { get; set; }

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

    /// <summary>Expected SHA256 hash of the requested module package before it is extracted and promoted.</summary>
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

    /// <summary>Reinstall the target version when it is already installed.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>Allow command exports to overlap with other modules in the target root.</summary>
    [Parameter]
    public SwitchParameter AllowClobber { get; set; }

    /// <summary>Accept package licenses when packages declare license acceptance is required.</summary>
    [Parameter]
    public SwitchParameter AcceptLicense { get; set; }

    /// <summary>Validate Authenticode signatures for signable package files before promotion.</summary>
    [Parameter]
    public SwitchParameter AuthenticodeCheck { get; set; }

    /// <summary>Skip installing dependencies declared by the package.</summary>
    [Parameter]
    [Alias("SkipDependenciesCheck")]
    public SwitchParameter SkipDependencyCheck { get; set; }

    /// <summary>Loaded module evidence used to block risky in-session updates.</summary>
    [Parameter]
    public ManagedModuleLoadedModule[] LoadedModule { get; set; } = Array.Empty<ManagedModuleLoadedModule>();

    /// <summary>Typed family policy used to keep related installed modules version-coherent.</summary>
    [Parameter]
    public ManagedModuleFamilyPolicy? FamilyPolicy { get; set; }

    /// <summary>Friendly family policy name used in plans and diagnostics.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? FamilyName { get; set; }

    /// <summary>Module name prefix used to discover installed family members.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? FamilyModuleNamePrefix { get; set; }

    /// <summary>Exact installed module names that belong to the family.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string[] FamilyModuleName { get; set; } = Array.Empty<string>();

    /// <summary>Typed source policy used to require managed receipt evidence from the requested repository.</summary>
    [Parameter]
    public ManagedModuleSourcePolicy? SourcePolicy { get; set; }

    /// <summary>Require installed source evidence to match the requested repository before reporting up to date.</summary>
    [Parameter]
    public SwitchParameter RequireSourceMatch { get; set; }

    /// <summary>Allow updating even when matching loaded module evidence is supplied.</summary>
    [Parameter]
    public SwitchParameter AllowLoadedModuleUpdate { get; set; }

    /// <summary>Return an inspectable update plan without writing files.</summary>
    [Parameter]
    public SwitchParameter Plan { get; set; }

    /// <summary>Write a compact Spectre.Console summary for each plan or result.</summary>
    [Parameter]
    public SwitchParameter ShowSummary { get; set; }

    /// <summary>Updates requested modules.</summary>
    protected override void ProcessRecord()
    {
        var moduleRoot = ManagedModuleCommandSupport.ResolveProviderPath(this, ModuleRoot);
        var repository = ManagedModuleCommandSupport.CreateRepository(
            this,
            RepositoryName,
            Repository,
            ProfileName,
            MyInvocation.BoundParameters.ContainsKey("Repository"));
        var credential = ManagedModuleCommandSupport.ResolveCredential(this, Credential, CredentialUserName, CredentialSecret, CredentialSecretFilePath);
        var trustPolicy = ManagedModuleCommandSupport.CreateTrustPolicy(TrustPolicy, RequireTrustedRepository.IsPresent, AllowedAuthor);
        var logger = new CmdletLogger(this, MyInvocation.BoundParameters.ContainsKey("Verbose"));
        var service = new ManagedModuleUpdateService(logger);
        var targetScope = string.IsNullOrWhiteSpace(moduleRoot) ? Scope : ManagedModuleInstallScope.Custom;
        var targetModuleRoot = ManagedModuleInstallRootResolver.Resolve(targetScope, ShellEdition, moduleRoot);
        var moduleNames = ResolveModuleNames(targetModuleRoot).ToArray();
        var updateAllInstalled = Name.Length == 0;
        if (moduleNames.Length == 0)
        {
            WriteVerbose($"No installed modules were found under '{targetModuleRoot}'.");
            return;
        }

        foreach (var moduleName in moduleNames)
        {
            try
            {
                var request = new ManagedModuleUpdateRequest
                {
                    Repository = repository,
                    Name = moduleName,
                    Version = Version,
                    MinimumVersion = MinimumVersion,
                    MaximumVersion = MaximumVersion,
                    VersionPolicy = VersionPolicy,
                    IncludePrerelease = Prerelease.IsPresent,
                    Scope = targetScope,
                    ShellEdition = ShellEdition,
                    ModuleRoot = moduleRoot,
                    PackageCacheDirectory = ManagedModuleCommandSupport.ResolveProviderPath(this, PackageCacheDirectory),
                    ExpectedPackageSha256 = ExpectedPackageSha256,
                    TrustPolicy = trustPolicy,
                    Credential = credential,
                    Force = Force.IsPresent,
                    AllowClobber = AllowClobber.IsPresent,
                    AcceptLicense = AcceptLicense.IsPresent,
                    AuthenticodeCheck = AuthenticodeCheck.IsPresent,
                    SkipDependencyCheck = SkipDependencyCheck.IsPresent,
                    LoadedModules = LoadedModule,
                    FamilyPolicy = ResolveFamilyPolicy(),
                    SourcePolicy = ResolveSourcePolicy(),
                    AllowLoadedModuleUpdate = AllowLoadedModuleUpdate.IsPresent
                };

                if (Plan.IsPresent)
                {
                    var plan = service.PlanUpdateAsync(request).GetAwaiter().GetResult();
                    WriteObject(plan);
                    if (ShowSummary.IsPresent)
                        ManagedModuleSummaryWriter.Write(plan);
                    continue;
                }

                if (!ShouldProcess(moduleName, $"Update managed module from repository '{repository.Name}'"))
                    continue;

                var result = service.UpdateAsync(request).GetAwaiter().GetResult();

                WriteObject(result);
                if (ShowSummary.IsPresent)
                    ManagedModuleSummaryWriter.Write(result);
            }
            catch (Exception ex) when (updateAllInstalled)
            {
                WriteError(new ErrorRecord(ex, "UpdateManagedModuleFailed", ErrorCategory.NotSpecified, moduleName));
            }
        }
    }

    private string[] ResolveModuleNames(string moduleRoot)
    {
        if (Name.Length > 0)
            return Name
                .Where(static name => !string.IsNullOrWhiteSpace(name))
                .Select(static name => name.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (!Directory.Exists(moduleRoot))
            return Array.Empty<string>();

        return Directory.EnumerateDirectories(moduleRoot)
            .Select(Path.GetFileName)
            .Where(static name => !string.IsNullOrWhiteSpace(name) &&
                                  !name!.StartsWith(".", StringComparison.Ordinal))
            .Select(static name => name!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private ManagedModuleFamilyPolicy? ResolveFamilyPolicy()
    {
        if (FamilyPolicy is not null)
            return FamilyPolicy;

        if (string.IsNullOrWhiteSpace(FamilyName) &&
            string.IsNullOrWhiteSpace(FamilyModuleNamePrefix) &&
            !FamilyModuleName.Any(static name => !string.IsNullOrWhiteSpace(name)))
            return null;

        return new ManagedModuleFamilyPolicy
        {
            Name = FamilyName ?? string.Empty,
            ModuleNamePrefix = FamilyModuleNamePrefix,
            ModuleNames = FamilyModuleName
        };
    }

    private ManagedModuleSourcePolicy? ResolveSourcePolicy()
    {
        if (SourcePolicy is not null)
            return SourcePolicy;

        return RequireSourceMatch.IsPresent ? new ManagedModuleSourcePolicy() : null;
    }
}
