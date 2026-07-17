using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Threading.Tasks;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Saves modules from a managed repository to an explicit module root.
/// </summary>
/// <remarks>
/// <para>
/// This command provides the module-save functionality of <c>Save-PSResource</c> through the same managed C#
/// repository and archive engine as <c>Install-ManagedModule</c>. It accepts names or typed
/// <c>Find-ManagedModule</c> output, saves unpacked modules or .nupkg files, and defaults to the current filesystem
/// location when Path is omitted.
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
/// <example>
/// <summary>Save typed find output with PowerShellGet-compatible XML metadata</summary>
/// <code>Find-ManagedModule -Name Company.Tools -Version 1.2.0 -ProfileName CompanyModules | Save-ManagedModule -Path C:\OfflineModules -IncludeXml</code>
/// </example>
/// <example>
/// <summary>Save a package file in the current directory</summary>
/// <code>Save-ManagedModule -Name Company.Tools -Version 1.2.0 -AsNupkg</code>
/// </example>
[Cmdlet(VerbsData.Save, "ManagedModule", SupportsShouldProcess = true, DefaultParameterSetName = NameParameterSet)]
[OutputType(typeof(ManagedModuleInstallResult), typeof(ManagedModuleInstallPlan))]
public sealed class SaveManagedModuleCommand : AsyncPSCmdlet
{
    private const string NameParameterSet = "NameParameterSet";
    private const string InputObjectParameterSet = "InputObjectParameterSet";
    private readonly List<ManagedModuleInstallResult> _results = new();

    /// <summary>Module names to save.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true, ParameterSetName = NameParameterSet)]
    [Alias("ModuleName")]
    [ValidateNotNullOrEmpty]
    public string[] Name { get; set; } = Array.Empty<string>();

    /// <summary>Module versions returned by Find-ManagedModule to save.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true, ParameterSetName = InputObjectParameterSet)]
    [Alias("ParentResource")]
    [ValidateNotNullOrEmpty]
    public ManagedModuleVersionInfo[] InputObject { get; set; } = Array.Empty<ManagedModuleVersionInfo>();

    /// <summary>Destination module root. Defaults to the current filesystem location.</summary>
    [Parameter(Position = 1)]
    [Alias("DestinationPath", "ModuleRoot")]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = ".";

    /// <summary>Repository URL, NuGet v3 service index, flat-container URL, or local folder feed.</summary>
    [Parameter(ParameterSetName = NameParameterSet)]
    [Parameter(ParameterSetName = InputObjectParameterSet)]
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

    /// <summary>Exact package version to save. When omitted, the latest repository version is used.</summary>
    [Parameter(ParameterSetName = NameParameterSet)]
    [Alias("RequiredVersion")]
    [ValidateNotNullOrEmpty]
    public string? Version { get; set; }

    /// <summary>Minimum package version to save when Version is omitted.</summary>
    [Parameter(ParameterSetName = NameParameterSet)]
    [ValidateNotNullOrEmpty]
    public string? MinimumVersion { get; set; }

    /// <summary>Maximum package version to save when Version is omitted.</summary>
    [Parameter(ParameterSetName = NameParameterSet)]
    [ValidateNotNullOrEmpty]
    public string? MaximumVersion { get; set; }

    /// <summary>NuGet-style version range policy used when Version is omitted.</summary>
    [Parameter(ParameterSetName = NameParameterSet)]
    [ValidateNotNullOrEmpty]
    public string? VersionPolicy { get; set; }

    /// <summary>Include prerelease versions when resolving the latest version.</summary>
    [Parameter(ParameterSetName = NameParameterSet)]
    [Alias("AllowPrerelease")]
    public SwitchParameter Prerelease { get; set; }

    /// <summary>Optional package cache directory.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? PackageCacheDirectory { get; set; }

    /// <summary>Save the selected packages as .nupkg files instead of unpacked module folders.</summary>
    [Parameter]
    public SwitchParameter AsNupkg { get; set; }

