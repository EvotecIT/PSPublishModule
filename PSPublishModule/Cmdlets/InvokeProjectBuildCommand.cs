using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Management.Automation;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using PowerForge;
using Spectre.Console;

namespace PSPublishModule;

/// <summary>
/// Executes a repository-wide .NET build/release pipeline from a JSON configuration.
/// </summary>
/// <remarks>
/// <para>
/// The configuration file follows <c>Schemas/project.build.schema.json</c> in the PSPublishModule repository.
/// Use it to define discovery rules, versioning, staging, NuGet publishing, and GitHub release settings.
/// </para>
/// <para>
/// GitHub tag/release templates support tokens:
/// <c>{Project}</c>, <c>{Version}</c>, <c>{PrimaryProject}</c>, <c>{PrimaryVersion}</c>, <c>{Repo}</c>,
/// <c>{Repository}</c>, <c>{Date}</c>, <c>{UtcDate}</c>, <c>{DateTime}</c>, <c>{UtcDateTime}</c>,
/// <c>{Timestamp}</c>, <c>{UtcTimestamp}</c>.
/// When GitHub release mode is Single and multiple project versions are present, <c>{Version}</c> defaults to
/// the local date (<c>yyyy.MM.dd</c>) unless a primary project version is available.
/// </para>
/// </remarks>
[Cmdlet(VerbsLifecycle.Invoke, "ProjectBuild", SupportsShouldProcess = true)]
[OutputType(typeof(ProjectBuildResult))]
public sealed partial class InvokeProjectBuildCommand : PSCmdlet
{
    /// <summary>Path to the JSON configuration file.</summary>
    [Parameter(Mandatory = true)]
    public string ConfigPath { get; set; } = string.Empty;

    /// <summary>Run version update step.</summary>
    [Parameter]
    public SwitchParameter UpdateVersions { get; set; }

    /// <summary>Run build/pack step.</summary>
    [Parameter]
    public SwitchParameter Build { get; set; }

    /// <summary>Publish packages to NuGet.</summary>
    [Parameter]
    public SwitchParameter PublishNuget { get; set; }

    /// <summary>Publish artifacts to GitHub.</summary>
    [Parameter]
    public SwitchParameter PublishGitHub { get; set; }

    /// <summary>Generate a plan only (no build/publish actions).</summary>
    [Parameter]
    public SwitchParameter Plan { get; set; }

    /// <summary>Optional path to write the plan JSON file.</summary>
    [Parameter]
    public string? PlanPath { get; set; }

