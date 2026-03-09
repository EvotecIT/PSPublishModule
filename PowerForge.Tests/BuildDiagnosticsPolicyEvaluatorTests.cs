using Xunit;

namespace PowerForge.Tests;

public sealed class BuildDiagnosticsPolicyEvaluatorTests
{
    [Fact]
    public void Evaluate_FailOnNewDiagnostics_UsesBaselineComparison()
    {
        var diagnostics = new[]
        {
            CreateDiagnostic(BuildDiagnosticSeverity.Warning, BuildDiagnosticBaselineState.Existing),
            CreateDiagnostic(BuildDiagnosticSeverity.Warning, BuildDiagnosticBaselineState.New)
        };

        var evaluation = BuildDiagnosticsPolicyEvaluator.Evaluate(
            new ModulePipelineDiagnosticsOptions
            {
                FailOnNewDiagnostics = true
            },
            diagnostics,
            new BuildDiagnosticsBaselineComparison
            {
                BaselineLoaded = true
            });

        Assert.NotNull(evaluation);
        Assert.True(evaluation!.PolicyViolated);
        Assert.Equal(1, evaluation.NewDiagnosticCount);
        Assert.Contains("new diagnostic", evaluation.FailureReason);
    }

    [Fact]
    public void Evaluate_FailOnSeverity_CountsMatchingDiagnostics()
    {
        var diagnostics = new[]
        {
            CreateDiagnostic(BuildDiagnosticSeverity.Warning, BuildDiagnosticBaselineState.Existing),
            CreateDiagnostic(BuildDiagnosticSeverity.Error, BuildDiagnosticBaselineState.Existing)
        };

        var evaluation = BuildDiagnosticsPolicyEvaluator.Evaluate(
            new ModulePipelineDiagnosticsOptions
            {
                FailOnSeverity = BuildDiagnosticSeverity.Error
            },
            diagnostics,
            baseline: null);

        Assert.NotNull(evaluation);
        Assert.True(evaluation!.PolicyViolated);
        Assert.Equal(1, evaluation.SeverityDiagnosticCount);
        Assert.Contains("severity Error or higher", evaluation.FailureReason);
    }

    [Fact]
    public void Evaluate_GenerateBaseline_DoesNotFailPolicy()
    {
        var diagnostics = new[]
        {
            CreateDiagnostic(BuildDiagnosticSeverity.Warning, BuildDiagnosticBaselineState.New)
        };

        var evaluation = BuildDiagnosticsPolicyEvaluator.Evaluate(
            new ModulePipelineDiagnosticsOptions
            {
                GenerateBaseline = true,
                FailOnNewDiagnostics = true,
                FailOnSeverity = BuildDiagnosticSeverity.Warning
            },
            diagnostics,
            new BuildDiagnosticsBaselineComparison
            {
                BaselineLoaded = false
            });

        Assert.NotNull(evaluation);
        Assert.False(evaluation!.PolicyViolated);
        Assert.Equal(1, evaluation.NewDiagnosticCount);
        Assert.Equal(1, evaluation.SeverityDiagnosticCount);
        Assert.True(string.IsNullOrWhiteSpace(evaluation.FailureReason));
    }

    private static BuildDiagnostic CreateDiagnostic(BuildDiagnosticSeverity severity, BuildDiagnosticBaselineState baselineState)
        => new(
            ruleId: $"TEST-{severity}",
            area: BuildDiagnosticArea.Validation,
            severity: severity,
            scope: BuildDiagnosticScope.Project,
            owner: BuildDiagnosticOwner.ModuleAuthor,
            remediationKind: BuildDiagnosticRemediationKind.ManualFix,
            canAutoFix: false,
            summary: "Test diagnostic",
            details: "Test details",
            recommendedAction: "Test action")
        {
            BaselineState = baselineState
        };
}
