using System.Collections;
using System.IO;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// References an existing <c>project.build.json</c> package build from the module-build DSL.
/// </summary>
/// <remarks>
/// <para>
/// Use this cmdlet inside <c>Build-Module { }</c> when package build details already live in a JSON file and the
/// module build should coordinate with that package lane.
/// </para>
/// </remarks>
[Cmdlet(VerbsCommon.New, "ConfigurationProjectBuild")]
[OutputType(typeof(ConfigurationProjectBuildSegment))]
public sealed class NewConfigurationProjectBuildCommand : PSCmdlet
{
    /// <summary>Optional friendly name for this package build lane.</summary>
    [Parameter]
    public string? Name { get; set; }

    /// <summary>Path to an existing <c>project.build.json</c> file.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string ConfigPath { get; set; } = Path.Combine("Build", "project.build.json");

    /// <summary>Whether this project build lane is enabled. Defaults to true.</summary>
    [Parameter]
    public SwitchParameter Enabled { get; set; }

    /// <summary>Whether package outputs must be produced before the module lane runs.</summary>
    [Parameter]
    public SwitchParameter BuildBeforeModule { get; set; }

    /// <summary>Whether the resolved package version should be used as the unified release version source.</summary>
    [Parameter]
    public SwitchParameter UseAsReleaseVersionSource { get; set; }

    /// <summary>Whether package outputs should be exposed as a temporary local NuGet feed for the module lane.</summary>
    [Parameter]
    public SwitchParameter ProvideLocalNuGetFeed { get; set; }

    /// <summary>Whether project/package versions should be updated, overriding the referenced JSON when set.</summary>
    [Parameter]
    public SwitchParameter UpdateVersions { get; set; }

    /// <summary>Whether package projects should be built/packed, overriding the referenced JSON when set.</summary>
    [Parameter]
    public SwitchParameter Build { get; set; }

    /// <summary>Whether NuGet packages should be published, overriding the referenced JSON when set.</summary>
    [Parameter]
    public SwitchParameter PublishNuget { get; set; }

    /// <summary>Whether package GitHub release publishing should be enabled, overriding the referenced JSON when set.</summary>
    [Parameter]
    public SwitchParameter PublishGitHub { get; set; }

    /// <summary>Whether release ZIPs should be created, overriding the referenced JSON when set.</summary>
    [Parameter]
    public SwitchParameter CreateReleaseZip { get; set; }

    /// <summary>Whether assemblies should be signed before packages are created, overriding the referenced JSON when set.</summary>
    [Parameter]
    public SwitchParameter SignAssemblies { get; set; }

    /// <summary>Whether copied dependency assemblies should also be signed, overriding the referenced JSON when set.</summary>
    [Parameter]
    public SwitchParameter SignDependencyAssemblies { get; set; }

    /// <summary>Whether generated NuGet packages should be signed, overriding the referenced JSON when set.</summary>
    [Parameter]
    public SwitchParameter SignPackages { get; set; }

    /// <summary>Additional project-build JSON overrides for less common fields.</summary>
    [Parameter]
    public IDictionary? Options { get; set; }

    /// <summary>Emits a project-build configuration segment.</summary>
    protected override void ProcessRecord()
    {
        WriteObject(new ConfigurationProjectBuildSegment
        {
            Configuration = new ProjectBuildConfigurationReference
            {
                Name = Normalize(Name),
                ConfigPath = ConfigPath,
                Enabled = BoundSwitchOrDefault(nameof(Enabled), Enabled, defaultValue: true),
                BuildBeforeModule = BuildBeforeModule.IsPresent,
                UseAsReleaseVersionSource = UseAsReleaseVersionSource.IsPresent,
                ProvideLocalNuGetFeed = ProvideLocalNuGetFeed.IsPresent,
                UpdateVersions = BoundSwitch(nameof(UpdateVersions), UpdateVersions),
                Build = BoundSwitch(nameof(Build), Build),
                PublishNuget = BoundSwitch(nameof(PublishNuget), PublishNuget),
                PublishGitHub = BoundSwitch(nameof(PublishGitHub), PublishGitHub),
                CreateReleaseZip = BoundSwitch(nameof(CreateReleaseZip), CreateReleaseZip),
                SignAssemblies = BoundSwitch(nameof(SignAssemblies), SignAssemblies),
                SignDependencyAssemblies = BoundSwitch(nameof(SignDependencyAssemblies), SignDependencyAssemblies),
                SignPackages = BoundSwitch(nameof(SignPackages), SignPackages),
                Options = PackageBuildConfiguration.ToObjectDictionary(Options)
            }
        });
    }

