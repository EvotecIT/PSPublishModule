using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Threading.Tasks;
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
[Cmdlet(VerbsLifecycle.Install, "ManagedModule", SupportsShouldProcess = true, DefaultParameterSetName = NameParameterSet)]
[OutputType(typeof(ManagedModuleInstallResult), typeof(ManagedModuleInstallPlan))]
public sealed class InstallManagedModuleCommand : AsyncPSCmdlet
{
    private const string NameParameterSet = "NameParameterSet";
    private const string RequiredResourceParameterSet = "RequiredResourceParameterSet";
    private const string RequiredResourceFileParameterSet = "RequiredResourceFileParameterSet";

    /// <summary>Module names to install.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true, ParameterSetName = NameParameterSet)]
    [Alias("ModuleName")]
    [ValidateNotNullOrEmpty]
    public string[] Name { get; set; } = Array.Empty<string>();

    /// <summary>PSResourceGet-style required resource map to install.</summary>
    [Parameter(Mandatory = true, ParameterSetName = RequiredResourceParameterSet)]
    [ValidateNotNull]
    public object? RequiredResource { get; set; }

    /// <summary>Path to a PowerShell data file containing a PSResourceGet-style required resource map.</summary>
    [Parameter(Mandatory = true, ParameterSetName = RequiredResourceFileParameterSet)]
    [ValidateNotNullOrEmpty]
    public string? RequiredResourceFile { get; set; }

    /// <summary>Repository URL, NuGet v3 service index, flat-container URL, or local folder feed.</summary>
    [Parameter(Position = 1, ParameterSetName = NameParameterSet)]
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

    /// <summary>Exact package version to install. When omitted, the latest repository version is used.</summary>
    [Parameter(ParameterSetName = NameParameterSet)]
    [Alias("RequiredVersion")]
    [ValidateNotNullOrEmpty]
    public string? Version { get; set; }

    /// <summary>Minimum package version to install when Version is omitted.</summary>
    [Parameter(ParameterSetName = NameParameterSet)]
    [ValidateNotNullOrEmpty]
    public string? MinimumVersion { get; set; }

    /// <summary>Maximum package version to install when Version is omitted.</summary>
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

    /// <summary>Maximum number of dependency branches to install concurrently. Omit to use the managed engine default.</summary>
    [Parameter]
    [ValidateRange(1, 256)]
    public int DependencyConcurrency { get; set; }

    /// <summary>Expected SHA256 hash of the root package before it is extracted and promoted.</summary>
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

    /// <summary>Reinstall the module version when it already exists.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>PSResourceGet-compatible spelling for reinstalling the selected module version when it already exists.</summary>
    [Parameter]
    public SwitchParameter Reinstall { get; set; }

    /// <summary>Allow command exports to overlap with other modules in the target root.</summary>
    [Parameter]
    public SwitchParameter AllowClobber { get; set; }

    /// <summary>PSResourceGet-compatible spelling for the managed default that rejects command export conflicts.</summary>
    [Parameter]
    public SwitchParameter NoClobber { get; set; }

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

    /// <summary>Return an inspectable install plan without writing files.</summary>
    [Parameter]
    public SwitchParameter Plan { get; set; }

    /// <summary>Write a compact Spectre.Console summary for each plan or result.</summary>
    [Parameter]
    public SwitchParameter ShowSummary { get; set; }

    /// <summary>Suppress optional host summaries and progress-style output without changing pipeline result objects.</summary>
    [Parameter]
    public SwitchParameter Quiet { get; set; }

    /// <summary>Installs the requested modules.</summary>
    protected override async Task ProcessRecordAsync()
    {
        var moduleRoot = ManagedModuleCommandSupport.ResolveProviderPath(this, ModuleRoot);
        var packageCacheDirectory = ManagedModuleCommandSupport.ResolveProviderPath(this, PackageCacheDirectory);
        var credential = ManagedModuleCommandSupport.ResolveCredential(this, Credential, CredentialUserName, CredentialSecret, CredentialSecretFilePath);
        var trustPolicy = ManagedModuleCommandSupport.CreateTrustPolicy(TrustPolicy, RequireTrustedRepository.IsPresent, AllowedAuthor);
        var logger = new CmdletLogger(this, MyInvocation.BoundParameters.ContainsKey("Verbose"));
        var repositoryClient = ManagedModuleCommandSupport.CreateRepositoryClient(this, logger, Proxy, ProxyCredential);
        var service = new ManagedModuleInstallService(logger, repositoryClient);
        ManagedModuleCommandSupport.ValidateClobberSwitches(AllowClobber.IsPresent, NoClobber.IsPresent);
        var writeSummary = ManagedModuleCommandSupport.ShouldWriteSummary(ShowSummary.IsPresent, Quiet.IsPresent);
        var targets = ResolveTargets().ToArray();
        ManagedModuleCommandSupport.ValidateSinglePackageHashTarget(ExpectedPackageSha256, targets.Select(static target => target.Name).ToArray());
        var defaultRepository = ManagedModuleCommandSupport.CreateRepository(
            this,
            RepositoryName,
            Repository,
            ProfileName,
            MyInvocation.BoundParameters.ContainsKey("Repository"));

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
                Scope = string.IsNullOrWhiteSpace(moduleRoot) ? target.Scope : ManagedModuleInstallScope.Custom,
                ShellEdition = ShellEdition,
                ModuleRoot = moduleRoot,
                PackageCacheDirectory = packageCacheDirectory,
                DependencyConcurrency = DependencyConcurrency,
                ExpectedPackageSha256 = ExpectedPackageSha256,
                TrustPolicy = trustPolicy,
                Credential = credential,
                Force = target.Reinstall,
                AllowClobber = target.AllowClobber,
                AcceptLicense = target.AcceptLicense,
                AuthenticodeCheck = AuthenticodeCheck.IsPresent,
                SkipDependencyCheck = target.SkipDependencyCheck
            };

