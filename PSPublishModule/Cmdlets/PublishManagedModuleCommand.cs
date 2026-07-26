using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Publishes a PowerShell module package through the managed C# module engine.
/// </summary>
/// <remarks>
/// <para>
/// This command provides the module-publish functionality of <c>Publish-PSResource</c>. It can create a NuGet
/// package from a module folder or publish an existing .nupkg file to a local folder feed or NuGet-compatible
/// package publish endpoint.
/// </para>
/// <para>
/// Microsoft Artifact Registry module prefixes are not applied because the managed repository client does not yet
/// expose a container-registry transport.
/// </para>
/// </remarks>
/// <example>
/// <summary>Publish a module to a local folder feed</summary>
/// <code>Publish-ManagedModule -Path C:\Source\Company.Tools -Repository C:\Packages</code>
/// </example>
/// <example>
/// <summary>Publish an existing package without repacking it</summary>
/// <code>Publish-ManagedModule -NupkgPath C:\Packages\Company.Tools.1.2.0.nupkg -Repository CompanyFeed</code>
/// </example>
[Cmdlet(VerbsData.Publish, "ManagedModule", SupportsShouldProcess = true, DefaultParameterSetName = PathParameterSet)]
[OutputType(typeof(ManagedModulePublishResult))]
public sealed class PublishManagedModuleCommand : PSCmdlet
{
    private const string PathParameterSet = "PathParameterSet";
    private const string NupkgPathParameterSet = "NupkgPathParameterSet";

