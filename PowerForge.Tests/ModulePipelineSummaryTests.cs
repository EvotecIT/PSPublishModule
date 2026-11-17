using System;
using PowerForge.ConsoleUi;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModulePipelineSummaryTests
{
    [Fact]
    public void BuildRecommendationRows_PreservesDiagnosticLocation()
    {
        var diagnostics = new[]
        {
            new BuildDiagnostic(
                ruleId: "VALIDATION-PSSA-PSAVOIDASSIGNMENTTOAUTOMATICVARIABLE",
                area: BuildDiagnosticArea.Validation,
                severity: BuildDiagnosticSeverity.Warning,
                scope: BuildDiagnosticScope.Project,
                owner: BuildDiagnosticOwner.ModuleAuthor,
                remediationKind: BuildDiagnosticRemediationKind.ManualFix,
                canAutoFix: false,
                summary: "Fix PSAvoidAssignmentToAutomaticVariable",
                details: "Tests\\Test.ps1:75:1 - The variable profile is automatic.",
                recommendedAction: "Rename the variable so it does not overwrite a PowerShell automatic variable.",
                sourcePath: "Tests\\Test.ps1:75:1",
                generatedBy: "PSScriptAnalyzer")
        };

        var rows = ModulePipelineSummary.BuildRecommendationRows(diagnostics);

        var row = Assert.Single(rows);
        Assert.Equal("Fix PSAvoidAssignmentToAutomaticVariable", row.When);
        Assert.Equal("Tests\\Test.ps1:75:1", row.Where);
        Assert.Contains("Rename the variable", row.Action);
    }
}
