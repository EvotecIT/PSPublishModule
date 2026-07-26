using System;
using System.Collections.Generic;
using System.IO;
using PowerForge;
using PowerForge.ConsoleShared;
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
        BufferedLogger? interactiveBuffer = null;
        ILogger logger = interactive
            ? interactiveBuffer = new BufferedLogger { IsVerbose = isVerbose }
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

        var executeBuild = !preparation.PlanOnly && ShouldProcess(preparation.RootPath, "Build project repository");
        var workflowService = new ProjectBuildWorkflowService(
            logger,
            signAssemblies: DotNetAssemblySigningCallbackFactory.Create(logger),
            validateAssemblySigning: DotNetAssemblySigningCallbackFactory.CreatePreflight(logger));
        ProjectBuildWorkflowResult workflow;
        if (interactive)
        {
            workflow = SpectreProjectBuildConsoleUi.RunInteractive(
                new ProjectBuildConsolePlan
                {
                    ConfigPath = configFullPath,
                    RootPath = preparation.RootPath,
                    StagingPath = preparation.StagingPath,
                    OutputPath = preparation.OutputPath,
                    PlanOutputPath = preparation.PlanOutputPath,
                    PlanOnly = preparation.PlanOnly || !executeBuild,
                    UpdateVersions = preparation.UpdateVersions,
                    Build = preparation.Build,
                    SignPackages = preparation.Build &&
                        preparation.Spec.SignPackages &&
                        !string.IsNullOrWhiteSpace(preparation.Spec.CertificateThumbprint),
                    PublishNuGet = preparation.PublishNuget,
                    PublishGitHub = preparation.PublishGitHub
                },
                progress => workflowService.Execute(config, configDir, preparation, executeBuild, progress: progress));

            var summary = new DotNetRepositoryReleaseSummaryService().CreateSummary(workflow.Result.Release ?? new DotNetRepositoryReleaseResult
            {
                Success = workflow.Result.Success,
                ErrorMessage = workflow.Result.ErrorMessage
            });
            var display = new DotNetRepositoryReleaseDisplayService().CreateDisplay(summary, isPlan: preparation.PlanOnly || !executeBuild);
            SpectreProjectBuildSummaryWriter.Write(display);

            if (!workflow.Result.Success && interactiveBuffer?.Entries.Count > 0)
            {
                new BufferedLogSupportService().WriteTail(
                    interactiveBuffer.Entries,
                    new SpectreConsoleLogger { IsVerbose = isVerbose });
            }

            if (workflow.GitHubPublishSummary is { PerProject: false } publishSummary)
                WriteGitHubSummary(false, publishSummary.SummaryTag, publishSummary.SummaryReleaseUrl, publishSummary.SummaryAssetsCount, workflow.Result.GitHub);
        }
        else
        {
            workflow = workflowService.Execute(config, configDir, preparation, executeBuild);
        }

        WriteObject(workflow.Result);
    }
}
