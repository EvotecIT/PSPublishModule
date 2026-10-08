namespace PowerForge;

public sealed partial class PowerShellCompilationProjectWorkflowService
{
    private static PowerShellCompilationProjectResult InspectDiagnostics(
        string projectPath, IEnumerable<string>? targetNames, bool verifyArtifact)
    {
        var operation = verifyArtifact ? "diagnose" : "explain";
        PowerShellCompilationProjectManifestService.ProjectContext context;
        try { context = PowerShellCompilationProjectManifestService.OpenForDiagnostics(projectPath); }
        catch (PowerShellCompilationProjectManifestService.InspectionFailure failure)
        {
            var issue = InspectionIssue(failure.Stage, "project.validation", failure);
            return Complete(operation, projectPath, new[]
            {
                new PowerShellCompilationProjectTargetResult
                {
                    Name = string.IsNullOrEmpty(failure.Target) ? "<project>" : failure.Target,
                    Message = issue.Message, Succeeded = false,
                    DiagnosticReport = new PowerShellCompilationDiagnosticReport { Issues = new[] { issue } }
                }
            });
        }
        var results = new List<PowerShellCompilationProjectTargetResult>();
        foreach (var artifact in SelectArtifacts(context, targetNames))
        {
            var stage = PowerShellCompilationDiagnosticStage.Dependency;
            var report = new PowerShellCompilationDiagnosticReport();
            var result = new PowerShellCompilationProjectTargetResult
            {
                Name = artifact.Name, TargetContractSha256 = artifact.Target.ContractSha256
            };
            PowerShellCompilationPlan? plan = null;
            PowerShellCompilationReportIssue? environmentIssue = null;
            try
            {
                string? packageRoot = null;
                if (File.Exists(context.Resolve(".powerforge/environment/environment.json")))
                {
                    try { packageRoot = ReadEnvironment(context).PackageRoot; }
                    catch (Exception exception)
                    {
                        // Still inspect source when acquired evidence is stale or incomplete.
                        environmentIssue = InspectionIssue(PowerShellCompilationDiagnosticStage.Dependency,
                            "project.environment", exception);
                    }
                }
                var providers = ResolveProviders(context, artifact);
                stage = PowerShellCompilationDiagnosticStage.Input;
                var input = ResolveInput(context, artifact);
                stage = PowerShellCompilationDiagnosticStage.Semantic;
                plan = CreatePlan(context, artifact, input, providers.Providers, packageRoot,
                    () => stage = PowerShellCompilationDiagnosticStage.Dependency);
                stage = PowerShellCompilationDiagnosticStage.Shaping;
                var explanation = PowerShellCompilationExplainShaper.CreateFinalExplanation(input, plan,
                    artifact.Target.TargetFramework, providers.Providers);
                report = PowerShellCompilationDiagnosticReportService.Create(plan, explanation);
                result.Succeeded = explanation.CanProceed;
                result.DependencyLockSha256 = plan.DependencyGraph?.LockSha256;
                result.Message = explanation.CanProceed
                    ? "Final target-aware decisions were shaped; inspect grouped routes and causes."
                    : "The selected target cannot proceed; inspect grouped source and dependency causes.";
                if (!verifyArtifact)
                {
                    stage = PowerShellCompilationDiagnosticStage.Input;
                    result.Path = context.Resolve($".powerforge/explain/{artifact.Name}.json");
                    WriteJson(result.Path, explanation);
                }
            }
            catch (Exception exception)
            {
                // Keep semantic evidence when final shaping fails, without presenting it as a final artifact route.
                if (plan is not null && !report.FinalShapeAvailable)
                    report = PowerShellCompilationDiagnosticReportService.Create(plan,
                        PowerShellCompilationExplanationService.Create(plan), finalShapeAvailable: false);
                if (!report.FinalShapeAvailable) report.CanProceed = false;
                var issue = InspectionIssue(stage, "project." + stage.ToString().ToLowerInvariant(), exception);
                report.Issues = report.Issues.Concat(new[] { issue }).ToArray();
                result.Message = issue.Message;
                result.Succeeded = false;
            }
            if (environmentIssue is not null)
            {
                report.Issues = report.Issues.Concat(new[] { environmentIssue }).ToArray();
                result.Message = environmentIssue.Message;
                result.Succeeded = false;
            }
            if (verifyArtifact)
            {
                try
                {
                    var validated = ValidateBuildReceipt(context, artifact);
                    result.Path = validated.ArtifactPath;
                    result.ArtifactSha256 = validated.Manifest.ArtifactSha256;
                    if (result.Succeeded)
                        result.Message = "Source decisions, target, locks, reproduction evidence, and complete artifact-set integrity are valid.";
                }
                catch (Exception exception)
                {
                    var issue = InspectionIssue(PowerShellCompilationDiagnosticStage.Integrity, "artifact.integrity", exception);
                    report.Issues = report.Issues.Concat(new[] { issue }).ToArray();
                    result.Message = issue.Message;
                    result.Succeeded = false;
                }
            }
            result.DiagnosticReport = report;
            results.Add(result);
        }
        return Complete(operation, context.ProjectPath, results);
    }

    private static PowerShellCompilationReportIssue InspectionIssue(
        PowerShellCompilationDiagnosticStage stage, string code, Exception exception)
        => new()
        {
            Stage = stage, Code = code,
            Message = PowerShellCompilationDiagnosticsEvidenceBuilder.Redact(null, exception.Message)
        };
}