    private bool? BoundSwitch(string name, SwitchParameter value)
        => MyInvocation.BoundParameters.ContainsKey(name) ? value.IsPresent : null;

    private bool BoundSwitchOrDefault(string name, SwitchParameter value, bool defaultValue)
        => MyInvocation.BoundParameters.ContainsKey(name) ? value.IsPresent : defaultValue;

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}

/// <summary>
/// Creates inline .NET/NuGet package build configuration from the module-build DSL.
/// </summary>
/// <remarks>
/// <para>
/// This cmdlet mirrors the repository package build settings normally authored in <c>project.build.json</c>, allowing
/// <c>Build-Module { }</c> to remain the primary authoring surface for combined module and package publishing.
/// </para>
/// </remarks>
[Cmdlet(VerbsCommon.New, "ConfigurationPackageBuild")]
[OutputType(typeof(ConfigurationPackageBuildSegment))]
public sealed class NewConfigurationPackageBuildCommand : PSCmdlet
{
    /// <summary>Optional friendly name for this package build lane.</summary>
    [Parameter] public string? Name { get; set; }

    /// <summary>Whether this package build lane is enabled. Defaults to true.</summary>
    [Parameter] public SwitchParameter Enabled { get; set; }

    /// <summary>Whether package outputs must be produced before the module lane runs.</summary>
    [Parameter] public SwitchParameter BuildBeforeModule { get; set; }

    /// <summary>Whether the resolved package version should be used as the unified release version source.</summary>
    [Parameter] public SwitchParameter UseAsReleaseVersionSource { get; set; }

    /// <summary>Whether package outputs should be exposed as a temporary local NuGet feed for the module lane.</summary>
    [Parameter] public SwitchParameter ProvideLocalNuGetFeed { get; set; }

    /// <summary>Root path used for project discovery.</summary>
    [Parameter] public string? RootPath { get; set; }

    /// <summary>Global expected package version or X-pattern.</summary>
    [Parameter] public string? ExpectedVersion { get; set; }

    /// <summary>Per-project expected package version map.</summary>
    [Parameter] public IDictionary? ExpectedVersionMap { get; set; }

    /// <summary>Shared version tracks keyed by track name.</summary>
    [Parameter] public IDictionary? VersionTracks { get; set; }

    /// <summary>When true, <c>ExpectedVersionMap</c> acts as an include list.</summary>
    [Parameter] public SwitchParameter ExpectedVersionMapAsInclude { get; set; }

    /// <summary>When true, <c>ExpectedVersionMap</c> keys support wildcard matching.</summary>
    [Parameter] public SwitchParameter ExpectedVersionMapUseWildcards { get; set; }

    /// <summary>Project names to include.</summary>
    [Parameter] public string[]? IncludeProjects { get; set; }

    /// <summary>Project names to exclude.</summary>
    [Parameter] public string[]? ExcludeProjects { get; set; }

    /// <summary>Directory names to exclude from project discovery.</summary>
    [Parameter] public string[]? ExcludeDirectories { get; set; }

    /// <summary>NuGet sources used for version lookup.</summary>
    [Parameter] public string[]? NugetSource { get; set; }

    /// <summary>Whether prerelease versions can be considered during version lookup.</summary>
    [Parameter] public SwitchParameter IncludePrerelease { get; set; }

    /// <summary>Build configuration, usually Release or Debug.</summary>
    [Parameter] public string? Configuration { get; set; }

    /// <summary>Package output path override.</summary>
    [Parameter] public string? OutputPath { get; set; }

    /// <summary>Release ZIP output path override.</summary>
    [Parameter] public string? ReleaseZipOutputPath { get; set; }

    /// <summary>Staging root for project-build outputs.</summary>
    [Parameter] public string? StagingPath { get; set; }

    /// <summary>Whether to clean staging before the package build runs.</summary>
    [Parameter] public SwitchParameter CleanStaging { get; set; }

    /// <summary>Whether to produce a plan without executing package build steps.</summary>
    [Parameter] public SwitchParameter PlanOnly { get; set; }

