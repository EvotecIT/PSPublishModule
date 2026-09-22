using System.Text.Json;
using System.Text.Json.Nodes;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilerGate")]
public sealed class PowerShellCompilationProjectDiagnosticsTests
{
    internal const string ShapingSource = """
        function Get-Leaf {
            param([string] $Command)
            & $Command
        }
        function Get-Middle {
            param([string] $Command)
            Get-Leaf $Command
        }
        function Get-Outer {
            param([string] $Command)
            Get-Middle $Command
        }
        function Get-Typed { param([int] $Number) $Number + 1 }
        """;

    [Theory]
    [InlineData(PowerShellCompilationMode.Strict)]
    [InlineData(PowerShellCompilationMode.Hybrid)]
    public void ProjectExplain_GroupsFinalShapingAndBoundLocalCalls(PowerShellCompilationMode mode)
    {
        using var fixture = Fixture.Create(ShapingSource, PowerShellCompilationArtifactKind.BinaryModule, mode);
        var result = new PowerShellCompilationProjectWorkflowService().Explain(fixture.Project);
        var target = Assert.Single(result.Targets);
        var report = Assert.IsType<PowerShellCompilationDiagnosticReport>(target.DiagnosticReport);
        Assert.Equal(mode == PowerShellCompilationMode.Hybrid, result.Succeeded);
        Assert.Equal(result.Succeeded, report.CanProceed);
        Assert.True(report.FinalShapeAvailable);
        var leaf = report.Units.Single(group => group.Unit.Name == "Get-Leaf");
        var middle = report.Units.Single(group => group.Unit.Name == "Get-Middle");
        var outer = report.Units.Single(group => group.Unit.Name == "Get-Outer");
        Assert.Contains(leaf.Issues, issue => issue.Stage == PowerShellCompilationDiagnosticStage.Shaping &&
            issue.Code == "binary-module.cmdlet-shape" && issue.Line == 1);
        Assert.Equal(mode == PowerShellCompilationMode.Strict ? PowerShellCompilationDecisionKind.Rejected :
            PowerShellCompilationDecisionKind.RuntimeFallback, leaf.Unit.Decision);
        Assert.Equal(mode == PowerShellCompilationMode.Hybrid, leaf.Unit.RetainedHostedSource);
        var call = Assert.Single(middle.LocalCalls);
        Assert.Equal(leaf.Unit.UnitId, call.CalleeUnitId);
        Assert.Equal("Functions.psm1", call.CalleeRelativePath);
        Assert.Equal(7, call.Line);
        Assert.Equal(5, call.Column);
        Assert.Equal(middle.Unit.UnitId, Assert.Single(outer.LocalCalls).CalleeUnitId);
        Assert.Equal(11, Assert.Single(outer.LocalCalls).Line);
        Assert.True(report.Units.Single(group => group.Unit.Name == "Get-Typed").Unit.Emitted);
        var text = string.Join("\n", PowerShellCompilationDiagnosticReportFormatter.Format(report));
        Assert.Contains("[Shaping] binary-module.cmdlet-shape", text);
        Assert.Contains("Calls Get-Leaf at 7:5 -> Functions.psm1:1", text);
        Assert.DoesNotContain(fixture.Root, JsonSerializer.Serialize(report), StringComparison.OrdinalIgnoreCase);
        var explanation = JsonSerializer.Deserialize<PowerShellCompilationExplanation>(
            File.ReadAllText(target.Path!), PowerShellCompilationProjectManifestService.JsonOptions)!;
        Assert.Equal(explanation.CanProceed, result.Succeeded); // Do not use the pre-shaping plan's success.
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, fixture.Manifest.Artifacts.Single().OutputDirectory)));
    }

    [Fact]
    public void ProjectDiagnose_KeepsSemanticAndArtifactIntegrityFailuresSeparate()
    {
        using var fixture = Fixture.Create("function Invoke-Dynamic { param([string] $Name) & $Name }",
            PowerShellCompilationArtifactKind.Library, PowerShellCompilationMode.Strict);
        var workflow = new PowerShellCompilationProjectWorkflowService();
        var report = Assert.Single(workflow.Diagnose(fixture.Project).Targets).DiagnosticReport!;
        Assert.False(report.CanProceed);
        Assert.Contains(report.Units.SelectMany(group => group.Issues), issue =>
            issue.Stage == PowerShellCompilationDiagnosticStage.Semantic && issue.Code == "command.dynamic");
        Assert.Contains(report.Issues, issue => issue.Stage == PowerShellCompilationDiagnosticStage.Integrity);
        Assert.Contains(report.Issues, issue => issue.Code == "target.not-shaped");

        File.WriteAllText(fixture.Source, "function Get-Number { return 42 }");
        var explained = workflow.Explain(fixture.Project);
        Assert.True(explained.Succeeded);
        var diagnosed = workflow.Diagnose(fixture.Project);
        Assert.False(diagnosed.Succeeded);
        Assert.True(Assert.Single(diagnosed.Targets).DiagnosticReport!.CanProceed);
        Assert.Single(diagnosed.Targets[0].DiagnosticReport!.Issues,
            issue => issue.Stage == PowerShellCompilationDiagnosticStage.Integrity);
        var evidencePath = explained.Targets[0].Path!;
        File.Delete(evidencePath);
        Directory.CreateDirectory(evidencePath);
        var writeFailure = workflow.Explain(fixture.Project);
        Assert.False(writeFailure.Succeeded);
        Assert.True(writeFailure.Targets[0].DiagnosticReport!.CanProceed);
        Assert.True(writeFailure.Targets[0].DiagnosticReport!.FinalShapeAvailable);
        Assert.Contains(writeFailure.Targets[0].DiagnosticReport!.Issues,
            issue => issue.Stage == PowerShellCompilationDiagnosticStage.Input);
        Directory.Delete(evidencePath);
        var environmentDirectory = Directory.CreateDirectory(Path.Combine(fixture.Root, ".powerforge", "environment"));
        File.WriteAllText(Path.Combine(environmentDirectory.FullName, "environment.json"), "{}");
        var staleEnvironment = workflow.Diagnose(fixture.Project);
        Assert.False(staleEnvironment.Succeeded);
        Assert.True(staleEnvironment.Targets[0].DiagnosticReport!.CanProceed);
        Assert.NotEmpty(staleEnvironment.Targets[0].DiagnosticReport!.Units);
        Assert.Contains(staleEnvironment.Targets[0].DiagnosticReport!.Issues,
            issue => issue.Stage == PowerShellCompilationDiagnosticStage.Dependency && issue.Code == "project.environment");
    }

    [Fact]
    public void ProjectExplain_ReportsOwningInputDependencyAndTargetStages()
    {
        using var fixture = Fixture.Create("function Get-Number { return 42 }",
            PowerShellCompilationArtifactKind.Library, PowerShellCompilationMode.Strict);
        var workflow = new PowerShellCompilationProjectWorkflowService();
        var original = File.ReadAllText(fixture.Project);
        var json = JsonNode.Parse(original)!;
        json["artifacts"]![0]!["target"]!["targetFramework"] = "net8.0";
        File.WriteAllText(fixture.Project, json.ToJsonString());
        CheckStage(PowerShellCompilationDiagnosticStage.Target);
        File.WriteAllText(fixture.Project, original);
        json = JsonNode.Parse(original)!;
        json["providerPackages"] = new JsonArray("missing-provider.nupkg");
        File.WriteAllText(fixture.Project, json.ToJsonString());
        CheckStage(PowerShellCompilationDiagnosticStage.Dependency);
        File.WriteAllText(fixture.Project, original);
        File.Delete(fixture.Source);
        CheckStage(PowerShellCompilationDiagnosticStage.Input);

        void CheckStage(PowerShellCompilationDiagnosticStage stage)
        {
            var result = workflow.Explain(fixture.Project);
            Assert.False(result.Succeeded);
            var report = Assert.Single(result.Targets).DiagnosticReport!;
            Assert.False(report.FinalShapeAvailable);
            Assert.Equal(stage, Assert.Single(report.Issues).Stage);
            Assert.DoesNotContain(fixture.Root, JsonSerializer.Serialize(report), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ProjectExplain_ReportsMissingAssemblyWithoutRejectingTypedUnitSemantics()
    {
        using var fixture = Fixture.Create("function Get-Number { return 42 }",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid);
        File.WriteAllText(Path.Combine(fixture.Root, "Module.psd1"),
            "@{RootModule='Functions.psm1';ModuleVersion='1.0.0';RequiredAssemblies=@('Missing.dll');FunctionsToExport=@('Get-Number')}");
        fixture.Manifest.Sources = new[] { "Module.psd1" };
        new PowerShellCompilationProjectManifestService().Save(fixture.Project, fixture.Manifest);
        var result = new PowerShellCompilationProjectWorkflowService().Explain(fixture.Project);
        Assert.False(result.Succeeded);
        var report = Assert.Single(result.Targets).DiagnosticReport!;
        Assert.Contains(report.Issues, issue => issue.Stage == PowerShellCompilationDiagnosticStage.Dependency &&
            issue.Code == "dependency.missing" && issue.RelativePath == "Missing.dll");
        Assert.True(Assert.Single(report.Units).Unit.SemanticEligible);
        Assert.Empty(Assert.Single(report.Units).Issues);
    }

    [Fact]
    public void ProjectExplain_RetainsCrossFileCallIdentityAndDoesNotMutateDecisionEvidence()
    {
        using var fixture = Fixture.Create("function Get-Outer { param([int] $Number) Get-Inner $Number }",
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid);
        Directory.CreateDirectory(Path.Combine(fixture.Root, "Public"));
        File.WriteAllText(Path.Combine(fixture.Root, "Public", "Inner.ps1"),
            "function Get-Inner { param([int] $Number) $Number + 1 }");
        File.WriteAllText(fixture.Source, ". \"$PSScriptRoot/Public/Inner.ps1\"\n" + File.ReadAllText(fixture.Source) + "\nGet-Outer 1\n");
        var context = PowerShellCompilationProjectManifestService.Open(fixture.Project);
        var artifact = fixture.Manifest.Artifacts.Single();
        var input = new PowerShellCompilationInputResolver().Resolve(context.Sources,
            artifact.Target.ArtifactKind, artifact.Target.Mode);
        var plan = new PowerShellCompilationAnalyzer().Analyze(input, artifact.Target.Mode,
            artifact.Target.TargetFramework, PowerShellCompilationResourceMode.Declared,
            null, null, null, artifact.Target);
        var explanation = PowerShellCompilationExplainShaper.CreateFinalExplanation(input, plan, "net10.0");
        var before = JsonSerializer.Serialize(explanation);
        var report = PowerShellCompilationDiagnosticReportService.Create(plan, explanation);
        Assert.Equal(before, JsonSerializer.Serialize(explanation));
        var outer = report.Units.Single(group => group.Unit.Name == "Get-Outer");
        var inner = report.Units.Single(group => group.Unit.Name == "Get-Inner");
        Assert.Equal(inner.Unit.UnitId, Assert.Single(outer.LocalCalls).CalleeUnitId);
        Assert.Equal(inner.RelativePath, Assert.Single(outer.LocalCalls).CalleeRelativePath);
        Assert.Equal("Public/Inner.ps1", inner.RelativePath);
        var workflowReport = Assert.Single(new PowerShellCompilationProjectWorkflowService().Explain(fixture.Project).Targets).DiagnosticReport!;
        Assert.Equal(inner.Unit.UnitId, workflowReport.Units.Single(group => group.Unit.Name == "Get-Inner").Unit.UnitId);
        Assert.Equal(2, Assert.Single(outer.LocalCalls).Line);
        var scriptCall = Assert.Single(report.Units.Single(group => group.Unit.Kind == PowerShellCompilationUnitKind.Script).LocalCalls);
        Assert.Equal(outer.Unit.UnitId, scriptCall.CalleeUnitId);
        Assert.Equal(0, scriptCall.Line);
        Assert.Equal(0, scriptCall.Column);
        Assert.Contains("call location unavailable", string.Join("\n", PowerShellCompilationDiagnosticReportFormatter.Format(report)));
    }

    internal sealed class Fixture : IDisposable
    {
        private Fixture(string root, string project, string source, PowerShellCompilationProjectManifest manifest)
        { Root = root; Project = project; Source = source; Manifest = manifest; }
        internal string Root { get; }
        internal string Project { get; }
        internal string Source { get; }
        internal PowerShellCompilationProjectManifest Manifest { get; }
        internal static Fixture Create(string source, PowerShellCompilationArtifactKind kind, PowerShellCompilationMode mode)
        {
            var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PFC", Guid.NewGuid().ToString("N")[..12])).FullName;
            var path = Path.Combine(root, "Functions.psm1");
            File.WriteAllText(path, source);
            var project = Path.Combine(root, "powerforge.psproject.json");
            var service = new PowerShellCompilationProjectManifestService();
            var target = PowerShellCompilationTargetContractService.Create(kind, mode, "net10.0", null,
                false, false, PowerShellCompilationExecutableOptimization.None, true);
            var manifest = service.Create(project, path, "Diagnostics", target);
            service.Save(project, manifest);
            return new Fixture(root, project, path, manifest);
        }
        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
