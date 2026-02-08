using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    /// <summary>
    /// Executes the provided <paramref name="plan"/>.
    /// </summary>
    public DotNetPublishResult Run(DotNetPublishPlan plan, IDotNetPublishProgressReporter? progress)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        progress ??= NullDotNetPublishProgressReporter.Instance;

        var artefacts = new List<DotNetPublishArtefactResult>();
        string? manifestJson = null;
        string? manifestText = null;

        try
        {
            foreach (var step in plan.Steps ?? Array.Empty<DotNetPublishStep>())
            {
                progress.StepStarting(step);
                try
                {
                    switch (step.Kind)
                    {
                        case DotNetPublishStepKind.Restore:
                            Restore(plan, step.Runtime);
                            break;
                        case DotNetPublishStepKind.Clean:
                            Clean(plan);
                            break;
                        case DotNetPublishStepKind.Build:
                            Build(plan, step.Runtime);
                            break;
                        case DotNetPublishStepKind.Publish:
                            artefacts.Add(Publish(plan, step.TargetName!, step.Framework ?? string.Empty, step.Runtime!));
                            break;
                        case DotNetPublishStepKind.Manifest:
                            (manifestJson, manifestText) = WriteManifests(plan, artefacts);
                            break;
                    }

                    progress.StepCompleted(step);
                }
                catch (Exception ex)
                {
                    progress.StepFailed(step, ex);
                    throw new DotNetPublishStepException(step, ex);
                }
            }

            return new DotNetPublishResult
            {
                Succeeded = true,
                Artefacts = artefacts.ToArray(),
                ManifestJsonPath = manifestJson,
                ManifestTextPath = manifestText
            };
        }
        catch (Exception ex)
        {
            var failure = BuildFailure(plan, ex, out var errorMessage);

            _logger.Error(errorMessage);
            if (_logger.IsVerbose) _logger.Verbose(ex.ToString());
            return new DotNetPublishResult
            {
                Succeeded = false,
                ErrorMessage = errorMessage,
                Failure = failure,
                Artefacts = artefacts.ToArray(),
                ManifestJsonPath = manifestJson,
                ManifestTextPath = manifestText
            };
        }
    }

}