    /// <summary>Plan output path.</summary>
    [Parameter] public string? PlanOutputPath { get; set; }

    /// <summary>Whether project/package versions should be updated.</summary>
    [Parameter] public SwitchParameter UpdateVersions { get; set; }

    /// <summary>Whether package projects should be built/packed.</summary>
    [Parameter] public SwitchParameter Build { get; set; }

    /// <summary>Pack strategy, for example PerProject or MSBuild.</summary>
    [Parameter] public string? PackStrategy { get; set; }

    /// <summary>Whether NuGet packages should be published.</summary>
    [Parameter] public SwitchParameter PublishNuget { get; set; }

    /// <summary>Whether package GitHub release publishing should be enabled.</summary>
    [Parameter] public SwitchParameter PublishGitHub { get; set; }

    /// <summary>Whether release ZIPs should be created for package projects.</summary>
    [Parameter] public SwitchParameter CreateReleaseZip { get; set; }

    /// <summary>Whether GitHub Packages should be used as the NuGet version lookup and publish feed.</summary>
    [Parameter] public SwitchParameter UseGitHubPackages { get; set; }

    /// <summary>GitHub user or organization that owns the GitHub Packages NuGet feed.</summary>
    [Parameter] public string? GitHubPackagesOwner { get; set; }

    /// <summary>NuGet publish source.</summary>
    [Parameter] public string? PublishSource { get; set; }

    /// <summary>Inline NuGet publish API key. Prefer file or environment forms for automation.</summary>
    [Parameter] public string? PublishApiKey { get; set; }

    /// <summary>Path to a file containing the NuGet publish API key.</summary>
    [Parameter] public string? PublishApiKeyFilePath { get; set; }

    /// <summary>Environment variable containing the NuGet publish API key.</summary>
    [Parameter] public string? PublishApiKeyEnvName { get; set; }

    /// <summary>Whether duplicate NuGet packages should be skipped during push.</summary>
    [Parameter] public SwitchParameter SkipDuplicate { get; set; }

    /// <summary>Whether package publishing should stop on first failure.</summary>
    [Parameter] public SwitchParameter PublishFailFast { get; set; }

    /// <summary>Code signing certificate thumbprint for package signing.</summary>
    [Parameter] public string? CertificateThumbprint { get; set; }

    /// <summary>Certificate store location for package signing.</summary>
    [Parameter] public string? CertificateStore { get; set; }

    /// <summary>Timestamp server URL for package signing.</summary>
    [Parameter] public string? TimeStampServer { get; set; }

    /// <summary>Whether assemblies should be signed before packages are created.</summary>
    [Parameter] public SwitchParameter SignAssemblies { get; set; }

    /// <summary>Whether copied dependency assemblies should also be signed.</summary>
    [Parameter] public SwitchParameter SignDependencyAssemblies { get; set; }

    /// <summary>Whether generated NuGet packages should be signed.</summary>
    [Parameter] public SwitchParameter SignPackages { get; set; }

    /// <summary>NuGet version lookup credential user name.</summary>
    [Parameter] public string? NugetCredentialUserName { get; set; }

    /// <summary>NuGet version lookup credential secret.</summary>
    [Parameter] public string? NugetCredentialSecret { get; set; }

    /// <summary>Path to a file containing the NuGet version lookup credential secret.</summary>
    [Parameter] public string? NugetCredentialSecretFilePath { get; set; }

    /// <summary>Environment variable containing the NuGet version lookup credential secret.</summary>
    [Parameter] public string? NugetCredentialSecretEnvName { get; set; }

    /// <summary>Inline GitHub access token. Prefer file or environment forms for automation.</summary>
    [Parameter] public string? GitHubAccessToken { get; set; }

    /// <summary>Path to a file containing the GitHub access token.</summary>
    [Parameter] public string? GitHubAccessTokenFilePath { get; set; }

    /// <summary>Environment variable containing the GitHub access token.</summary>
    [Parameter] public string? GitHubAccessTokenEnvName { get; set; }

    /// <summary>GitHub owner/user name.</summary>
    [Parameter] public string? GitHubUsername { get; set; }

    /// <summary>GitHub repository name.</summary>
    [Parameter] public string? GitHubRepositoryName { get; set; }