            if (Plan.IsPresent)
            {
                var plan = await service.PlanInstallAsync(request, CancelToken).ConfigureAwait(false);
                WriteObject(plan);
                if (writeSummary)
                    ManagedModuleSummaryWriter.Write(plan);
                continue;
            }

            if (!ShouldProcess(target.Name, $"Install managed module from repository '{repository.Name}'"))
                continue;

            var result = await service.InstallAsync(request, CancelToken).ConfigureAwait(false);

            WriteObject(result);
            if (writeSummary)
                ManagedModuleSummaryWriter.Write(result);
        }
    }

    private IEnumerable<RequiredResourceTarget> ResolveTargets()
    {
        if (ParameterSetName == RequiredResourceParameterSet)
            return ParseRequiredResource(RequiredResource);

        if (ParameterSetName == RequiredResourceFileParameterSet)
            return ParseRequiredResource(ImportRequiredResourceFile());

        var force = ManagedModuleCommandSupport.ResolveForce(Force.IsPresent, Reinstall.IsPresent);
        var allowClobber = AllowClobber.IsPresent && !NoClobber.IsPresent;
        return Name.Select(moduleName => new RequiredResourceTarget(
            moduleName,
            Version,
            MinimumVersion,
            MaximumVersion,
            VersionPolicy,
            Prerelease.IsPresent,
            Scope,
            null,
            force,
            allowClobber,
            AcceptLicense.IsPresent,
            SkipDependencyCheck.IsPresent));
    }

    private object ImportRequiredResourceFile()
    {
        var path = ManagedModuleCommandSupport.ResolveProviderPath(this, RequiredResourceFile)
            ?? throw new InvalidOperationException("RequiredResourceFile path is required.");
        if (!File.Exists(path))
            throw new FileNotFoundException("RequiredResourceFile was not found.", path);

        using var ps = PowerShell.Create(RunspaceMode.CurrentRunspace);
        ps.AddCommand("Microsoft.PowerShell.Utility\\Import-PowerShellDataFile")
            .AddParameter("LiteralPath", path);
        var output = ps.Invoke();
        if (ps.HadErrors)
            throw new InvalidOperationException(string.Join(Environment.NewLine, ps.Streams.Error.Select(static error => error.ToString())));
        if (output.Count != 1)
            throw new InvalidOperationException($"RequiredResourceFile '{path}' did not return a single hashtable.");

        return output[0].BaseObject;
    }

    private IEnumerable<RequiredResourceTarget> ParseRequiredResource(object? resource)
    {
        var table = AsDictionary(resource)
            ?? throw new InvalidOperationException("RequiredResource must be a hashtable whose keys are resource names and whose values are resource option hashtables.");

        foreach (DictionaryEntry entry in table)
        {
            var keyName = Convert.ToString(entry.Key)?.Trim();
            if (string.IsNullOrWhiteSpace(keyName))
                throw new InvalidOperationException("RequiredResource contains an empty resource name.");

            var options = AsDictionary(entry.Value)
                ?? throw new InvalidOperationException($"RequiredResource input with name '{keyName}' does not have a valid value; the value must be a hashtable.");
            yield return ParseRequiredResourceEntry(keyName!, options);
        }
    }

    private RequiredResourceTarget ParseRequiredResourceEntry(string fallbackName, IDictionary options)
    {
        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Name",
            "Version",
            "Repository",
            "AcceptLicense",
            "Prerelease",
            "Scope",
            "Quiet",
            "Reinstall",
            "TrustRepository",
            "NoClobber",
            "SkipDependencyCheck"
        };
        foreach (DictionaryEntry option in options)
        {
            var key = Convert.ToString(option.Key)?.Trim();
            if (string.IsNullOrWhiteSpace(key) || !allowed.Contains(key!))
                throw new InvalidOperationException($"The parameter '{key}' provided is not a recognized or valid required resource parameter.");
        }

        var name = GetString(options, "Name") ?? fallbackName;
        var repository = GetString(options, "Repository");
        var prerelease = GetBool(options, "Prerelease") ?? Prerelease.IsPresent;
        var reinstall = GetBool(options, "Reinstall") ?? ManagedModuleCommandSupport.ResolveForce(Force.IsPresent, Reinstall.IsPresent);
        var noClobber = GetBool(options, "NoClobber") ?? false;
        var acceptLicense = GetBool(options, "AcceptLicense") ?? AcceptLicense.IsPresent;
        var skipDependencyCheck = GetBool(options, "SkipDependencyCheck") ?? SkipDependencyCheck.IsPresent;
        var scope = GetScope(options, "Scope") ?? Scope;
        var version = GetString(options, "Version");
        SplitRequiredResourceVersion(version, out var exactVersion, out var versionPolicy);
        return new RequiredResourceTarget(
            name,
            exactVersion,
            null,
            null,
            versionPolicy,
            prerelease,
            scope,
            repository,
            reinstall,
            AllowClobber.IsPresent && !NoClobber.IsPresent && !noClobber,
            acceptLicense,
            skipDependencyCheck);
    }

    private static IDictionary? AsDictionary(object? value)
    {
        if (value is null)
            return null;
        if (value is PSObject psObject)
            value = psObject.BaseObject;
        return value as IDictionary;
    }

    private static string? GetString(IDictionary options, string key)
        => TryGetOption(options, key, out var value) && value is not null
            ? Convert.ToString(value)?.Trim()
            : null;

    private static bool? GetBool(IDictionary options, string key)
    {
        if (!TryGetOption(options, key, out var value) || value is null)
            return null;
        if (value is bool boolean)
            return boolean;
        if (bool.TryParse(Convert.ToString(value), out var parsed))
            return parsed;

        throw new InvalidOperationException($"RequiredResource parameter '{key}' must be a Boolean value.");
    }

    private static ManagedModuleInstallScope? GetScope(IDictionary options, string key)
    {
        var value = GetString(options, key);
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (Enum.TryParse<ManagedModuleInstallScope>(value, ignoreCase: true, out var scope))
            return scope;

        throw new InvalidOperationException($"RequiredResource parameter '{key}' has unsupported value '{value}'.");
    }

    private static bool TryGetOption(IDictionary options, string key, out object? value)
    {
        foreach (DictionaryEntry option in options)
        {
            if (string.Equals(Convert.ToString(option.Key), key, StringComparison.OrdinalIgnoreCase))
            {
                value = option.Value is PSObject psObject ? psObject.BaseObject : option.Value;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static void SplitRequiredResourceVersion(string? version, out string? exactVersion, out string? versionPolicy)
    {
        exactVersion = null;
        versionPolicy = null;
        if (string.IsNullOrWhiteSpace(version))
            return;

        var trimmed = version!.Trim();
        if ((trimmed.StartsWith("[", StringComparison.Ordinal) || trimmed.StartsWith("(", StringComparison.Ordinal)) &&
            trimmed.IndexOf(",", StringComparison.Ordinal) >= 0)
        {
            versionPolicy = trimmed;
            return;
        }

        if (trimmed.StartsWith("[", StringComparison.Ordinal) &&
            trimmed.EndsWith("]", StringComparison.Ordinal) &&
            trimmed.IndexOf(",", StringComparison.Ordinal) < 0)
        {
            exactVersion = trimmed.Substring(1, trimmed.Length - 2).Trim();
            return;
        }

        exactVersion = trimmed;
    }

    private sealed class RequiredResourceTarget
    {
        public RequiredResourceTarget(
            string name,
            string? version,
            string? minimumVersion,
            string? maximumVersion,
            string? versionPolicy,
            bool includePrerelease,
            ManagedModuleInstallScope scope,
            string? repository,
            bool reinstall,
            bool allowClobber,
            bool acceptLicense,
            bool skipDependencyCheck)
        {
            Name = name;
            Version = version;
            MinimumVersion = minimumVersion;
            MaximumVersion = maximumVersion;
            VersionPolicy = versionPolicy;
            IncludePrerelease = includePrerelease;
            Scope = scope;
            Repository = repository;
            Reinstall = reinstall;
            AllowClobber = allowClobber;
            AcceptLicense = acceptLicense;
            SkipDependencyCheck = skipDependencyCheck;
        }

        public string Name { get; }
        public string? Version { get; }
        public string? MinimumVersion { get; }
        public string? MaximumVersion { get; }
        public string? VersionPolicy { get; }
        public bool IncludePrerelease { get; }
        public ManagedModuleInstallScope Scope { get; }
        public string? Repository { get; }
        public bool Reinstall { get; }
        public bool AllowClobber { get; }
        public bool AcceptLicense { get; }
        public bool SkipDependencyCheck { get; }
    }

}
