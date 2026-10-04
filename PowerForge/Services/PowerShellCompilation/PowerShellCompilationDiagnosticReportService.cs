namespace PowerForge;

/// <summary>Groups existing final decision evidence without parsing source or deciding compiler eligibility.</summary>
public static class PowerShellCompilationDiagnosticReportService
{
    /// <summary>Projects final per-unit decisions, stage-specific causes, and bound local-call dependencies.</summary>
    public static PowerShellCompilationDiagnosticReport Create(
        PowerShellCompilationPlan plan, PowerShellCompilationExplanation explanation, bool finalShapeAvailable = true)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (explanation is null) throw new ArgumentNullException(nameof(explanation));
        var plannedUnits = plan.Files.SelectMany(file => file.Units.Select(unit =>
            (Id: PowerShellCompilationExplanationService.ComputeUnitId(
                PowerShellCompilationExplanationService.NormalizeRelativePath(file.RelativePath, Path.GetFileName(file.FullPath)), unit), Unit: unit)))
            .ToDictionary(static item => item.Id, static item => item.Unit, StringComparer.Ordinal);
        var issues = explanation.Files.SelectMany(file => file.Causes.Select(cause =>
            Issue(cause.Code == PowerShellCompilationDiagnosticCode.InputError
                ? PowerShellCompilationDiagnosticStage.Input : PowerShellCompilationDiagnosticStage.Semantic,
                file.RelativePath, cause))).ToList();
        issues.AddRange(explanation.DependencyCauses.Select(cause => new PowerShellCompilationReportIssue
        {
            Stage = PowerShellCompilationDiagnosticStage.Dependency, Code = "dependency.missing",
            RelativePath = cause.RelativePath, Message = cause.Message
        }));
        if (plan.Mode == PowerShellCompilationMode.Strict && !plan.CanProceed)
            issues.Add(new PowerShellCompilationReportIssue
            {
                Stage = PowerShellCompilationDiagnosticStage.Shaping, Code = "target.not-shaped",
                Message = "Strict compilation stopped before artifact shaping. Units without local semantic blockers still have no artifact; address the reported source and dependency causes."
            });
        var groups = explanation.Files.SelectMany(file => file.Units.Select(unit =>
        {
            if (!plannedUnits.TryGetValue(unit.UnitId, out var planned))
                throw new InvalidOperationException("The explanation does not belong to the supplied plan.");
            return new PowerShellCompilationDiagnosticGroup
            {
                RelativePath = file.RelativePath, Unit = unit,
                Issues = unit.Causes.Select(cause => Issue(
                    planned.Diagnostics.Any(diagnostic => diagnostic.FeatureId == cause.FeatureId &&
                        diagnostic.Line == cause.Line && diagnostic.Column == cause.Column)
                        ? PowerShellCompilationDiagnosticStage.Semantic : PowerShellCompilationDiagnosticStage.Shaping,
                    file.RelativePath, cause)).ToArray(),
                LocalCalls = planned.LocalCalls.ToArray()
            };
        })).ToArray();
        return new PowerShellCompilationDiagnosticReport
        {
            CanProceed = explanation.CanProceed, FinalShapeAvailable = finalShapeAvailable,
            Issues = issues.ToArray(), Units = groups
        };
    }

    private static PowerShellCompilationReportIssue Issue(PowerShellCompilationDiagnosticStage stage,
        string relativePath, PowerShellCompilationExplanationDiagnostic cause)
        => new()
        {
            Stage = stage, Code = cause.FeatureId, RelativePath = relativePath,
            Line = cause.Line, Column = cause.Column, Message = cause.Message
        };
}