    /// <summary>Whether GitHub releases should be marked prerelease.</summary>
    [Parameter] public SwitchParameter GitHubIsPreRelease { get; set; }

    /// <summary>Whether project name should be included in generated package GitHub tags. Defaults to true.</summary>
    [Parameter] public SwitchParameter GitHubIncludeProjectNameInTag { get; set; }

    /// <summary>Whether GitHub should generate release notes.</summary>
    [Parameter] public SwitchParameter GitHubGenerateReleaseNotes { get; set; }

    /// <summary>GitHub release name template or override.</summary>
    [Parameter] public string? GitHubReleaseName { get; set; }

    /// <summary>GitHub tag name override.</summary>
    [Parameter] public string? GitHubTagName { get; set; }

    /// <summary>GitHub tag template.</summary>
    [Parameter] public string? GitHubTagTemplate { get; set; }

    /// <summary>GitHub release mode, for example Single or PerProject.</summary>
    [Parameter] public string? GitHubReleaseMode { get; set; }

    /// <summary>Primary project used for single-release version resolution.</summary>
    [Parameter] public string? GitHubPrimaryProject { get; set; }

    /// <summary>GitHub tag conflict policy.</summary>
    [Parameter] public string? GitHubTagConflictPolicy { get; set; }

    /// <summary>Additional project-build options for fields not yet modeled as first-class parameters.</summary>
    [Parameter] public IDictionary? Options { get; set; }

    /// <summary>Emits an inline package-build configuration segment.</summary>
    protected override void ProcessRecord()
    {
        WriteObject(new ConfigurationPackageBuildSegment
        {
            Configuration = new PackageBuildConfiguration
            {
                Name = Normalize(Name),
                Enabled = BoundSwitchOrDefault(nameof(Enabled), Enabled, defaultValue: true),
                BuildBeforeModule = BuildBeforeModule.IsPresent,
                UseAsReleaseVersionSource = UseAsReleaseVersionSource.IsPresent,
                ProvideLocalNuGetFeed = ProvideLocalNuGetFeed.IsPresent,
                RootPath = Normalize(RootPath),
                ExpectedVersion = Normalize(ExpectedVersion),
                ExpectedVersionMap = PackageBuildConfiguration.ToStringDictionary(ExpectedVersionMap),
                VersionTracks = PackageBuildConfiguration.ToVersionTracksDictionary(VersionTracks),
                ExpectedVersionMapAsInclude = ExpectedVersionMapAsInclude.IsPresent,
                ExpectedVersionMapUseWildcards = ExpectedVersionMapUseWildcards.IsPresent,
                IncludeProjects = IncludeProjects,
                ExcludeProjects = ExcludeProjects,
                ExcludeDirectories = ExcludeDirectories,
                NugetSource = NugetSource,
                IncludePrerelease = IncludePrerelease.IsPresent,
                Configuration = Normalize(Configuration),
                OutputPath = Normalize(OutputPath),
                ReleaseZipOutputPath = Normalize(ReleaseZipOutputPath),
                StagingPath = Normalize(StagingPath),
                CleanStaging = BoundSwitch(nameof(CleanStaging), CleanStaging),
                PlanOnly = BoundSwitch(nameof(PlanOnly), PlanOnly),
                PlanOutputPath = Normalize(PlanOutputPath),
                UpdateVersions = BoundSwitch(nameof(UpdateVersions), UpdateVersions),
                Build = BoundSwitch(nameof(Build), Build),
                PackStrategy = Normalize(PackStrategy),
                PublishNuget = BoundSwitch(nameof(PublishNuget), PublishNuget),
                PublishGitHub = BoundSwitch(nameof(PublishGitHub), PublishGitHub),
                CreateReleaseZip = BoundSwitch(nameof(CreateReleaseZip), CreateReleaseZip),
                UseGitHubPackages = UseGitHubPackages.IsPresent,
                GitHubPackagesOwner = Normalize(GitHubPackagesOwner),
                PublishSource = Normalize(PublishSource),
                PublishApiKey = PublishApiKey,
                PublishApiKeyFilePath = Normalize(PublishApiKeyFilePath),
                PublishApiKeyEnvName = Normalize(PublishApiKeyEnvName),
                SkipDuplicate = BoundSwitch(nameof(SkipDuplicate), SkipDuplicate),
                PublishFailFast = BoundSwitch(nameof(PublishFailFast), PublishFailFast),
                CertificateThumbprint = Normalize(CertificateThumbprint),
                CertificateStore = Normalize(CertificateStore),
                TimeStampServer = Normalize(TimeStampServer),
                SignAssemblies = BoundSwitch(nameof(SignAssemblies), SignAssemblies),
                SignDependencyAssemblies = BoundSwitch(nameof(SignDependencyAssemblies), SignDependencyAssemblies),
                SignPackages = BoundSwitch(nameof(SignPackages), SignPackages),
                NugetCredentialUserName = Normalize(NugetCredentialUserName),
                NugetCredentialSecret = NugetCredentialSecret,
                NugetCredentialSecretFilePath = Normalize(NugetCredentialSecretFilePath),
                NugetCredentialSecretEnvName = Normalize(NugetCredentialSecretEnvName),
                GitHubAccessToken = GitHubAccessToken,
                GitHubAccessTokenFilePath = Normalize(GitHubAccessTokenFilePath),
                GitHubAccessTokenEnvName = Normalize(GitHubAccessTokenEnvName),
                GitHubUsername = Normalize(GitHubUsername),
                GitHubRepositoryName = Normalize(GitHubRepositoryName),
                GitHubIsPreRelease = GitHubIsPreRelease.IsPresent,
                GitHubIncludeProjectNameInTag = BoundSwitchOrDefault(nameof(GitHubIncludeProjectNameInTag), GitHubIncludeProjectNameInTag, defaultValue: true),
                GitHubGenerateReleaseNotes = GitHubGenerateReleaseNotes.IsPresent,
                GitHubReleaseName = Normalize(GitHubReleaseName),
                GitHubTagName = Normalize(GitHubTagName),
                GitHubTagTemplate = Normalize(GitHubTagTemplate),
                GitHubReleaseMode = Normalize(GitHubReleaseMode),
                GitHubPrimaryProject = Normalize(GitHubPrimaryProject),
                GitHubTagConflictPolicy = Normalize(GitHubTagConflictPolicy),
                Options = PackageBuildConfiguration.ToObjectDictionary(Options)
            }
        });
    }

