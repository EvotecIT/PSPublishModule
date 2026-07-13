using System.Collections;
using System.Collections.Generic;

namespace PowerForge;

/// <summary>
/// Configuration segment that points at an existing project-build JSON configuration.
/// </summary>
public sealed class ConfigurationProjectBuildSegment : IConfigurationSegment
{
    /// <inheritdoc />
    public string Type => "ProjectBuild";

    /// <summary>Project-build configuration reference payload.</summary>
    public ProjectBuildConfigurationReference Configuration { get; set; } = new();
}

/// <summary>
/// Reference to an existing <c>project.build.json</c> file from the module-build DSL.
/// </summary>
public sealed class ProjectBuildConfigurationReference
{
    /// <summary>Optional friendly name for this package build lane.</summary>
    public string? Name { get; set; }

    /// <summary>Path to an existing <c>project.build.json</c> file.</summary>
    public string ConfigPath { get; set; } = string.Empty;

    /// <summary>Whether this project build lane is enabled. Defaults to true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Whether package build outputs must be produced before the module lane runs.</summary>
    public bool BuildBeforeModule { get; set; }

    /// <summary>Whether the resolved package version should be used as the unified release version source.</summary>
    public bool UseAsReleaseVersionSource { get; set; }

    /// <summary>Whether package outputs should be exposed as a temporary local NuGet feed for the module lane.</summary>
    public bool ProvideLocalNuGetFeed { get; set; }

    /// <summary>Whether project/package versions should be updated, overriding the referenced JSON when set.</summary>
    public bool? UpdateVersions { get; set; }

    /// <summary>Whether package projects should be built/packed, overriding the referenced JSON when set.</summary>
    public bool? Build { get; set; }

    /// <summary>Whether portable symbol packages should be created, overriding the referenced JSON when set.</summary>
    public bool? IncludeSymbols { get; set; }

    /// <summary>Whether NuGet packages should be published, overriding the referenced JSON when set.</summary>
    public bool? PublishNuget { get; set; }

    /// <summary>Whether package GitHub release publishing should be enabled, overriding the referenced JSON when set.</summary>
    public bool? PublishGitHub { get; set; }

    /// <summary>Whether release ZIPs should be created, overriding the referenced JSON when set.</summary>
    public bool? CreateReleaseZip { get; set; }

    /// <summary>Whether assemblies should be signed before packages are created, overriding the referenced JSON when set.</summary>
    public bool? SignAssemblies { get; set; }

    /// <summary>Whether copied dependency assemblies should also be signed, overriding the referenced JSON when set.</summary>
    public bool? SignDependencyAssemblies { get; set; }

    /// <summary>Whether generated NuGet packages should be signed, overriding the referenced JSON when set.</summary>
    public bool? SignPackages { get; set; }

    /// <summary>Additional project-build JSON overrides for less common fields.</summary>
    public Dictionary<string, object?>? Options { get; set; }
}

/// <summary>
/// Configuration segment that declares an inline NuGet package build lane from the module-build DSL.
/// </summary>
public sealed class ConfigurationPackageBuildSegment : IConfigurationSegment
{
    /// <inheritdoc />
    public string Type => "PackageBuild";

    /// <summary>Inline package build configuration payload.</summary>
    public PackageBuildConfiguration Configuration { get; set; } = new();
}

/// <summary>
/// Public module-build DSL shape for repository package build settings.
/// </summary>
public sealed class PackageBuildConfiguration
{
    /// <summary>Optional friendly name for this package build lane.</summary>
    public string? Name { get; set; }

    /// <summary>Whether this package build lane is enabled. Defaults to true.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Whether package build outputs must be produced before the module lane runs.</summary>
    public bool BuildBeforeModule { get; set; }

    /// <summary>Whether the resolved package version should be used as the unified release version source.</summary>
    public bool UseAsReleaseVersionSource { get; set; }

    /// <summary>Whether package outputs should be exposed as a temporary local NuGet feed for the module lane.</summary>
    public bool ProvideLocalNuGetFeed { get; set; }

    /// <summary>Root path used for project discovery.</summary>
    public string? RootPath { get; set; }

    /// <summary>Global expected package version or X-pattern.</summary>
    public string? ExpectedVersion { get; set; }

    /// <summary>Per-project expected package version map.</summary>
    public Dictionary<string, string>? ExpectedVersionMap { get; set; }

    /// <summary>Shared version tracks keyed by track name.</summary>
    public Dictionary<string, PackageBuildVersionTrackConfiguration>? VersionTracks { get; set; }

