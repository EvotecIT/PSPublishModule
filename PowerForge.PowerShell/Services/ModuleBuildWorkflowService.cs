namespace PowerForge;

internal sealed class ModuleBuildWorkflowService
{
    private readonly Func<ModulePipelineSpec, ModulePipelinePlan> _planPipeline;
    private readonly Func<ModulePipelineSpec, ModulePipelinePlan, ModulePipelineResult> _runPipeline;
    private readonly Func<ModulePipelineSpec, ModulePipelinePlan, string, ModulePipelineResult>? _runInteractive;
    private readonly Action<ModulePipelineResult> _writeSummary;

    public ModuleBuildWorkflowService(
        ILogger logger,
        Func<ModulePipelineSpec, ModulePipelinePlan>? planPipeline = null,
        Func<ModulePipelineSpec, ModulePipelinePlan, ModulePipelineResult>? runPipeline = null,
        Func<ModulePipelineSpec, ModulePipelinePlan, string, ModulePipelineResult>? runInteractive = null,
        Action<ModulePipelineResult>? writeSummary = null)
    {
        if (logger is null)
            throw new ArgumentNullException(nameof(logger));

        _planPipeline = planPipeline ?? (spec => new ModulePipelineRunner(logger).Plan(spec));
        _runPipeline = runPipeline ?? ((spec, plan) => new ModulePipelineRunner(logger).Run(spec, plan));
        _runInteractive = runInteractive;
        _writeSummary = writeSummary ?? (_ => { });
    }

    public ModuleBuildWorkflowResult Execute(ModuleBuildPreparedContext context, bool interactive, string configLabel)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));
        if (context.PipelineSpec is null)
            throw new ArgumentException("Prepared pipeline spec is required.", nameof(context));

        var plan = _planPipeline(context.PipelineSpec);
        var usedInteractiveView = interactive && _runInteractive is not null;

        try
        {
            var result = usedInteractiveView
                ? _runInteractive!(context.PipelineSpec, plan, configLabel)
                : _runPipeline(context.PipelineSpec, plan);

            _writeSummary(result);
            return new ModuleBuildWorkflowResult
            {
                Succeeded = true,
                UsedInteractiveView = usedInteractiveView,
                Plan = plan,
                Result = result
            };
        }
        catch (Exception ex)
        {
            var policyFailure = ex as ModulePipelineDiagnosticsPolicyException;
            var wrotePolicySummary = false;
            if (policyFailure is not null)
            {
                try
                {
                    _writeSummary(policyFailure.Result);
                    wrotePolicySummary = true;
                }
                catch
                {
                    wrotePolicySummary = false;
                }
            }

            return new ModuleBuildWorkflowResult
            {
                Succeeded = false,
                UsedInteractiveView = usedInteractiveView,
                WrotePolicySummary = wrotePolicySummary,
                Plan = plan,
                PolicyFailure = policyFailure,
                Error = ex
            };
        }
    }
}