    /// <summary>Write PowerShellGet-compatible PSGetModuleInfo.xml metadata beside each saved unpacked module.</summary>
    [Parameter]
    public SwitchParameter IncludeXml { get; set; }

    /// <summary>Maximum number of dependency branches to save concurrently. Omit to use the managed engine default.</summary>
    [Parameter]
    [ValidateRange(1, 256)]
    public int DependencyConcurrency { get; set; }

    /// <summary>Expected SHA256 hash of the root package before it is extracted and saved.</summary>
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

    /// <summary>Overwrite an existing saved version.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>Allow command exports to overlap with other modules in the destination root.</summary>
    [Parameter]
    public SwitchParameter AllowClobber { get; set; }

    /// <summary>Accept package licenses when packages declare license acceptance is required.</summary>
    [Parameter]
    public SwitchParameter AcceptLicense { get; set; }

    /// <summary>Validate Authenticode signatures for signable package files before saving.</summary>
    [Parameter]
    public SwitchParameter AuthenticodeCheck { get; set; }

    /// <summary>Skip installing dependencies declared by the package.</summary>
    [Parameter]
    [Alias("SkipDependenciesCheck")]
    public SwitchParameter SkipDependencyCheck { get; set; }

    /// <summary>Return an inspectable save plan without writing files.</summary>
    [Parameter]
    public SwitchParameter Plan { get; set; }

    /// <summary>Optional path for offline bundle metadata written after successful saves.</summary>
    [Parameter]
    [Alias("MetadataPath", "OfflineBundleMetadataPath")]
    public string? BundleMetadataPath { get; set; }

    /// <summary>Write a compact Spectre.Console summary for each plan or result.</summary>
    [Parameter]
    public SwitchParameter ShowSummary { get; set; }

    /// <summary>Suppress optional host summaries and progress-style output without changing pipeline result objects.</summary>
    [Parameter]
    public SwitchParameter Quiet { get; set; }

    /// <summary>Saves requested modules.</summary>
    protected override async Task ProcessRecordAsync()
    {
        if (AsNupkg.IsPresent && IncludeXml.IsPresent)
            throw new InvalidOperationException("AsNupkg and IncludeXml cannot be used together.");

        var moduleRoot = ManagedModuleCommandSupport.ResolveProviderPath(this, Path)!;
        var packageCacheDirectory = ManagedModuleCommandSupport.ResolveProviderPath(this, PackageCacheDirectory);
        var defaultRepository = ManagedModuleCommandSupport.CreateRepository(
            this,
            RepositoryName,
            Repository,
            ProfileName,
            MyInvocation.BoundParameters.ContainsKey("Repository"));
        var credential = ManagedModuleCommandSupport.ResolveCredential(this, Credential, CredentialUserName, CredentialSecret, CredentialSecretFilePath);
        var trustPolicy = ManagedModuleCommandSupport.CreateTrustPolicy(TrustPolicy, RequireTrustedRepository.IsPresent, AllowedAuthor);
        var logger = new CmdletLogger(this, MyInvocation.BoundParameters.ContainsKey("Verbose"));
        var repositoryClient = ManagedModuleCommandSupport.CreateRepositoryClient(this, logger, Proxy, ProxyCredential);
        var service = new ManagedModuleInstallService(logger, repositoryClient);
        var targets = ResolveTargets().ToArray();
        ManagedModuleCommandSupport.ValidateSinglePackageHashTarget(ExpectedPackageSha256, targets.Select(static target => target.Name).ToArray());
        var writeSummary = ManagedModuleCommandSupport.ShouldWriteSummary(ShowSummary.IsPresent, Quiet.IsPresent);

        foreach (var target in targets)
        {
            var repository = string.IsNullOrWhiteSpace(target.Repository)
                ? defaultRepository
                : ManagedModuleCommandSupport.CreateRepository(this, target.Repository!, target.Repository!);
            var request = new ManagedModuleInstallRequest
            {
                Repository = repository,
                Name = target.Name,
                Version = target.Version,
                MinimumVersion = target.MinimumVersion,
                MaximumVersion = target.MaximumVersion,
                VersionPolicy = target.VersionPolicy,
                IncludePrerelease = target.IncludePrerelease,
                Scope = ManagedModuleInstallScope.Custom,
                ModuleRoot = moduleRoot,
                PackageCacheDirectory = packageCacheDirectory,
                SaveAsNupkg = AsNupkg.IsPresent,
                DependencyConcurrency = DependencyConcurrency,
                ExpectedPackageSha256 = ExpectedPackageSha256,
                TrustPolicy = trustPolicy,
                Credential = credential,
                Force = Force.IsPresent,
                AllowClobber = AllowClobber.IsPresent,
                AcceptLicense = AcceptLicense.IsPresent,
                AuthenticodeCheck = AuthenticodeCheck.IsPresent,
                SkipDependencyCheck = SkipDependencyCheck.IsPresent
            };

            if (Plan.IsPresent)
            {
                var plan = await service.PlanInstallAsync(request, CancelToken).ConfigureAwait(false);
                WriteObject(plan);
                if (writeSummary)
                    ManagedModuleSummaryWriter.Write(plan);
                continue;
            }

            if (!ShouldProcess(target.Name, $"Save managed module to '{moduleRoot}'"))
                continue;

            var result = await service.InstallAsync(request, CancelToken).ConfigureAwait(false);
            if (IncludeXml.IsPresent)
                new PowerShellGetModuleInfoWriter().Write(result);
            _results.Add(result);

            WriteObject(result);
            if (writeSummary)
                ManagedModuleSummaryWriter.Write(result);
        }
    }