    /// <summary>When true, <see cref="ExpectedVersionMap"/> acts as an include list.</summary>
    public bool ExpectedVersionMapAsInclude { get; set; }

    /// <summary>When true, <see cref="ExpectedVersionMap"/> keys support wildcard matching.</summary>
    public bool ExpectedVersionMapUseWildcards { get; set; }

    /// <summary>Project names to include.</summary>
    public string[]? IncludeProjects { get; set; }

    /// <summary>Project names to exclude.</summary>
    public string[]? ExcludeProjects { get; set; }

    /// <summary>Directory names to exclude from project discovery.</summary>
    public string[]? ExcludeDirectories { get; set; }

    /// <summary>NuGet sources used for version lookup.</summary>
    public string[]? NugetSource { get; set; }

    /// <summary>Whether prerelease versions can be considered during version lookup.</summary>
    public bool IncludePrerelease { get; set; }

    /// <summary>Build configuration, usually Release or Debug.</summary>
    public string? Configuration { get; set; }

    /// <summary>Package output path override.</summary>
    public string? OutputPath { get; set; }

    /// <summary>Release ZIP output path override.</summary>
    public string? ReleaseZipOutputPath { get; set; }

    /// <summary>Staging root for project-build outputs.</summary>
    public string? StagingPath { get; set; }

    /// <summary>Whether to clean staging before the package build runs.</summary>
    public bool? CleanStaging { get; set; }

    /// <summary>Whether to produce a plan without executing package build steps.</summary>
    public bool? PlanOnly { get; set; }

    /// <summary>Plan output path.</summary>
    public string? PlanOutputPath { get; set; }

    /// <summary>Whether project/package versions should be updated.</summary>
    public bool? UpdateVersions { get; set; }

    /// <summary>Whether package projects should be built/packed.</summary>
    public bool? Build { get; set; }

    /// <summary>Pack strategy, for example PerProject or MSBuild.</summary>
    public string? PackStrategy { get; set; }

    /// <summary>Whether portable <c>.snupkg</c> symbol packages should be created.</summary>
    public bool? IncludeSymbols { get; set; }

    /// <summary>Whether NuGet packages should be published.</summary>
    public bool? PublishNuget { get; set; }

    /// <summary>Whether package GitHub release publishing should be enabled.</summary>
    public bool? PublishGitHub { get; set; }

    /// <summary>Whether release ZIPs should be created for package projects.</summary>
    public bool? CreateReleaseZip { get; set; }

    /// <summary>Whether GitHub Packages should be used as the NuGet version lookup and publish feed.</summary>
    public bool UseGitHubPackages { get; set; }

    /// <summary>GitHub user or organization that owns the GitHub Packages NuGet feed.</summary>
    public string? GitHubPackagesOwner { get; set; }

    /// <summary>NuGet publish source.</summary>
    public string? PublishSource { get; set; }

    /// <summary>Inline NuGet publish API key. Prefer file or environment forms for automation.</summary>
    public string? PublishApiKey { get; set; }

    /// <summary>Path to a file containing the NuGet publish API key.</summary>
    public string? PublishApiKeyFilePath { get; set; }

    /// <summary>Environment variable containing the NuGet publish API key.</summary>
    public string? PublishApiKeyEnvName { get; set; }

    /// <summary>Whether duplicate NuGet packages should be skipped during push.</summary>
    public bool? SkipDuplicate { get; set; }

    /// <summary>Whether package publishing should stop on first failure.</summary>
    public bool? PublishFailFast { get; set; }

    /// <summary>Code signing certificate thumbprint for package signing.</summary>
    public string? CertificateThumbprint { get; set; }

    /// <summary>Certificate store location for package signing.</summary>
    public string? CertificateStore { get; set; }

    /// <summary>Timestamp server URL for package signing.</summary>
    public string? TimeStampServer { get; set; }

    /// <summary>Whether assemblies should be signed before packages are created.</summary>
    public bool? SignAssemblies { get; set; }

    /// <summary>Whether copied dependency assemblies should also be signed.</summary>
    public bool? SignDependencyAssemblies { get; set; }

    /// <summary>Whether generated NuGet packages should be signed.</summary>
    public bool? SignPackages { get; set; }

    /// <summary>NuGet version lookup credential user name.</summary>
    public string? NugetCredentialUserName { get; set; }

    /// <summary>NuGet version lookup credential secret.</summary>
    public string? NugetCredentialSecret { get; set; }

