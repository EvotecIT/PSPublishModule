using System;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Measures managed module lifecycle scenarios through the managed C# module engine.
/// </summary>
/// <remarks>
/// <para>
/// This command is a thin PowerShell surface over the reusable PowerForge benchmark service. It measures the managed
/// engine directly and returns typed benchmark run data that can be compared with compatibility baselines.
/// </para>
/// </remarks>
/// <example>
/// <summary>Measure a managed install into a custom root</summary>
/// <code>Measure-ManagedModule -Name Company.Tools -Operation Install -Repository C:\Packages -ModuleRoot C:\Temp\Modules</code>
/// </example>
[Cmdlet(VerbsDiagnostic.Measure, "ManagedModule", SupportsShouldProcess = true)]
[OutputType(typeof(ManagedModuleBenchmarkResult))]
public sealed class MeasureManagedModuleCommand : PSCmdlet
{
    /// <summary>Module names to measure.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true)]
    [Alias("ModuleName")]
    [ValidateNotNullOrEmpty]
    public string[] Name { get; set; } = Array.Empty<string>();

    /// <summary>Lifecycle operation to measure.</summary>
    [Parameter]
    public ManagedModuleBenchmarkOperation Operation { get; set; } = ManagedModuleBenchmarkOperation.Install;

    /// <summary>Delivery engines to measure for each scenario.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public ManagedModuleBenchmarkEngine[] Engine { get; set; } = new[] { ManagedModuleBenchmarkEngine.Managed };

    /// <summary>Repository URL, NuGet v3 service index, flat-container URL, or local folder feed.</summary>
    [Parameter]
    [Alias("Source", "RepositoryUri")]
    [ValidateNotNullOrEmpty]
    public string Repository { get; set; } = ManagedModuleCommandSupport.DefaultRepositorySource;

    /// <summary>Friendly repository name used in output.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string RepositoryName { get; set; } = ManagedModuleCommandSupport.DefaultRepositoryName;

    /// <summary>Exact package version to measure.</summary>
    [Parameter]
    [Alias("RequiredVersion")]
    [ValidateNotNullOrEmpty]
    public string? Version { get; set; }

    /// <summary>Minimum package version to measure when Version is omitted.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? MinimumVersion { get; set; }

    /// <summary>Maximum package version to measure when Version is omitted.</summary>
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

    /// <summary>Install or update scope used when ModuleRoot is not supplied.</summary>
    [Parameter]
    public ManagedModuleInstallScope Scope { get; set; } = ManagedModuleInstallScope.CurrentUser;

    /// <summary>PowerShell path family used when resolving default CurrentUser or AllUsers module roots.</summary>
    [Parameter]
    public ManagedModuleShellEdition ShellEdition { get; set; } = ManagedModuleShellEdition.Auto;

    /// <summary>Explicit module root for install, save, or update measurements.</summary>
    [Parameter]
    [Alias("Path", "DestinationPath")]
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

    /// <summary>Force reinstall or update of the target version.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>Allow command exports to overlap with other modules in the target root.</summary>
    [Parameter]
    public SwitchParameter AllowClobber { get; set; }

    /// <summary>Accept package licenses when packages declare license acceptance is required.</summary>
    [Parameter]
    public SwitchParameter AcceptLicense { get; set; }

    /// <summary>Skip installing dependencies declared by the package.</summary>
    [Parameter]
    [Alias("SkipDependenciesCheck")]
    public SwitchParameter SkipDependencyCheck { get; set; }

    /// <summary>Number of times each module scenario should be measured.</summary>
    [Parameter]
    [ValidateRange(1, int.MaxValue)]
    public int Iterations { get; set; } = 1;

    /// <summary>Stop at the first scenario failure instead of recording failed runs and continuing.</summary>
    [Parameter]
    public SwitchParameter StopOnError { get; set; }

    /// <summary>Import the delivered module in out-of-process PowerShell hosts and record version evidence.</summary>
    [Parameter]
    public SwitchParameter ValidateImport { get; set; }

    /// <summary>PowerShell hosts used when ValidateImport is enabled.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public ManagedModuleImportValidationHost[] ImportHost { get; set; } = Array.Empty<ManagedModuleImportValidationHost>();