    private bool? BoundSwitch(string name, SwitchParameter value)
        => MyInvocation.BoundParameters.ContainsKey(name) ? value.IsPresent : null;

    private bool BoundSwitchOrDefault(string name, SwitchParameter value, bool defaultValue)
        => MyInvocation.BoundParameters.ContainsKey(name) ? value.IsPresent : defaultValue;

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}

/// <summary>
/// Creates repo-level release coordination settings for a module and package build.
/// </summary>
[Cmdlet(VerbsCommon.New, "ConfigurationRelease")]
[OutputType(typeof(ConfigurationReleaseSegment))]
public sealed class NewConfigurationReleaseCommand : PSCmdlet
{
    /// <summary>Staged release root where upload-ready assets should be copied.</summary>
    [Parameter] public string? StageRoot { get; set; } = Path.Combine("Artefacts", "UploadReady");

    /// <summary>Source used to resolve the coordinated release version.</summary>
    [Parameter] public ReleaseVersionSource VersionSource { get; set; } = ReleaseVersionSource.Module;

    /// <summary>Explicit release version used when <see cref="VersionSource"/> is <see cref="ReleaseVersionSource.Manual"/>.</summary>
    [Parameter] public string? Version { get; set; }

    /// <summary>Primary package/project used when the version source is package/project build.</summary>
    [Parameter] public string? PrimaryProject { get; set; }

    /// <summary>Preferred build order for high-level release lanes.</summary>
    [Parameter] public string[]? BuildOrder { get; set; }

    /// <summary>Preferred publish order for destinations such as NuGet, PowerShellGallery, and GitHub.</summary>
    [Parameter] public string[]? PublishOrder { get; set; }

    /// <summary>Emits a release coordination configuration segment.</summary>
    protected override void ProcessRecord()
    {
        WriteObject(new ConfigurationReleaseSegment
        {
            Configuration = new ReleaseConfiguration
            {
                StageRoot = Normalize(StageRoot),
                VersionSource = VersionSource,
                Version = Normalize(Version),
                PrimaryProject = Normalize(PrimaryProject),
                BuildOrder = BuildOrder,
                PublishOrder = PublishOrder
            }
        });
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
}
