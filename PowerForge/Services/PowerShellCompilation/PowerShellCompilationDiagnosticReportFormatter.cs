namespace PowerForge;

/// <summary>Formats grouped diagnostics for text consumers without choosing compiler routes.</summary>
public static class PowerShellCompilationDiagnosticReportFormatter
{
    /// <summary>Returns readable source groups and direct links to called units, including retained and rejected routes.</summary>
    public static IEnumerable<string> Format(PowerShellCompilationDiagnosticReport report)
    {
        if (report is null) throw new ArgumentNullException(nameof(report));
        yield return report.FinalShapeAvailable
            ? $"Source and final-shaping decisions: {(report.CanProceed ? "can proceed" : "blocked")}."
            : "Analysis evidence only; final shaping is unavailable.";
        foreach (var issue in report.Issues) yield return FormatIssue(issue);
        var units = report.Units.ToDictionary(static group => group.Unit.UnitId, StringComparer.Ordinal);
        foreach (var group in report.Units)
        {
            var unit = group.Unit;
            yield return $"{group.RelativePath}:{unit.StartLine} {unit.Name}: {unit.Decision} / {unit.LoweringRoute}" +
                (unit.RetainedHostedSource ? "; authored source retained" : string.Empty) +
                (unit.PromotedTypedRegions > 0 ? $"; {unit.PromotedTypedRegions} typed regions" : string.Empty) +
                (unit.Omitted ? "; omitted from artifact" : string.Empty);
            foreach (var issue in group.Issues) yield return "  " + FormatIssue(issue);
            foreach (var call in group.LocalCalls)
            {
                var location = call.Line > 0 ? $"{call.Line}:{call.Column}" : "call location unavailable";
                var disposition = units.TryGetValue(call.CalleeUnitId, out var callee)
                    ? callee.Unit.Decision.ToString() : "decision unavailable";
                yield return $"  Calls {call.CalleeName} at {location} -> {call.CalleeRelativePath}:{call.CalleeStartLine} ({disposition}); see callee causes.";
            }
        }
    }

    private static string FormatIssue(PowerShellCompilationReportIssue issue)
    {
        var location = string.IsNullOrEmpty(issue.RelativePath) ? string.Empty
            : issue.Line > 0 ? $" {issue.RelativePath}:{issue.Line}:{issue.Column}" : " " + issue.RelativePath;
        return $"[{issue.Stage}] {issue.Code}{location}: {issue.Message}";
    }
}
