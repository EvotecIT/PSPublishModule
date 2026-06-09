namespace PowerForge;

/// <summary>
/// Raised when configured diagnostics policy rules fail after the pipeline has already produced a full result.
/// </summary>
public sealed class ModulePipelineDiagnosticsPolicyException : InvalidOperationException
{
    /// <summary>
    /// Creates a new diagnostics policy exception with the evaluated pipeline result.
    /// </summary>
    public ModulePipelineDiagnosticsPolicyException(
        ModulePipelineResult result,
        BuildDiagnosticsPolicyEvaluation policy,
        string message)
        : base(message)
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
        Policy = policy ?? throw new ArgumentNullException(nameof(policy));
    }

    /// <summary>
    /// Full pipeline result that triggered the policy failure.
    /// </summary>
    public ModulePipelineResult Result { get; }
    /// <summary>
    /// Policy evaluation that caused the failure.
    /// </summary>
    public BuildDiagnosticsPolicyEvaluation Policy { get; }
}