    private IEnumerable<ManagedModuleRequiredResourceTarget> ResolveTargets()
    {
        if (ParameterSetName == InputObjectParameterSet)
        {
            var repositoryWasBound = MyInvocation.BoundParameters.ContainsKey(nameof(Repository));
            var profileWasBound = MyInvocation.BoundParameters.ContainsKey(nameof(ProfileName));
            return InputObject.Select(resource => new ManagedModuleRequiredResourceTarget(
                resource.Name,
                resource.Version,
                minimumVersion: null,
                maximumVersion: null,
                versionPolicy: null,
                resource.IsPrerelease,
                ManagedModuleInstallScope.Custom,
                scopeSpecified: true,
                repositoryWasBound || profileWasBound
                    ? null
                    : FirstNonEmpty(resource.RepositorySource, resource.RepositoryName),
                Force.IsPresent,
                AllowClobber.IsPresent,
                AcceptLicense.IsPresent,
                SkipDependencyCheck.IsPresent));
        }

        return Name.Select(moduleName => new ManagedModuleRequiredResourceTarget(
            moduleName,
            Version,
            MinimumVersion,
            MaximumVersion,
            VersionPolicy,
            Prerelease.IsPresent,
            ManagedModuleInstallScope.Custom,
            scopeSpecified: true,
            repository: null,
            Force.IsPresent,
            AllowClobber.IsPresent,
            AcceptLicense.IsPresent,
            SkipDependencyCheck.IsPresent));
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))?.Trim();

    /// <summary>Writes optional offline bundle metadata after all save results are available.</summary>
    protected override Task EndProcessingAsync()
    {
        if (Plan.IsPresent || string.IsNullOrWhiteSpace(BundleMetadataPath) || _results.Count == 0)
            return Task.CompletedTask;

        var metadataPath = ManagedModuleCommandSupport.ResolveProviderPath(this, BundleMetadataPath);
        if (!ShouldProcess(metadataPath, "Write managed module bundle metadata"))
            return Task.CompletedTask;

        new ManagedModuleBundleMetadataWriter().Write(metadataPath!, _results);
        return Task.CompletedTask;
    }
}
