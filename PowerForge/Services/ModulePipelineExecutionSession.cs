using System.Collections.Generic;
using System.Linq;

namespace PowerForge;

internal sealed class ModulePipelineExecutionSession
{
    private readonly IModulePipelineProgressReporterV2? _reporterV2;
    private readonly HashSet<string> _startedKeys;
    private readonly Dictionary<string, ModulePipelineStep> _stepsByKey;
    private readonly Dictionary<ConfigurationArtefactSegment, ModulePipelineStep> _artefactSteps;
    private readonly Dictionary<ConfigurationPublishSegment, ModulePipelineStep> _publishSteps;

    private ModulePipelineExecutionSession(ModulePipelineStep[] steps, IModulePipelineProgressReporter reporter)
    {
        Steps = steps ?? Array.Empty<ModulePipelineStep>();
        Reporter = reporter ?? NullModulePipelineProgressReporter.Instance;
        _reporterV2 = Reporter as IModulePipelineProgressReporterV2;
        _startedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        _stepsByKey = Steps
            .Where(static step => !string.IsNullOrWhiteSpace(step.Key))
            .ToDictionary(static step => step.Key, StringComparer.OrdinalIgnoreCase);
        _artefactSteps = Steps
            .Where(static step => step.ArtefactSegment is not null)
            .ToDictionary(static step => step.ArtefactSegment!, static step => step);
        _publishSteps = Steps
            .Where(static step => step.PublishSegment is not null)
            .ToDictionary(static step => step.PublishSegment!, static step => step);

        TestSteps = Steps
            .Where(static step => step.Kind == ModulePipelineStepKind.Tests &&
                                  step.Key.StartsWith("tests:", StringComparison.OrdinalIgnoreCase) &&
                                  step.Key.EndsWith(":aftermerge", StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    internal IModulePipelineProgressReporter Reporter { get; }
    internal ModulePipelineStep[] Steps { get; }
    internal ModulePipelineStep[] TestSteps { get; }

    internal ModulePipelineStep? StageStep => GetStep("build:stage");
    internal ModulePipelineStep? BuildStep => GetStep("build:build");
    internal ModulePipelineStep? ManifestStep => GetStep("build:manifest");
    internal ModulePipelineStep? DocsExtractStep => GetStep("docs:extract");
    internal ModulePipelineStep? DocsWriteStep => GetStep("docs:write");
    internal ModulePipelineStep? DocsMamlStep => GetStep("docs:maml");
    internal ModulePipelineStep? FormatStagingStep => GetStep("format:staging");
    internal ModulePipelineStep? FormatProjectStep => GetStep("format:project");
    internal ModulePipelineStep? SignStep => GetStep("sign");
    internal ModulePipelineStep? FileConsistencyStep => GetStep("validate:fileconsistency");
    internal ModulePipelineStep? ProjectFileConsistencyStep => GetStep("validate:fileconsistency-project");
    internal ModulePipelineStep? CompatibilityStep => GetStep("validate:compatibility");
    internal ModulePipelineStep? ModuleValidationStep => GetStep("validate:module");
    internal ModulePipelineStep? BinaryConflictAnalysisStep => GetStep("validate:binary-conflicts");
    internal ModulePipelineStep? BinaryDependenciesStep => GetStep("tests:binary-dependencies");
    internal ModulePipelineStep? ImportModulesStep => GetStep("tests:import-modules");
    internal ModulePipelineStep? InstallStep => Steps.FirstOrDefault(static step => step.Kind == ModulePipelineStepKind.Install);
    internal ModulePipelineStep? CleanupStep => Steps.FirstOrDefault(static step => step.Kind == ModulePipelineStepKind.Cleanup);

    internal static ModulePipelineExecutionSession Create(ModulePipelinePlan plan, IModulePipelineProgressReporter? progress = null)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));

        return new ModulePipelineExecutionSession(
            steps: ModulePipelineStep.Create(plan),
            reporter: progress ?? NullModulePipelineProgressReporter.Instance);
    }

    internal ModulePipelineStep? GetArtefactStep(ConfigurationArtefactSegment artefact)
    {
        if (artefact is null) return null;
        return _artefactSteps.TryGetValue(artefact, out var step) ? step : null;
    }

    internal ModulePipelineStep? GetPublishStep(ConfigurationPublishSegment publish)
    {
        if (publish is null) return null;
        return _publishSteps.TryGetValue(publish, out var step) ? step : null;
    }

    internal void Start(ModulePipelineStep? step)
    {
        if (step is null) return;
        if (!string.IsNullOrWhiteSpace(step.Key))
            _startedKeys.Add(step.Key);

        try { Reporter.StepStarting(step); } catch { }
    }

    internal void Done(ModulePipelineStep? step)
    {
        if (step is null) return;
        try { Reporter.StepCompleted(step); } catch { }
    }

    internal void Fail(ModulePipelineStep? step, Exception error)
    {
        if (step is null) return;
        try { Reporter.StepFailed(step, error); } catch { }
    }

    internal void NotifySkippedOnFailure()
    {
        if (_reporterV2 is null) return;

        foreach (var step in Steps)
        {
            if (step is null || string.IsNullOrWhiteSpace(step.Key) || _startedKeys.Contains(step.Key))
                continue;

            try { _reporterV2.StepSkipped(step); } catch { }
        }
    }

    private ModulePipelineStep? GetStep(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        return _stepsByKey.TryGetValue(key, out var step) ? step : null;
    }
}