    /// <summary>Path to a file containing the NuGet version lookup credential secret.</summary>
    public string? NugetCredentialSecretFilePath { get; set; }

    /// <summary>Environment variable containing the NuGet version lookup credential secret.</summary>
    public string? NugetCredentialSecretEnvName { get; set; }

    /// <summary>Inline GitHub access token. Prefer file or environment forms for automation.</summary>
    public string? GitHubAccessToken { get; set; }

    /// <summary>Path to a file containing the GitHub access token.</summary>
    public string? GitHubAccessTokenFilePath { get; set; }

    /// <summary>Environment variable containing the GitHub access token.</summary>
    public string? GitHubAccessTokenEnvName { get; set; }

    /// <summary>GitHub owner/user name.</summary>
    public string? GitHubUsername { get; set; }

    /// <summary>GitHub repository name.</summary>
    public string? GitHubRepositoryName { get; set; }

    /// <summary>Whether GitHub releases should be marked prerelease.</summary>
    public bool GitHubIsPreRelease { get; set; }

    /// <summary>Whether project name should be included in generated package GitHub tags.</summary>
    public bool GitHubIncludeProjectNameInTag { get; set; } = true;

    /// <summary>Whether GitHub should generate release notes.</summary>
    public bool GitHubGenerateReleaseNotes { get; set; }

    /// <summary>GitHub release name template or override.</summary>
    public string? GitHubReleaseName { get; set; }

    /// <summary>GitHub tag name override.</summary>
    public string? GitHubTagName { get; set; }

    /// <summary>GitHub tag template.</summary>
    public string? GitHubTagTemplate { get; set; }

    /// <summary>GitHub release mode, for example Single or PerProject.</summary>
    public string? GitHubReleaseMode { get; set; }

    /// <summary>Primary project used for single-release version resolution.</summary>
    public string? GitHubPrimaryProject { get; set; }

    /// <summary>GitHub tag conflict policy.</summary>
    public string? GitHubTagConflictPolicy { get; set; }

    /// <summary>Additional project-build options for fields not yet modeled as first-class parameters.</summary>
    public Dictionary<string, object?>? Options { get; set; }

    /// <summary>Converts a PowerShell dictionary into a string map.</summary>
    public static Dictionary<string, string>? ToStringDictionary(IDictionary? source)
    {
        if (source is null)
            return null;

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in source)
        {
            var key = entry.Key?.ToString();
            if (string.IsNullOrWhiteSpace(key))
                continue;
            result[key!.Trim()] = entry.Value?.ToString() ?? string.Empty;
        }

        return result.Count == 0 ? null : result;
    }

    /// <summary>Converts a PowerShell dictionary into a loose options map.</summary>
    public static Dictionary<string, object?>? ToObjectDictionary(IDictionary? source)
    {
        if (source is null)
            return null;

        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in source)
        {
            var key = entry.Key?.ToString();
            if (string.IsNullOrWhiteSpace(key))
                continue;
            result[key!.Trim()] = entry.Value;
        }

        return result.Count == 0 ? null : result;
    }

    /// <summary>Converts a PowerShell dictionary into version-track configuration entries.</summary>
    public static Dictionary<string, PackageBuildVersionTrackConfiguration>? ToVersionTracksDictionary(IDictionary? source)
    {
        if (source is null)
            return null;

        var result = new Dictionary<string, PackageBuildVersionTrackConfiguration>(StringComparer.OrdinalIgnoreCase);
        foreach (DictionaryEntry entry in source)
        {
            var key = entry.Key?.ToString();
            if (string.IsNullOrWhiteSpace(key))
                continue;

            result[key!.Trim()] = ConvertVersionTrack(entry.Value);
        }

        return result.Count == 0 ? null : result;
    }

    private static PackageBuildVersionTrackConfiguration ConvertVersionTrack(object? value)
    {
        if (value is PackageBuildVersionTrackConfiguration typed)
            return typed;

        if (value is IDictionary dictionary)
        {
            return new PackageBuildVersionTrackConfiguration
            {
                ExpectedVersion = GetString(dictionary, nameof(PackageBuildVersionTrackConfiguration.ExpectedVersion)),
                AnchorProject = GetString(dictionary, nameof(PackageBuildVersionTrackConfiguration.AnchorProject)),
                AnchorPackageId = GetString(dictionary, nameof(PackageBuildVersionTrackConfiguration.AnchorPackageId)),
                Projects = GetStringArray(dictionary, nameof(PackageBuildVersionTrackConfiguration.Projects)),
                NugetSource = GetStringArray(dictionary, nameof(PackageBuildVersionTrackConfiguration.NugetSource)),
                IncludePrerelease = GetNullableBool(dictionary, nameof(PackageBuildVersionTrackConfiguration.IncludePrerelease))
            };
        }

        return new PackageBuildVersionTrackConfiguration
        {
            ExpectedVersion = value?.ToString()
        };
    }

    private static object? GetValue(IDictionary dictionary, string key)
    {
        foreach (DictionaryEntry entry in dictionary)
        {
            if (string.Equals(entry.Key?.ToString(), key, StringComparison.OrdinalIgnoreCase))
                return entry.Value;
        }

        return null;
    }

    private static string? GetString(IDictionary dictionary, string key)
        => GetValue(dictionary, key)?.ToString();

    private static string[]? GetStringArray(IDictionary dictionary, string key)
    {
        var value = GetValue(dictionary, key);
        if (value is null)
            return null;

        if (value is string text)
            return string.IsNullOrWhiteSpace(text) ? null : new[] { text };

        if (value is IEnumerable enumerable)
        {
            var values = new List<string>();
            foreach (var item in enumerable)
            {
                var itemText = item?.ToString();
                if (!string.IsNullOrWhiteSpace(itemText))
                    values.Add(itemText!.Trim());
            }

            return values.Count == 0 ? null : values.ToArray();
        }

        return new[] { value.ToString() ?? string.Empty };
    }

    private static bool? GetNullableBool(IDictionary dictionary, string key)
    {
        var value = GetValue(dictionary, key);
        if (value is null)
            return null;

        if (value is bool boolean)
            return boolean;

        return bool.TryParse(value.ToString(), out var parsed) ? parsed : null;
    }
}