    /// <summary>Module folder to package.</summary>
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = PathParameterSet)]
    [Alias("ModulePath")]
    [ValidateNotNullOrEmpty]
    public string? Path { get; set; }

    /// <summary>Existing .nupkg file to publish without repacking a module folder.</summary>
    [Parameter(Mandatory = true, ParameterSetName = NupkgPathParameterSet)]
    [ValidateNotNullOrEmpty]
    public string? NupkgPath { get; set; }

    /// <summary>Repository URL, NuGet v3 service index, publish endpoint, or local folder feed.</summary>
    [Parameter(Position = 1)]
    [Alias("RepositoryPath", "RepositoryUri", "Source")]
    [ValidateNotNullOrEmpty]
    public string? Repository { get; set; }

    /// <summary>Friendly repository name used in output.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string RepositoryName { get; set; } = ManagedModuleCommandSupport.DefaultRepositoryName;

    /// <summary>Saved module repository profile to use instead of Repository or OutputDirectory.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? ProfileName { get; set; }

    /// <summary>Directory that receives the created or supplied package; it is also the local target when Repository is omitted.</summary>
    [Parameter]
    [Alias("DestinationPath", "OutputPath")]
    [ValidateNotNullOrEmpty]
    public string? OutputDirectory { get; set; }

    /// <summary>Optional explicit module manifest path.</summary>
    [Parameter(ParameterSetName = PathParameterSet)]
    [ValidateNotNullOrEmpty]
    public string? ManifestPath { get; set; }

    /// <summary>Optional package id override.</summary>
    [Parameter(ParameterSetName = PathParameterSet)]
    [ValidateNotNullOrEmpty]
    public string? Name { get; set; }

    /// <summary>Optional package version override.</summary>
    [Parameter(ParameterSetName = PathParameterSet)]
    [ValidateNotNullOrEmpty]
    public string? Version { get; set; }

    /// <summary>Optional authors override.</summary>
    [Parameter(ParameterSetName = PathParameterSet)]
    public string? Authors { get; set; }

    /// <summary>Optional description override.</summary>
    [Parameter(ParameterSetName = PathParameterSet)]
    public string? Description { get; set; }

    /// <summary>Optional project URL override.</summary>
    [Parameter(ParameterSetName = PathParameterSet)]
    public string? ProjectUrl { get; set; }

    /// <summary>Optional package tags override.</summary>
    [Parameter(ParameterSetName = PathParameterSet)]
    public string[]? Tags { get; set; }

    /// <summary>Optional repository credential.</summary>
    [Parameter]
    public PSCredential? Credential { get; set; }

    /// <summary>API key used by NuGet-compatible package publish endpoints.</summary>
    [Parameter]
    [Alias("NuGetApiKey")]
    public string? ApiKey { get; set; }

    /// <summary>Optional path to a file containing the API key.</summary>
    [Parameter]
    [Alias("ApiKeyPath", "NuGetApiKeyPath")]
    public string? ApiKeyFilePath { get; set; }

    /// <summary>Optional HTTP proxy used for repository requests.</summary>
    [Parameter]
    [ValidateNotNull]
    public Uri? Proxy { get; set; }

    /// <summary>Optional proxy credential used with Proxy.</summary>
    [Parameter]
    public PSCredential? ProxyCredential { get; set; }

    /// <summary>Overwrite an existing package.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>Skip checking RequiredModules against the target repository.</summary>
    [Parameter]
    public SwitchParameter SkipDependenciesCheck { get; set; }

    /// <summary>Skip managed manifest metadata validation before packaging.</summary>
    [Parameter(ParameterSetName = PathParameterSet)]
    public SwitchParameter SkipModuleManifestValidate { get; set; }

    /// <summary>Write a compact Spectre.Console summary for the publish result.</summary>
    [Parameter]
    public SwitchParameter ShowSummary { get; set; }

    /// <summary>Creates and publishes the package to the selected destination.</summary>
    protected override void ProcessRecord()
    {
        var modulePath = ManagedModuleCommandSupport.ResolveProviderPath(this, Path);
        var packagePath = ManagedModuleCommandSupport.ResolveProviderPath(this, NupkgPath);
        var manifestPath = ManagedModuleCommandSupport.ResolveProviderPath(this, ManifestPath);
        var repository = ResolveRepository();
        var publishRepository = ResolvePublishRepository();
        var outputDirectory = ManagedModuleCommandSupport.ResolveProviderPath(this, OutputDirectory);
        var credential = ManagedModuleCommandSupport.ResolveCredential(this, Credential, null, null, null);
        var publishCredential = ManagedModuleCommandSupport.ResolveCredential(this, null, null, ApiKey, ApiKeyFilePath) ?? credential;
        var logger = new CmdletLogger(this, MyInvocation.BoundParameters.ContainsKey("Verbose"));
        var repositoryClient = ManagedModuleCommandSupport.CreateRepositoryClient(this, logger, Proxy, ProxyCredential);

        var sourcePath = packagePath ?? modulePath!;
        if (!ShouldProcess(sourcePath, $"Publish managed module package to '{publishRepository.Source}'"))
            return;

        var result = new ManagedModulePublishService(logger, repositoryClient).PublishAsync(
                new ManagedModulePublishRequest
                {
                    ModulePath = modulePath ?? string.Empty,
                    PackagePath = packagePath,
                    ManifestPath = manifestPath,
                    Name = Name,
                    Version = Version,
                    Repository = repository,
                    PublishRepository = publishRepository,
                    OutputDirectory = outputDirectory,
                    Credential = credential,
                    PublishCredential = publishCredential,
                    Authors = Authors,
                    Description = Description,
                    ProjectUrl = ProjectUrl,
                    Tags = Tags,
                    SkipDependenciesCheck = SkipDependenciesCheck.IsPresent,
                    SkipModuleManifestValidate = SkipModuleManifestValidate.IsPresent,
                    Force = Force.IsPresent
                })
            .GetAwaiter()
            .GetResult();

        WriteObject(result);
        if (ShowSummary.IsPresent)
            ManagedModuleSummaryWriter.Write(result);
    }

    private ManagedModuleRepository ResolveRepository()
        => ManagedModuleCommandSupport.CreatePublishReadRepository(
            this,
            RepositoryName,
            Repository,
            OutputDirectory,
            ProfileName,
            MyInvocation.BoundParameters.ContainsKey(nameof(Repository)),
            MyInvocation.BoundParameters.ContainsKey(nameof(OutputDirectory)));

    private ManagedModuleRepository ResolvePublishRepository()
        => ManagedModuleCommandSupport.CreatePublishRepository(
            this,
            RepositoryName,
            Repository,
            OutputDirectory,
            ProfileName,
            MyInvocation.BoundParameters.ContainsKey(nameof(Repository)),
            MyInvocation.BoundParameters.ContainsKey(nameof(OutputDirectory)));
}
