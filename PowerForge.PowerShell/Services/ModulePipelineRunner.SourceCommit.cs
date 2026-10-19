using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private static void ValidateSelectedModuleReleaseProvenanceSourceContext(
        ModulePipelinePlan plan,
        ModulePipelineRunState state)
    {
        if (!plan.GenerateReleaseProvenance)
            return;

        var candidates = state.ArtefactResults.Where(artefact =>
            artefact.Type is ArtefactType.Packed or ArtefactType.ScriptPacked ||
            (plan.Release?.Configuration is not null && artefact.Type == ArtefactType.Script)).ToArray();
        if (candidates.Length == 0)
            return;
        var selected = plan.Publishes
            .Where(static publish => publish.Configuration is { Enabled: true, Destination: PublishDestination.GitHub })
            .SelectMany(publish => SelectModuleReleaseArtefacts(candidates, publish.Configuration.ID))
            .Where(static artefact => artefact.Type == ArtefactType.Packed)
            .Distinct()
            .ToArray();
        if (selected.Length == 0)
            return;
        if (!GitObjectId.IsFull(plan.SourceCommitSha))
        {
            throw new InvalidOperationException(
                "Module release provenance source context is unknown for the selected payload. Rebuild from the selected source checkout before publishing with GenerateProvenance.");
        }

        // Check the selected payload after cache restoration. The protected checkout
        // revision remains the current-run mutation guard, not a historical artifact identity.
        foreach (var artefact in selected)
        {
            var evidence = PowerForgeReleaseArtifactVerifier.ReadSigningEvidence(artefact.OutputPath + ".signing.json");
            if (!string.Equals(evidence.SourceRevision, plan.SourceCommitSha, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Module release provenance source context '{evidence.SourceRevision}' does not match the selected payload's recorded build commit '{plan.SourceCommitSha}'. Rebuild from the selected source checkout or restore a coherent cached release payload before publishing.");
            }
        }
    }

    private static void CaptureSynchronizedReleaseSourceCommits(
        ModulePipelinePlan plan,
        ModulePipelineRunState state,
        SynchronizedReleaseCheckpoint checkpoint)
    {
        checkpoint.SourceCommitSha = plan.SourceCommitSha;
        checkpoint.PackageSourceCommitShas = ResolveSynchronizedReleasePayloadLanes(state)
            .ToDictionary(
                static lane => lane.CheckpointKey,
                static lane => lane.Execution.Result.Release?.SourceCommitSha,
                StringComparer.OrdinalIgnoreCase);
        checkpoint.CapturedSourceCommits = true;
    }

    private static void RestoreSynchronizedReleaseSourceCommits(
        ModulePipelinePlan plan,
        ModulePipelineRunState state,
        SynchronizedReleaseCheckpoint checkpoint)
    {
        // Older checkpoints have no commit context. Never label their cached payload
        // using the checkout observed by the retry's fresh build.
        plan.SourceCommitSha = checkpoint.CapturedSourceCommits ? checkpoint.SourceCommitSha : null;
        foreach (var lane in ResolveSynchronizedReleasePayloadLanes(state))
        {
            if (lane.Execution.Result.Release is not { } release)
                continue;
            if (!checkpoint.CapturedSourceCommits)
            {
                release.SourceCommitSha = null;
                continue;
            }
            if (checkpoint.PackageSourceCommitShas is null ||
                !checkpoint.PackageSourceCommitShas.TryGetValue(lane.CheckpointKey, out var sourceCommit))
            {
                throw new InvalidOperationException(
                    $"The coordinated release checkpoint has no build source identity for package lane '{lane.CheckpointKey}'. Preserve the checkpoint and inspect the incomplete release before retrying.");
            }
            release.SourceCommitSha = sourceCommit;
        }
    }

    private static bool CanAdvanceSynchronizedReleaseSourceCommits(
        SynchronizedReleaseCheckpoint previous,
        SynchronizedReleaseCheckpoint candidate)
    {
        if (string.IsNullOrWhiteSpace(previous.PayloadFingerprint))
            return true;
        if (previous.CapturedSourceCommits != candidate.CapturedSourceCommits ||
            !string.Equals(previous.SourceCommitSha, candidate.SourceCommitSha, StringComparison.OrdinalIgnoreCase) ||
            previous.PackageSourceCommitShas is null || candidate.PackageSourceCommitShas is null ||
            previous.PackageSourceCommitShas.Count != candidate.PackageSourceCommitShas.Count)
        {
            return false;
        }
        return previous.PackageSourceCommitShas.All(entry =>
            candidate.PackageSourceCommitShas.TryGetValue(entry.Key, out var sourceCommit) &&
            string.Equals(entry.Value, sourceCommit, StringComparison.OrdinalIgnoreCase));
    }
}