/// <summary>
/// Public version-track shape for package build configuration authored from PowerShell.
/// </summary>
public sealed class PackageBuildVersionTrackConfiguration
{
    /// <summary>Expected version or X-pattern for this track.</summary>
    public string? ExpectedVersion { get; set; }

    /// <summary>Anchor project whose version is used for the track.</summary>
    public string? AnchorProject { get; set; }

    /// <summary>Anchor package ID when it differs from the project name.</summary>
    public string? AnchorPackageId { get; set; }

    /// <summary>Projects that participate in this version track.</summary>
    public string[]? Projects { get; set; }

    /// <summary>NuGet sources used for this track's version lookup.</summary>
    public string[]? NugetSource { get; set; }

    /// <summary>Whether prerelease versions can be considered for this track.</summary>
    public bool? IncludePrerelease { get; set; }
}

/// <summary>
/// Configuration segment for repo-level release coordination from the module-build DSL.
/// </summary>
public sealed class ConfigurationReleaseSegment : IConfigurationSegment
{
    /// <inheritdoc />
    public string Type => "Release";

    /// <summary>Release coordination configuration payload.</summary>
    public ReleaseConfiguration Configuration { get; set; } = new();
}

/// <summary>
/// Repo-level release coordination options.
/// </summary>
public sealed class ReleaseConfiguration
{
    /// <summary>Staged release root where upload-ready assets should be copied.</summary>
    public string? StageRoot { get; set; }

    /// <summary>Source used to resolve the coordinated release version.</summary>
    public ReleaseVersionSource VersionSource { get; set; } = ReleaseVersionSource.Module;

    /// <summary>Primary package/project used when the version source is package/project build.</summary>
    public string? PrimaryProject { get; set; }

    /// <summary>Explicit release version used when <see cref="VersionSource"/> is <see cref="ReleaseVersionSource.Manual"/>.</summary>
    public string? Version { get; set; }

    /// <summary>Preferred build order for high-level release lanes.</summary>
    public string[]? BuildOrder { get; set; }

    /// <summary>Preferred publish order for destinations such as NuGet, PowerShellGallery, and GitHub.</summary>
    public string[]? PublishOrder { get; set; }

}

/// <summary>
/// Source used to resolve the coordinated release version.
/// </summary>
public enum ReleaseVersionSource
{
    /// <summary>Use the resolved module version.</summary>
    Module,

    /// <summary>Use the resolved project-build package version.</summary>
    ProjectBuild,

    /// <summary>Use the resolved inline package-build version.</summary>
    PackageBuild,

    /// <summary>Use an explicitly supplied release version.</summary>
    Manual
}
