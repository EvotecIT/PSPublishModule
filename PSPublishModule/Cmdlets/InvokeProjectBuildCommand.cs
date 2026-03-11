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
        var support = new ProjectBuildSupportService(logger);

        var configFullPath = ResolveConfigPath(ConfigPath);
        var configDir = Path.GetDirectoryName(configFullPath) ?? SessionState.Path.CurrentFileSystemLocation.Path;
        var config = support.LoadConfig(configFullPath);

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

        bool planOnly = planProvided ? ProjectBuildSupportService.IsTrue(planValue) : (config.PlanOnly ?? false);
        bool updateVersions = updateProvided ? ProjectBuildSupportService.IsTrue(updateValue) : (config.UpdateVersions ?? false);
        bool build = buildProvided ? ProjectBuildSupportService.IsTrue(buildValue) : (config.Build ?? false);
        bool publishNuget = publishNugetProvided ? ProjectBuildSupportService.IsTrue(publishNugetValue) : (config.PublishNuget ?? false);
        bool publishGitHub = publishGitHubProvided ? ProjectBuildSupportService.IsTrue(publishGitHubValue) : (config.PublishGitHub ?? false);

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

        var rootPath = ProjectBuildSupportService.ResolveOptionalPath(config.RootPath, configDir) ?? configDir;
        var stagingPath = ProjectBuildSupportService.ResolveOptionalPath(config.StagingPath, rootPath);
        var outputPath = ProjectBuildSupportService.ResolveOptionalPath(config.OutputPath, rootPath);
        var releaseZipOutputPath = ProjectBuildSupportService.ResolveOptionalPath(config.ReleaseZipOutputPath, rootPath);
        var planOutputPath = ProjectBuildSupportService.ResolveOptionalPath(PlanPath ?? config.PlanOutputPath, configDir);

        if (string.IsNullOrWhiteSpace(outputPath) && !string.IsNullOrWhiteSpace(stagingPath))
            outputPath = Path.Combine(stagingPath, "packages");
        if (string.IsNullOrWhiteSpace(releaseZipOutputPath) && !string.IsNullOrWhiteSpace(stagingPath))
            releaseZipOutputPath = Path.Combine(stagingPath, "releases");

        var nugetCredentialSecret = ProjectBuildSupportService.ResolveSecret(config.NugetCredentialSecret, config.NugetCredentialSecretFilePath, config.NugetCredentialSecretEnvName, configDir);
        var nugetUser = string.IsNullOrWhiteSpace(config.NugetCredentialUserName) ? null : config.NugetCredentialUserName!.Trim();
        var nugetCredential = (!string.IsNullOrWhiteSpace(nugetUser) || !string.IsNullOrWhiteSpace(nugetCredentialSecret))
            ? new RepositoryCredential
            {
                UserName = nugetUser,
                Secret = nugetCredentialSecret
            }
            : null;

        var publishApiKey = ProjectBuildSupportService.ResolveSecret(config.PublishApiKey, config.PublishApiKeyFilePath, config.PublishApiKeyEnvName, configDir);

        var store = ProjectBuildSupportService.ParseCertificateStore(config.CertificateStore);
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
        var preflightErrors = new List<string>();
        if (!plan.Success)
            preflightErrors.Add(plan.ErrorMessage ?? "Plan/preflight validation failed.");

        if (planOnly || !ShouldProcess(rootPath, "Build project repository"))
        {
            support.TryWritePlan(plan, planOutputPath);
            WriteObject(new ProjectBuildResult
            {
                Success = preflightErrors.Count == 0,
                ErrorMessage = preflightErrors.Count == 0 ? null : string.Join(Environment.NewLine, preflightErrors),
                Release = plan
            });
            return;
        }

        var preflightError = support.ValidatePreflight(publishNuget, publishGitHub, createReleaseZip, publishApiKey, config, configDir);
        if (!string.IsNullOrWhiteSpace(preflightError))
            preflightErrors.Add(preflightError!);

        var gitHubToken = publishGitHub
            ? ProjectBuildSupportService.ResolveSecret(config.GitHubAccessToken, config.GitHubAccessTokenFilePath, config.GitHubAccessTokenEnvName, configDir)
            : null;
        if (publishGitHub && string.IsNullOrWhiteSpace(preflightError))
        {
            var gitHubPreflightError = ValidateGitHubPublishPreflight(config, plan, gitHubToken!, logger);
            if (!string.IsNullOrWhiteSpace(gitHubPreflightError))
                preflightErrors.Add(gitHubPreflightError!);
        }

        if (preflightErrors.Count > 0)
        {
            WriteObject(new ProjectBuildResult
            {
                Success = false,
                ErrorMessage = string.Join(Environment.NewLine, preflightErrors),
                Release = plan
            });
            return;
        }

        if (!string.IsNullOrWhiteSpace(stagingPath))
            support.PrepareStaging(stagingPath!, config.CleanStaging ?? false);
        ProjectBuildSupportService.EnsureDirectory(outputPath);
        ProjectBuildSupportService.EnsureDirectory(releaseZipOutputPath);
        support.TryWritePlan(plan, planOutputPath);

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

        gitHubToken ??= ProjectBuildSupportService.ResolveSecret(config.GitHubAccessToken, config.GitHubAccessTokenFilePath, config.GitHubAccessTokenEnvName, configDir);
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

        var resolvedGitHubToken = gitHubToken!;
        var publisher = new ProjectBuildGitHubPublisher(logger);
        var publishSummary = publisher.Publish(new ProjectBuildGitHubPublishRequest
        {
            Owner = config.GitHubUsername!,
            Repository = config.GitHubRepositoryName!,
            Token = resolvedGitHubToken,
            Release = release,
            ReleaseMode = config.GitHubReleaseMode ?? "Single",
            IncludeProjectNameInTag = config.GitHubIncludeProjectNameInTag,
            IsPreRelease = config.GitHubIsPreRelease,
            GenerateReleaseNotes = config.GitHubGenerateReleaseNotes,
            PublishFailFast = spec.PublishFailFast,
            ReleaseName = config.GitHubReleaseName,
            TagName = config.GitHubTagName,
            TagTemplate = config.GitHubTagTemplate,
            PrimaryProject = config.GitHubPrimaryProject,
            TagConflictPolicy = config.GitHubTagConflictPolicy
        });

        result.GitHub.AddRange(publishSummary.Results);
        result.Success = publishSummary.Success;
        result.ErrorMessage = publishSummary.ErrorMessage;
        if (interactive && !publishSummary.PerProject)
            WriteGitHubSummary(false, publishSummary.SummaryTag, publishSummary.SummaryReleaseUrl, publishSummary.SummaryAssetsCount, result.GitHub);

        if (result.ErrorMessage is null)
            result.Success = result.GitHub.Count == 0 || result.GitHub.TrueForAll(g => g.Success);

        WriteObject(result);
    }
}