    /// <summary>Executes the configured build pipeline.</summary>
    protected override void ProcessRecord()
    {
        var bound = MyInvocation?.BoundParameters;
        var isVerbose = bound?.ContainsKey("Verbose") == true;

        ConsoleEncoding.EnsureUtf8();
        try
        {
            if (!Console.IsOutputRedirected && !Console.IsErrorRedirected)
                AnsiConsole.Profile.Capabilities.Unicode = true;
        }
        catch
        {
            // best effort only
        }

        var interactive = SpectrePipelineConsoleUi.ShouldUseInteractiveView(isVerbose);
        ILogger logger = interactive
            ? new SpectreConsoleLogger { IsVerbose = isVerbose }
            : new CmdletLogger(this, isVerbose);

        var configFullPath = ResolveConfigPath(ConfigPath);
        var configDir = Path.GetDirectoryName(configFullPath) ?? SessionState.Path.CurrentFileSystemLocation.Path;
        var config = LoadConfig(configFullPath);

        object? planValue = null;
        object? updateValue = null;
        object? buildValue = null;
        object? publishNugetValue = null;
        object? publishGitHubValue = null;

        var planProvided = bound?.TryGetValue(nameof(Plan), out planValue) == true;
        var updateProvided = bound?.TryGetValue(nameof(UpdateVersions), out updateValue) == true;
        var buildProvided = bound?.TryGetValue(nameof(Build), out buildValue) == true;
        var publishNugetProvided = bound?.TryGetValue(nameof(PublishNuget), out publishNugetValue) == true;
        var publishGitHubProvided = bound?.TryGetValue(nameof(PublishGitHub), out publishGitHubValue) == true;

        bool planOnly = planProvided ? IsTrue(planValue) : (config.PlanOnly ?? false);
        bool updateVersions = updateProvided ? IsTrue(updateValue) : (config.UpdateVersions ?? false);
        bool build = buildProvided ? IsTrue(buildValue) : (config.Build ?? false);
        bool publishNuget = publishNugetProvided ? IsTrue(publishNugetValue) : (config.PublishNuget ?? false);
        bool publishGitHub = publishGitHubProvided ? IsTrue(publishGitHubValue) : (config.PublishGitHub ?? false);

        var anyConfigSpecified = config.UpdateVersions is not null || config.Build is not null ||
                                 config.PublishNuget is not null || config.PublishGitHub is not null;
        var anyProvided = updateProvided || buildProvided || publishNugetProvided || publishGitHubProvided;

        if (!anyConfigSpecified && !anyProvided)
        {
            updateVersions = true;
            build = true;
            publishNuget = true;
            publishGitHub = true;
        }

        if (!updateVersions && !build && !publishNuget && !publishGitHub)
        {
            WriteObject(new ProjectBuildResult
            {
                Success = false,
                ErrorMessage = "Nothing to do. Enable UpdateVersions, Build, PublishNuget, or PublishGitHub."
            });
            return;
        }

        var rootPath = ResolveOptionalPath(config.RootPath, configDir) ?? configDir;
        var stagingPath = ResolveOptionalPath(config.StagingPath, rootPath);
        var outputPath = ResolveOptionalPath(config.OutputPath, rootPath);
        var releaseZipOutputPath = ResolveOptionalPath(config.ReleaseZipOutputPath, rootPath);
        var planOutputPath = ResolveOptionalPath(PlanPath ?? config.PlanOutputPath, configDir);

        if (string.IsNullOrWhiteSpace(outputPath) && !string.IsNullOrWhiteSpace(stagingPath))
            outputPath = Path.Combine(stagingPath, "packages");
        if (string.IsNullOrWhiteSpace(releaseZipOutputPath) && !string.IsNullOrWhiteSpace(stagingPath))
            releaseZipOutputPath = Path.Combine(stagingPath, "releases");

        var nugetCredentialSecret = ResolveSecret(config.NugetCredentialSecret, config.NugetCredentialSecretFilePath, config.NugetCredentialSecretEnvName, configDir);
        var nugetUser = string.IsNullOrWhiteSpace(config.NugetCredentialUserName) ? null : config.NugetCredentialUserName!.Trim();
        var nugetCredential = (!string.IsNullOrWhiteSpace(nugetUser) || !string.IsNullOrWhiteSpace(nugetCredentialSecret))
            ? new RepositoryCredential
            {
                UserName = nugetUser,
                Secret = nugetCredentialSecret
            }
            : null;

        var publishApiKey = ResolveSecret(config.PublishApiKey, config.PublishApiKeyFilePath, config.PublishApiKeyEnvName, configDir);

        var store = ParseCertificateStore(config.CertificateStore);
        var createReleaseZip = config.CreateReleaseZip ?? true;
        var spec = new DotNetRepositoryReleaseSpec
        {
            RootPath = rootPath,
            ExpectedVersion = config.ExpectedVersion,
            ExpectedVersionsByProject = config.ExpectedVersionMap,
            ExpectedVersionMapAsInclude = config.ExpectedVersionMapAsInclude,
            ExpectedVersionMapUseWildcards = config.ExpectedVersionMapUseWildcards,
            IncludeProjects = config.IncludeProjects,
            ExcludeProjects = config.ExcludeProjects,
            ExcludeDirectories = config.ExcludeDirectories,
            VersionSources = config.NugetSource,
            VersionSourceCredential = nugetCredential,
            IncludePrerelease = config.IncludePrerelease,
            Configuration = string.IsNullOrWhiteSpace(config.Configuration) ? "Release" : config.Configuration!,
            OutputPath = outputPath,
            ReleaseZipOutputPath = releaseZipOutputPath,
            CertificateThumbprint = config.CertificateThumbprint,
            CertificateStore = store,
            TimeStampServer = config.TimeStampServer,
            Pack = build || publishNuget || publishGitHub,
            CreateReleaseZip = createReleaseZip,
            Publish = publishNuget,
            PublishSource = config.PublishSource,
            PublishApiKey = publishApiKey,
            SkipDuplicate = config.SkipDuplicate ?? true,
            PublishFailFast = config.PublishFailFast ?? true,
            UpdateVersions = updateVersions
        };

        var runner = new DotNetRepositoryReleaseService(logger);
        spec.WhatIf = true;
        var plan = runner.Execute(spec);

        if (planOnly || !ShouldProcess(rootPath, "Build project repository"))
        {
            TryWritePlan(plan, planOutputPath, logger);
            WriteObject(new ProjectBuildResult
            {
                Success = plan.Success,
                ErrorMessage = plan.ErrorMessage,
                Release = plan
            });
            return;
        }

        var preflightError = ValidatePreflight(publishNuget, publishGitHub, createReleaseZip, publishApiKey, config, configDir);
        if (!string.IsNullOrWhiteSpace(preflightError))
        {
            WriteObject(new ProjectBuildResult
            {
                Success = false,
                ErrorMessage = preflightError,
                Release = plan
            });
            return;
        }

        if (!string.IsNullOrWhiteSpace(stagingPath))
            PrepareStaging(stagingPath!, config.CleanStaging ?? false, logger);
        EnsureDirectory(outputPath);
        EnsureDirectory(releaseZipOutputPath);
        TryWritePlan(plan, planOutputPath, logger);

        spec.WhatIf = false;
        var release = runner.Execute(spec);
        var result = new ProjectBuildResult { Release = release };

        if (release is null || !release.Success)
        {
            result.Success = false;
            result.ErrorMessage = release?.ErrorMessage ?? "Release pipeline failed.";
            WriteObject(result);
            return;
        }

        if (!publishGitHub)
        {
            result.Success = true;
            WriteObject(result);
            return;
        }

        var gitHubToken = ResolveSecret(config.GitHubAccessToken, config.GitHubAccessTokenFilePath, config.GitHubAccessTokenEnvName, configDir);
        if (string.IsNullOrWhiteSpace(gitHubToken))
        {
            result.Success = false;
            result.ErrorMessage = "GitHub access token is required for GitHub publishing.";
            WriteObject(result);
            return;
        }

        if (string.IsNullOrWhiteSpace(config.GitHubUsername) || string.IsNullOrWhiteSpace(config.GitHubRepositoryName))
        {
            result.Success = false;
            result.ErrorMessage = "GitHubUsername and GitHubRepositoryName are required for GitHub publishing.";
            WriteObject(result);
            return;
        }

        var sb = ScriptBlock.Create(@"
param($u,$r,$t,$tag,$name,$asset,$pre,$gen)
Send-GitHubRelease -GitHubUsername $u -GitHubRepositoryName $r -GitHubAccessToken $t -TagName $tag -ReleaseName $name -AssetFilePaths $asset -IsPreRelease:$pre -GenerateReleaseNotes:$gen
");

        var releaseMode = string.IsNullOrWhiteSpace(config.GitHubReleaseMode)
            ? "Single"
            : config.GitHubReleaseMode!.Trim();
        var perProject = string.Equals(releaseMode, "PerProject", StringComparison.OrdinalIgnoreCase);
        var nowLocal = DateTime.Now;
        var nowUtc = DateTime.UtcNow;
        var dateToken = nowLocal.ToString("yyyy.MM.dd");
        var utcDateToken = nowUtc.ToString("yyyy.MM.dd");
        var dateTimeToken = nowLocal.ToString("yyyy.MM.dd.HHmmss");
        var utcDateTimeToken = nowUtc.ToString("yyyy.MM.dd.HHmmss");
        var timestampToken = nowLocal.ToString("yyyyMMddHHmmss");
        var utcTimestampToken = nowUtc.ToString("yyyyMMddHHmmss");
        var repoName = string.IsNullOrWhiteSpace(config.GitHubRepositoryName)
            ? "repository"
            : config.GitHubRepositoryName!.Trim();

        if (perProject)
        {
            foreach (var project in release.Projects)
            {
                if (!project.IsPackable) continue;
                var r = new ProjectBuildGitHubResult { ProjectName = project.ProjectName };

                if (string.IsNullOrWhiteSpace(project.NewVersion))
                {
                    r.Success = false;
                    r.ErrorMessage = "Missing project version for GitHub release.";
                    result.GitHub.Add(r);
                    if (spec.PublishFailFast)
                    {
                        result.Success = false;
                        result.ErrorMessage = r.ErrorMessage;
                        break;
                    }
                    continue;
                }

                if (string.IsNullOrWhiteSpace(project.ReleaseZipPath))
                {
                    r.Success = false;
                    r.ErrorMessage = "No release zip available for GitHub release.";
                    result.GitHub.Add(r);
                    if (spec.PublishFailFast)
                    {
                        result.Success = false;
                        result.ErrorMessage = r.ErrorMessage;
                        break;
                    }
                    continue;
                }

                if (!File.Exists(project.ReleaseZipPath))
                {
                    r.Success = false;
                    r.ErrorMessage = $"Release zip not found: {project.ReleaseZipPath}";
                    result.GitHub.Add(r);
                    if (spec.PublishFailFast)
                    {
                        result.Success = false;
                        result.ErrorMessage = r.ErrorMessage;
                        break;
                    }
                    continue;
                }

                var tag = string.IsNullOrWhiteSpace(config.GitHubTagName)
                    ? (config.GitHubIncludeProjectNameInTag == false ? $"v{project.NewVersion}" : $"{project.ProjectName}-v{project.NewVersion}")
                    : config.GitHubTagName!;
                if (!string.IsNullOrWhiteSpace(config.GitHubTagTemplate))
                {
                    tag = ApplyTemplate(
                        config.GitHubTagTemplate!,
                        project.ProjectName,
                        project.NewVersion ?? project.OldVersion ?? string.Empty,
                        config.GitHubPrimaryProject ?? project.ProjectName,
                        project.NewVersion ?? project.OldVersion ?? string.Empty,
                        repoName,
                        dateToken,
                        utcDateToken,
                        dateTimeToken,
                        utcDateTimeToken,
                        timestampToken,
                        utcTimestampToken);
                }

                var releaseName = string.IsNullOrWhiteSpace(config.GitHubReleaseName)
                    ? tag
                    : ApplyTemplate(
                        config.GitHubReleaseName!,
                        project.ProjectName,
                        project.NewVersion ?? project.OldVersion ?? string.Empty,
                        config.GitHubPrimaryProject ?? project.ProjectName,
                        project.NewVersion ?? project.OldVersion ?? string.Empty,
                        repoName,
                        dateToken,
                        utcDateToken,
                        dateTimeToken,
                        utcDateTimeToken,
                        timestampToken,
                        utcTimestampToken);

                var output = sb.Invoke(config.GitHubUsername, config.GitHubRepositoryName, gitHubToken, tag, releaseName, new[] { project.ReleaseZipPath }, config.GitHubIsPreRelease, config.GitHubGenerateReleaseNotes);
                var status = output.Count > 0 ? output[0]?.BaseObject : null;

                bool ok = false;
                string? releaseUrl = null;
                string? errorMessage = null;

                if (status is SendGitHubReleaseCommand.GitHubReleaseResult gr)
                {
                    ok = gr.Succeeded;
                    releaseUrl = gr.ReleaseUrl;
                    errorMessage = gr.Succeeded ? null : gr.ErrorMessage;
                }
                else if (status is PSObject pso)
                {
                    var succeeded = pso.Properties["Succeeded"]?.Value as bool?;
                    ok = succeeded ?? false;
                    releaseUrl = pso.Properties["ReleaseUrl"]?.Value?.ToString();
                    errorMessage = pso.Properties["ErrorMessage"]?.Value?.ToString();
                }
                else
                {
                    errorMessage = "Unexpected result from Send-GitHubRelease.";
                }

                r.Success = ok;
                r.TagName = tag;
                r.ReleaseUrl = releaseUrl;
                r.ErrorMessage = errorMessage;
                result.GitHub.Add(r);

                if (!ok)
                {
                    result.Success = false;
                    result.ErrorMessage = errorMessage ?? "GitHub publish failed.";
                    if (spec.PublishFailFast)
                        break;
                }
            }
        }
        else
        {
            var assets = new List<string>();
            foreach (var project in release.Projects)
            {
                if (!project.IsPackable) continue;
                var zip = project.ReleaseZipPath;
                if (string.IsNullOrWhiteSpace(zip))
                    continue;
                if (File.Exists(zip))
                    assets.Add(zip!);
            }

            var distinctAssets = assets.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (distinctAssets.Length == 0)
            {
                result.Success = false;
                result.ErrorMessage = "No release zips available for GitHub release.";
                WriteObject(result);
                return;
            }

            var baseVersion = ResolveGitHubBaseVersion(config, release);
            var tagVersionToken = string.IsNullOrWhiteSpace(baseVersion) ? dateToken : baseVersion!;

            var tag = !string.IsNullOrWhiteSpace(config.GitHubTagName)
                ? config.GitHubTagName!
                : (!string.IsNullOrWhiteSpace(config.GitHubTagTemplate)
                    ? ApplyTemplate(
                        config.GitHubTagTemplate!,
                        repoName,
                        tagVersionToken,
                        config.GitHubPrimaryProject ?? repoName,
                        tagVersionToken,
                        repoName,
                        dateToken,
                        utcDateToken,
                        dateTimeToken,
                        utcDateTimeToken,
                        timestampToken,
                        utcTimestampToken)
                    : $"v{tagVersionToken}");

            var releaseName = string.IsNullOrWhiteSpace(config.GitHubReleaseName)
                ? tag
                : ApplyTemplate(
                    config.GitHubReleaseName!,
                    repoName,
                    tagVersionToken,
                    config.GitHubPrimaryProject ?? repoName,
                    tagVersionToken,
                    repoName,
                    dateToken,
                    utcDateToken,
                    dateTimeToken,
                    utcDateTimeToken,
                    timestampToken,
                    utcTimestampToken);

            var output = sb.Invoke(config.GitHubUsername, config.GitHubRepositoryName, gitHubToken, tag, releaseName, distinctAssets, config.GitHubIsPreRelease, config.GitHubGenerateReleaseNotes);
            var status = output.Count > 0 ? output[0]?.BaseObject : null;

            bool ok = false;
            string? releaseUrl = null;
            string? errorMessage = null;

            if (status is SendGitHubReleaseCommand.GitHubReleaseResult gr)
            {
                ok = gr.Succeeded;
                releaseUrl = gr.ReleaseUrl;
                errorMessage = gr.Succeeded ? null : gr.ErrorMessage;
            }
            else if (status is PSObject pso)
            {
                var succeeded = pso.Properties["Succeeded"]?.Value as bool?;
                ok = succeeded ?? false;
                releaseUrl = pso.Properties["ReleaseUrl"]?.Value?.ToString();
                errorMessage = pso.Properties["ErrorMessage"]?.Value?.ToString();
            }
            else
            {
                errorMessage = "Unexpected result from Send-GitHubRelease.";
            }

            foreach (var project in release.Projects.Where(p => p.IsPackable))
            {
                result.GitHub.Add(new ProjectBuildGitHubResult
                {
                    ProjectName = project.ProjectName,
                    Success = ok,
                    TagName = tag,
                    ReleaseUrl = releaseUrl,
                    ErrorMessage = ok ? null : errorMessage
                });
            }

            result.Success = ok;
            result.ErrorMessage = ok ? null : (errorMessage ?? "GitHub publish failed.");
            if (interactive)
                WriteGitHubSummary(perProject, tag, releaseUrl, distinctAssets.Length, result.GitHub);
        }

        if (result.ErrorMessage is null)
            result.Success = result.GitHub.Count == 0 || result.GitHub.TrueForAll(g => g.Success);

        WriteObject(result);
    }
}

/// <summary>Aggregate result for project builds.</summary>
public sealed class ProjectBuildResult
{
    /// <summary>True when the pipeline completed successfully.</summary>
    public bool Success { get; set; }
    /// <summary>Error message when <see cref="Success"/> is false.</summary>
    public string? ErrorMessage { get; set; }
    /// <summary>Release pipeline result.</summary>
    public DotNetRepositoryReleaseResult? Release { get; set; }
    /// <summary>GitHub publishing results.</summary>
    public List<ProjectBuildGitHubResult> GitHub { get; } = new();
}

/// <summary>GitHub publish result per project.</summary>
public sealed class ProjectBuildGitHubResult
{
    /// <summary>Project name.</summary>
    public string ProjectName { get; set; } = string.Empty;
    /// <summary>True when publishing succeeded.</summary>
    public bool Success { get; set; }
    /// <summary>Computed tag name.</summary>
    public string? TagName { get; set; }
    /// <summary>Release URL when publishing succeeded.</summary>
    public string? ReleaseUrl { get; set; }
    /// <summary>Error message when publishing failed.</summary>
    public string? ErrorMessage { get; set; }
}
