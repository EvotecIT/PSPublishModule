using System;
using System.Collections.Generic;
using System.IO;
using PowerForge;
using System.Management.Automation;
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
        var preparation = new ProjectBuildPreparationService().Prepare(
            config,
            configDir,
            PlanPath,
            new ProjectBuildRequestedActions
            {
                PlanOnly = ResolveRequestedAction(bound, nameof(Plan)),
                UpdateVersions = ResolveRequestedAction(bound, nameof(UpdateVersions)),
                Build = ResolveRequestedAction(bound, nameof(Build)),
                PublishNuget = ResolveRequestedAction(bound, nameof(PublishNuget)),
                PublishGitHub = ResolveRequestedAction(bound, nameof(PublishGitHub))
            });

        if (!preparation.HasWork)
        {
            WriteObject(new ProjectBuildResult
            {
                Success = false,
                ErrorMessage = "Nothing to do. Enable UpdateVersions, Build, PublishNuget, or PublishGitHub."
            });
            return;
        }

        var runner = new DotNetRepositoryReleaseService(logger);
        var spec = preparation.Spec;
        spec.WhatIf = true;
        var plan = runner.Execute(spec);
        var preflightErrors = new List<string>();
        if (!plan.Success)
            preflightErrors.Add(plan.ErrorMessage ?? "Plan/preflight validation failed.");

        if (preparation.PlanOnly || !ShouldProcess(preparation.RootPath, "Build project repository"))
        {
            support.TryWritePlan(plan, preparation.PlanOutputPath);
            WriteObject(new ProjectBuildResult
            {
                Success = preflightErrors.Count == 0,
                ErrorMessage = preflightErrors.Count == 0 ? null : string.Join(Environment.NewLine, preflightErrors),
                Release = plan
            });
            return;
        }

        var preflightError = support.ValidatePreflight(preparation.PublishNuget, preparation.PublishGitHub, preparation.CreateReleaseZip, preparation.PublishApiKey, config, configDir);
        if (!string.IsNullOrWhiteSpace(preflightError))
            preflightErrors.Add(preflightError!);

        var gitHubToken = preparation.PublishGitHub ? preparation.GitHubToken : null;
        if (preparation.PublishGitHub && string.IsNullOrWhiteSpace(preflightError))
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

        if (!string.IsNullOrWhiteSpace(preparation.StagingPath))
            support.PrepareStaging(preparation.StagingPath!, config.CleanStaging ?? false);
        ProjectBuildSupportService.EnsureDirectory(preparation.OutputPath);
        ProjectBuildSupportService.EnsureDirectory(preparation.ReleaseZipOutputPath);
        support.TryWritePlan(plan, preparation.PlanOutputPath);

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

        if (!preparation.PublishGitHub)
        {
            result.Success = true;
            WriteObject(result);
            return;
        }

        gitHubToken ??= preparation.GitHubToken;
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
