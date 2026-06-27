using System;
using System.Collections.Generic;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Saves modules from a managed repository to an explicit module root.
/// </summary>
/// <remarks>
/// <para>
/// This command uses the same managed C# repository and archive engine as <c>Install-ManagedModule</c>, but requires
/// an explicit destination root instead of installing into the default PowerShell module paths.
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
[Cmdlet(VerbsData.Save, "ManagedModule", SupportsShouldProcess = true)]
[Alias("Save-PublicModule")]
[OutputType(typeof(ManagedModuleInstallResult), typeof(ManagedModuleInstallPlan))]
public sealed class SaveManagedModuleCommand : PSCmdlet
{
    private readonly List<ManagedModuleInstallResult> _results = new();

    /// <summary>Module names to save.</summary>
    [Parameter(Mandatory = true, Position = 0, ValueFromPipelineByPropertyName = true)]
    [Alias("ModuleName")]
    [ValidateNotNullOrEmpty]
    public string[] Name { get; set; } = Array.Empty<string>();

    /// <summary>Destination module root.</summary>
    [Parameter(Mandatory = true, Position = 1)]
    [Alias("DestinationPath", "ModuleRoot")]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <summary>Repository URL, NuGet v3 service index, flat-container URL, or local folder feed.</summary>
    [Parameter]
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
    [Parameter]
    [Alias("RequiredVersion")]
    [ValidateNotNullOrEmpty]
    public string? Version { get; set; }

    /// <summary>Minimum package version to save when Version is omitted.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? MinimumVersion { get; set; }

    /// <summary>Maximum package version to save when Version is omitted.</summary>
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

    /// <summary>Overwrite an existing saved version.</summary>
    [Parameter]
    public SwitchParameter Force { get; set; }

    /// <summary>Allow command exports to overlap with other modules in the destination root.</summary>
    [Parameter]
    public SwitchParameter AllowClobber { get; set; }

    /// <summary>Accept package licenses when packages declare license acceptance is required.</summary>
    [Parameter]
    public SwitchParameter AcceptLicense { get; set; }

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

    /// <summary>Saves requested modules.</summary>
    protected override void ProcessRecord()
    {
        var moduleRoot = ManagedModuleCommandSupport.ResolveProviderPath(this, Path)!;
        var repository = ManagedModuleCommandSupport.CreateRepository(
            this,
            RepositoryName,
            Repository,
            ProfileName,
            MyInvocation.BoundParameters.ContainsKey("Repository"));
        var credential = ManagedModuleCommandSupport.ResolveCredential(this, Credential, CredentialUserName, CredentialSecret, CredentialSecretFilePath);
        var logger = new CmdletLogger(this, MyInvocation.BoundParameters.ContainsKey("Verbose"));
        var service = new ManagedModuleInstallService(logger);

        foreach (var moduleName in Name)
        {
            var request = new ManagedModuleInstallRequest
            {
                Repository = repository,
                Name = moduleName,
                Version = Version,
                MinimumVersion = MinimumVersion,
                MaximumVersion = MaximumVersion,
                VersionPolicy = VersionPolicy,
                IncludePrerelease = Prerelease.IsPresent,
                Scope = ManagedModuleInstallScope.Custom,
                ModuleRoot = moduleRoot,
                PackageCacheDirectory = ManagedModuleCommandSupport.ResolveProviderPath(this, PackageCacheDirectory),
                Credential = credential,
                Force = Force.IsPresent,
                AllowClobber = AllowClobber.IsPresent,
                AcceptLicense = AcceptLicense.IsPresent,
                SkipDependencyCheck = SkipDependencyCheck.IsPresent
            };

            if (Plan.IsPresent)
            {
                var plan = service.PlanInstallAsync(request).GetAwaiter().GetResult();
                WriteObject(plan);
                if (ShowSummary.IsPresent)
                    ManagedModuleSummaryWriter.Write(plan);
                continue;
            }

            if (!ShouldProcess(moduleName, $"Save managed module to '{moduleRoot}'"))
                continue;

            var result = service.InstallAsync(request).GetAwaiter().GetResult();
            _results.Add(result);

            WriteObject(result);
            if (ShowSummary.IsPresent)
                ManagedModuleSummaryWriter.Write(result);
        }
    }

    /// <summary>Writes optional offline bundle metadata after all save results are available.</summary>
    protected override void EndProcessing()
    {
        if (Plan.IsPresent || string.IsNullOrWhiteSpace(BundleMetadataPath) || _results.Count == 0)
            return;

        var metadataPath = ManagedModuleCommandSupport.ResolveProviderPath(this, BundleMetadataPath);
        if (!ShouldProcess(metadataPath, "Write managed module bundle metadata"))
            return;

        new ManagedModuleBundleMetadataWriter().Write(metadataPath!, _results);
    }
}
