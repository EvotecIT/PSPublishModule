using System;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Publishes a PowerShell module package through the managed C# module engine.
/// </summary>
/// <remarks>
/// <para>
/// This managed publish surface creates a NuGet package from a module folder and publishes it to a local folder feed
/// or NuGet-compatible package publish endpoint.
/// </para>
/// </remarks>
/// <example>
/// <summary>Publish a module to a local folder feed</summary>
/// <code>Publish-ManagedModule -Path C:\Source\Company.Tools -Repository C:\Packages</code>
/// </example>
[Cmdlet(VerbsData.Publish, "ManagedModule", SupportsShouldProcess = true)]
[OutputType(typeof(ManagedModulePublishResult))]
public sealed class PublishManagedModuleCommand : PSCmdlet
{
    /// <summary>Module folder to package.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [Alias("ModulePath")]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

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

    /// <summary>Output directory used when Repository is omitted.</summary>
    [Parameter]
    [Alias("DestinationPath", "OutputPath")]
    [ValidateNotNullOrEmpty]
    public string? OutputDirectory { get; set; }

    /// <summary>Optional explicit module manifest path.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? ManifestPath { get; set; }

    /// <summary>Optional package id override.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? Name { get; set; }

    /// <summary>Optional package version override.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? Version { get; set; }

    /// <summary>Optional authors override.</summary>
    [Parameter]
    public string? Authors { get; set; }

    /// <summary>Optional description override.</summary>
    [Parameter]
    public string? Description { get; set; }

    /// <summary>Optional project URL override.</summary>
    [Parameter]
    public string? ProjectUrl { get; set; }

    /// <summary>Optional package tags override.</summary>
    [Parameter]
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

    /// <summary>Overwrite an existing package.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>Skip checking RequiredModules against the target repository.</summary>
    [Parameter]
    public SwitchParameter SkipDependenciesCheck { get; set; }

    /// <summary>Skip managed manifest metadata validation before packaging.</summary>
    [Parameter]
    public SwitchParameter SkipModuleManifestValidate { get; set; }

    /// <summary>Write a compact Spectre.Console summary for the publish result.</summary>
    [Parameter]
    public SwitchParameter ShowSummary { get; set; }

    /// <summary>Creates and publishes the package to the selected destination.</summary>
    protected override void ProcessRecord()
    {
        var modulePath = ManagedModuleCommandSupport.ResolveProviderPath(this, Path)!;
        var manifestPath = ManagedModuleCommandSupport.ResolveProviderPath(this, ManifestPath);
        var repository = ResolveRepository();
        var outputDirectory = ManagedModuleCommandSupport.ResolveProviderPath(this, OutputDirectory);
        var credential = ManagedModuleCommandSupport.ResolveCredential(this, Credential, null, ApiKey, ApiKeyFilePath);
        var logger = new CmdletLogger(this, MyInvocation.BoundParameters.ContainsKey("Verbose"));

        if (!ShouldProcess(modulePath, $"Publish managed module package to '{repository.Source}'"))
            return;

        var result = new ManagedModulePublishService(logger).PublishAsync(
                new ManagedModulePublishRequest
                {
                    ModulePath = modulePath,
                    ManifestPath = manifestPath,
                    Name = Name,
                    Version = Version,
                    Repository = repository,
                    OutputDirectory = outputDirectory,
                    Credential = credential,
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
        => ManagedModuleCommandSupport.CreatePublishRepository(
            this,
            RepositoryName,
            Repository,
            OutputDirectory,
            ProfileName,
            MyInvocation.BoundParameters.ContainsKey(nameof(Repository)),
            MyInvocation.BoundParameters.ContainsKey(nameof(OutputDirectory)));
}
