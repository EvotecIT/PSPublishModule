using System.Text.Json;
using PowerForgeStudio.Domain.Portfolio;
using PowerForgeStudio.Domain.PowerShell;
using PowerForgeStudio.Domain.Queue;

namespace PowerForgeStudio.Orchestrator.Portfolio;

public sealed class RepositoryDetailService
{
    private readonly RepositoryGitRemediationService _gitRemediationService;
    private readonly RepositoryGitQuickActionService _gitQuickActionService;

    public RepositoryDetailService()
        : this(new RepositoryGitRemediationService(), new RepositoryGitQuickActionService()) {
    }

    public RepositoryDetailService(
        RepositoryGitRemediationService gitRemediationService,
        RepositoryGitQuickActionService gitQuickActionService)
    {
        _gitRemediationService = gitRemediationService;
        _gitQuickActionService = gitQuickActionService;
    }

    public RepositoryDetailSnapshot CreateDetail(
        RepositoryPortfolioItem? repository,
        ReleaseQueueSession? queueSession,
        PSPublishModuleResolution buildEngineResolution,
        RepositoryGitQuickActionReceipt? gitQuickActionReceipt = null)
    {
        var remediationSteps = _gitRemediationService.BuildSteps(repository);
        var quickActions = _gitQuickActionService.BuildActions(repository, remediationSteps);

        if (repository is null)
        {
            return new RepositoryDetailSnapshot(
                RepositoryName: "No repository selected",
                RepositoryBadge: "Selection pending",
                ReadinessDisplay: "Unknown",
                ReadinessReason: "Select a managed repository to inspect its contract, queue state, and adapter evidence.",
                RootPath: "No repository path selected.",
                BranchDisplay: "Branch information unavailable",
                GitDiagnosticsDisplay: "No git diagnostics yet.",
                GitDiagnosticsDetail: "Git preflight guidance will appear here once a managed repository is selected.",
                GitRemediationSteps: remediationSteps,
                GitQuickActions: quickActions,
                LastGitActionDisplay: "No git action recorded yet.",
                LastGitActionSummary: "Quick-action results will appear here after you run a git action from the repository detail pane.",
                LastGitActionOutput: "No output captured yet.",
                LastGitActionError: "No error captured yet.",
                BuildContractDisplay: "No build contract selected.",
                QueueLaneDisplay: "No queue state selected.",
                QueueCheckpointDisplay: "No checkpoint selected.",
                QueueSummary: "Queue evidence will appear here after a repository is selected and the queue has been prepared.",
                QueueCheckpointPayload: "No checkpoint payload captured yet.",
                ReleaseDriftDisplay: "Unknown",
                ReleaseDriftDetail: "Release drift will appear here once GitHub inbox data is available for the selected repository.",
                BuildEngineDisplay: $"{buildEngineResolution.SourceDisplay} ({buildEngineResolution.VersionDisplay})",
                BuildEnginePath: buildEngineResolution.ManifestPath,
                BuildEngineAdvisory: buildEngineResolution.Warning ?? "The resolved PSPublishModule engine will be shown here even before a repository is selected.",
                AdapterEvidence: [
                    new RepositoryAdapterEvidence(
                        AdapterDisplay: "Plan preview",
                        StatusDisplay: "Not run",
                        Summary: "No adapter evidence yet.",
                        Detail: "Prepare Queue to collect plan-only results for the first managed repositories.",
                        ArtifactPath: "No plan artifact captured yet.",
                        OutputTail: "No output tail captured yet.",
                        ErrorTail: "No error tail captured yet.")
                ]);
        }

        var queueItem = queueSession?.Items
            .Where(item => string.Equals(item.RootPath, repository.RootPath, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.UpdatedAtUtc)
            .ThenBy(item => item.QueueOrder)
            .FirstOrDefault();

        var buildContractDisplay = string.IsNullOrWhiteSpace(repository.PrimaryBuildScriptPath)
            ? "No build script detected yet."
            : repository.PrimaryBuildScriptPath;

        var queueLaneDisplay = queueItem is null
            ? "Not queued yet"
            : queueItem.LaneDisplay;

        var queueCheckpointDisplay = queueItem?.CheckpointKey ?? "Checkpoint not created yet";
        var queueSummary = queueItem?.Summary ?? "This repository is not part of the current persisted draft queue.";
        var queueCheckpointPayload = FormatCheckpointPayload(queueItem?.CheckpointStateJson);

        return new RepositoryDetailSnapshot(
            RepositoryName: repository.Name,
            RepositoryBadge: $"{repository.RepositoryKind} / {repository.WorkspaceKind}",
            ReadinessDisplay: repository.ReadinessKind.ToString(),
            ReadinessReason: repository.ReadinessReason,
            RootPath: repository.RootPath,
            BranchDisplay: $"{repository.BranchName} ({repository.UpstreamBranch})",
            GitDiagnosticsDisplay: repository.Git.DiagnosticStatus,
            GitDiagnosticsDetail: repository.Git.DiagnosticDetail,
            GitRemediationSteps: remediationSteps,
            GitQuickActions: quickActions,
            LastGitActionDisplay: gitQuickActionReceipt is null
                ? "No git action recorded yet."
                : $"{gitQuickActionReceipt.ActionTitle} ({gitQuickActionReceipt.StatusDisplay})",
            LastGitActionSummary: gitQuickActionReceipt?.Summary ?? "Quick-action results will appear here after you run a git action from the repository detail pane.",
            LastGitActionOutput: FormatTail(gitQuickActionReceipt?.OutputTail, "No output captured yet."),
            LastGitActionError: FormatTail(gitQuickActionReceipt?.ErrorTail, "No error captured yet."),
            BuildContractDisplay: buildContractDisplay,
            QueueLaneDisplay: queueLaneDisplay,
            QueueCheckpointDisplay: queueCheckpointDisplay,
            QueueSummary: queueSummary,
            QueueCheckpointPayload: queueCheckpointPayload,
            ReleaseDriftDisplay: repository.ReleaseDrift?.StatusDisplay ?? "Unknown",
            ReleaseDriftDetail: repository.ReleaseDrift?.Detail ?? "Release drift has not been assessed for the selected repository yet.",
            BuildEngineDisplay: $"{buildEngineResolution.SourceDisplay} ({buildEngineResolution.VersionDisplay})",
            BuildEnginePath: buildEngineResolution.ManifestPath,
            BuildEngineAdvisory: buildEngineResolution.Warning ?? "The selected repository will use this PSPublishModule engine for module-aware build, publish, and signing adapter calls.",
            AdapterEvidence: BuildAdapterEvidence(repository));
    }

    private static IReadOnlyList<RepositoryAdapterEvidence> BuildAdapterEvidence(RepositoryPortfolioItem repository)
    {
        var results = repository.PlanResults ?? [];
        if (results.Count == 0)
        {
            return [
                new RepositoryAdapterEvidence(
                    AdapterDisplay: "Plan preview",
                    StatusDisplay: "Not run",
                    Summary: "No adapter evidence yet.",
                    Detail: "Prepare Queue has not captured plan-only output for this repository yet.",
                    ArtifactPath: "No plan artifact captured yet.",
                    OutputTail: "No output tail captured yet.",
                    ErrorTail: "No error tail captured yet.")
            ];
        }

        return results
            .Select(result => new RepositoryAdapterEvidence(
                AdapterDisplay: result.AdapterKind.ToString(),
                StatusDisplay: result.Status.ToString(),
                Summary: result.Summary,
                Detail: FormatEvidenceDetail(result),
                ArtifactPath: string.IsNullOrWhiteSpace(result.PlanPath)
                    ? "No plan artifact path captured."
                    : result.PlanPath!,
                OutputTail: FormatTail(result.OutputTail, "No output tail captured."),
                ErrorTail: FormatTail(result.ErrorTail, "No error tail captured.")))
            .ToArray();
    }

    private static string FormatEvidenceDetail(RepositoryPlanResult result)
    {
        var detail = result.Status == RepositoryPlanStatus.Failed
            ? FirstMeaningfulLine(result.ErrorTail) ?? FirstMeaningfulLine(result.OutputTail)
            : FirstMeaningfulLine(result.OutputTail);

        if (!string.IsNullOrWhiteSpace(detail))
        {
            return detail!;
        }

        if (!string.IsNullOrWhiteSpace(result.PlanPath))
        {
            return result.PlanPath!;
        }

        return $"Completed in {result.DurationSeconds:0.##}s with exit code {result.ExitCode}.";
    }

    private static string? FirstMeaningfulLine(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
    }

    private static string FormatCheckpointPayload(string? checkpointStateJson)
    {
        if (string.IsNullOrWhiteSpace(checkpointStateJson))
        {
            return "No checkpoint payload captured yet.";
        }

        try
        {
            using var document = JsonDocument.Parse(checkpointStateJson);
            return JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions {
                WriteIndented = true
            });
        }
        catch (JsonException)
        {
            return checkpointStateJson!;
        }
    }

    private static string FormatTail(string? value, string emptyMessage)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return emptyMessage;
        }

        var lines = value
            .Split(["\r\n", "\n"], StringSplitOptions.None)
            .TakeLast(12)
            .ToArray();

        return string.Join(Environment.NewLine, lines);
    }
}