    /// <summary>Optional JSON report path for benchmark evidence.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? ReportPath { get; set; }

    /// <summary>Optional Markdown report path for benchmark evidence.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? MarkdownReportPath { get; set; }

    /// <summary>Runs the requested benchmark scenarios.</summary>
    protected override void ProcessRecord()
    {
        var moduleRoot = ManagedModuleCommandSupport.ResolveProviderPath(this, ModuleRoot);
        var packageCacheDirectory = ManagedModuleCommandSupport.ResolveProviderPath(this, PackageCacheDirectory);
        var reportPath = ManagedModuleCommandSupport.ResolveProviderPath(this, ReportPath);
        var markdownReportPath = ManagedModuleCommandSupport.ResolveProviderPath(this, MarkdownReportPath);
        var repository = ManagedModuleCommandSupport.CreateRepository(this, RepositoryName, Repository);
        var credential = ManagedModuleCommandSupport.ResolveCredential(this, Credential, CredentialUserName, CredentialSecret, CredentialSecretFilePath);
        var scenarios = Name
            .Where(static moduleName => !string.IsNullOrWhiteSpace(moduleName))
            .Select(moduleName => CreateScenario(moduleName, repository, moduleRoot, packageCacheDirectory, credential))
            .ToArray();

        if (scenarios.Length == 0)
            return;
        if (!ShouldProcess(string.Join(", ", scenarios.Select(static scenario => scenario.Name)), $"Measure module {Operation} with {string.Join(", ", Engine)}"))
            return;

        var logger = new CmdletLogger(this, MyInvocation.BoundParameters.ContainsKey("Verbose"));
        var result = new ManagedModuleBenchmarkService(logger).RunAsync(
            new ManagedModuleBenchmarkRequest
            {
                Scenarios = scenarios,
                Engines = Engine,
                ContinueOnError = !StopOnError.IsPresent
            }).GetAwaiter().GetResult();

        if (ValidateImport.IsPresent)
            new ManagedModuleImportValidationService().Validate(result, ImportHost);

        WriteReports(result, reportPath, markdownReportPath);
        WriteObject(result);
    }

    private void WriteReports(ManagedModuleBenchmarkResult result, string? reportPath, string? markdownReportPath)
    {
        if (string.IsNullOrWhiteSpace(reportPath) && string.IsNullOrWhiteSpace(markdownReportPath))
            return;

        var writer = new ManagedModuleBenchmarkReportWriter();
        if (!string.IsNullOrWhiteSpace(reportPath) && ShouldProcess(reportPath, "Write managed module benchmark JSON report"))
            writer.WriteJson(reportPath!, result);
        if (!string.IsNullOrWhiteSpace(markdownReportPath) && ShouldProcess(markdownReportPath, "Write managed module benchmark Markdown report"))
            writer.WriteMarkdown(markdownReportPath!, result);
    }

    private ManagedModuleBenchmarkScenario CreateScenario(
        string moduleName,
        ManagedModuleRepository repository,
        string? moduleRoot,
        string? packageCacheDirectory,
        RepositoryCredential? credential)
        => new()
        {
            Id = Operation + ":" + moduleName.Trim(),
            Operation = Operation,
            Repository = repository,
            Name = moduleName.Trim(),
            Version = Version,
            MinimumVersion = MinimumVersion,
            MaximumVersion = MaximumVersion,
            VersionPolicy = VersionPolicy,
            IncludePrerelease = Prerelease.IsPresent,
            Scope = string.IsNullOrWhiteSpace(moduleRoot) ? Scope : ManagedModuleInstallScope.Custom,
            ShellEdition = ShellEdition,
            ModuleRoot = moduleRoot,
            PackageCacheDirectory = packageCacheDirectory,
            Credential = credential,
            Force = Force.IsPresent,
            AllowClobber = AllowClobber.IsPresent,
            AcceptLicense = AcceptLicense.IsPresent,
            SkipDependencyCheck = SkipDependencyCheck.IsPresent,
            Iterations = Iterations
        };
}
