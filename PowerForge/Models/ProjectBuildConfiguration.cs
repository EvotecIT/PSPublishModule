using System.Collections.Generic;

namespace PowerForge;

/// <summary>
/// Deserialized configuration used by <c>Invoke-ProjectBuild</c>.
/// </summary>
internal sealed class ProjectBuildConfiguration
{
    public string? RootPath { get; set; }
    public string? ExpectedVersion { get; set; }
    public Dictionary<string, string>? ExpectedVersionMap { get; set; }
    public Dictionary<string, ProjectBuildVersionTrack>? VersionTracks { get; set; }
    public bool ExpectedVersionMapAsInclude { get; set; }
    public bool ExpectedVersionMapUseWildcards { get; set; }
    public string[]? IncludeProjects { get; set; }
    public string[]? ExcludeProjects { get; set; }
    public string[]? ExcludeDirectories { get; set; }
    public string[]? NugetSource { get; set; }
    public bool IncludePrerelease { get; set; }
    public string? Configuration { get; set; }
    public string? OutputPath { get; set; }
    public string? ReleaseZipOutputPath { get; set; }
    public string? StagingPath { get; set; }
    public bool? CleanStaging { get; set; }
    public bool? PlanOnly { get; set; }
    public string? PlanOutputPath { get; set; }
    public bool? UpdateVersions { get; set; }
    public bool? Build { get; set; }
    public bool? PublishNuget { get; set; }
    public bool? PublishGitHub { get; set; }
    public bool? CreateReleaseZip { get; set; }
    public string? PublishSource { get; set; }
    public string? PublishApiKey { get; set; }
    public string? PublishApiKeyFilePath { get; set; }
    public string? PublishApiKeyEnvName { get; set; }
    public bool? SkipDuplicate { get; set; }
    public bool? PublishFailFast { get; set; }
    public string? CertificateThumbprint { get; set; }
    public string? CertificateStore { get; set; }
    public string? TimeStampServer { get; set; }
    public string? NugetCredentialUserName { get; set; }
    public string? NugetCredentialSecret { get; set; }
    public string? NugetCredentialSecretFilePath { get; set; }
    public string? NugetCredentialSecretEnvName { get; set; }
    public string? GitHubAccessToken { get; set; }
    public string? GitHubAccessTokenFilePath { get; set; }
    public string? GitHubAccessTokenEnvName { get; set; }
    public string? GitHubUsername { get; set; }
    public string? GitHubRepositoryName { get; set; }
    public bool GitHubIsPreRelease { get; set; }
    public bool GitHubIncludeProjectNameInTag { get; set; } = true;
    public bool GitHubGenerateReleaseNotes { get; set; }
    public string? GitHubReleaseName { get; set; }
    public string? GitHubTagName { get; set; }
    public string? GitHubTagTemplate { get; set; }
    public string? GitHubReleaseMode { get; set; }
    public string? GitHubPrimaryProject { get; set; }
    public string? GitHubTagConflictPolicy { get; set; }
}
