namespace PowerForge;

internal sealed class ModuleTestSuiteWorkflowService
{
    private readonly Func<ModuleTestSuiteSpec, ModuleTestSuiteResult> _runSuite;
    private readonly Func<ModuleTestSuiteResult, bool, string?, string[]> _buildCiOutputs;

    public ModuleTestSuiteWorkflowService(
        ILogger logger,
        Func<ModuleTestSuiteSpec, ModuleTestSuiteResult>? runSuite = null,
        Func<ModuleTestSuiteResult, bool, string?, string[]>? buildCiOutputs = null)
    {
        if (logger is null)
            throw new ArgumentNullException(nameof(logger));

        _runSuite = runSuite ?? (spec => new ModuleTestSuiteService(new PowerShellRunner(), logger).Run(spec));
        _buildCiOutputs = buildCiOutputs ?? ((result, success, errorMessage) =>
            new ModuleTestSuiteCiOutputService().BuildOutputs(result, success, errorMessage));
    }

    public ModuleTestSuiteWorkflowResult Execute(ModuleTestSuitePreparedContext context)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));
        if (context.Spec is null)
            throw new ArgumentException("Prepared test suite spec is required.", nameof(context));

        var result = _runSuite(context.Spec);
        var success = result.FailedCount == 0;
        var failureMessage = success
            ? null
            : $"{result.FailedCount} test{(result.FailedCount != 1 ? "s" : string.Empty)} failed";

        return new ModuleTestSuiteWorkflowResult
        {
            Result = result,
            FailureMessage = failureMessage,
            CiOutputLines = _buildCiOutputs(result, success, failureMessage)
        };
    }
}
