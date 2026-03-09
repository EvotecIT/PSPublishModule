namespace PowerForge;

internal static class BuildDiagnosticsPolicyEvaluator
{
    internal static BuildDiagnosticsPolicyEvaluation? Evaluate(
        ModulePipelineDiagnosticsOptions? options,
        BuildDiagnostic[] diagnostics,
        BuildDiagnosticsBaselineComparison? baseline)
    {
        if (options is null)
            return null;

        var hasPolicy = options.FailOnNewDiagnostics || options.FailOnSeverity.HasValue;
        if (!hasPolicy)
            return null;

        var relevant = (diagnostics ?? Array.Empty<BuildDiagnostic>())
            .Where(static diagnostic => diagnostic is not null && diagnostic.Severity != BuildDiagnosticSeverity.Info)
            .ToArray();

        var currentCount = relevant.Length;
        var baselineLoaded = baseline?.BaselineLoaded == true;
        var newCount = options.FailOnNewDiagnostics
            ? baselineLoaded
                ? relevant.Count(static diagnostic => diagnostic.BaselineState == BuildDiagnosticBaselineState.New)
                : currentCount
            : 0;

        var severityThreshold = options.FailOnSeverity;
        var severityCount = severityThreshold.HasValue
            ? relevant.Count(diagnostic => diagnostic.Severity >= severityThreshold.Value)
            : 0;

        var reasons = new List<string>(2);
        var suppressPolicyFailure = options.GenerateBaseline || options.UpdateBaseline;

        if (!suppressPolicyFailure && options.FailOnNewDiagnostics && newCount > 0)
        {
            reasons.Add(baselineLoaded
                ? $"{newCount} new diagnostic{(newCount == 1 ? string.Empty : "s")} detected compared to the baseline."
                : $"{newCount} diagnostic{(newCount == 1 ? string.Empty : "s")} detected and no baseline was loaded, so all current diagnostics are treated as new.");
        }

        if (!suppressPolicyFailure && severityThreshold.HasValue && severityCount > 0)
        {
            reasons.Add($"{severityCount} diagnostic{(severityCount == 1 ? string.Empty : "s")} at severity {severityThreshold.Value} or higher.");
        }

        return new BuildDiagnosticsPolicyEvaluation
        {
            FailOnNewDiagnostics = options.FailOnNewDiagnostics,
            FailOnSeverity = severityThreshold,
            PolicyViolated = reasons.Count > 0,
            CurrentDiagnosticCount = currentCount,
            NewDiagnosticCount = newCount,
            SeverityDiagnosticCount = severityCount,
            FailureReason = string.Join(" ", reasons)
        };
    }
}
