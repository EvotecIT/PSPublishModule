using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class PowerForgeCliPowerShellCompilationTests
{
    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public async Task ProjectDiagnosticsCli_UsesAcquiredRuntimePackWithEmptyGlobalCache()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = PowerShellCompilationProjectDiagnosticsTests.Fixture.Create(
            "function Get-Number { return 7 }", PowerShellCompilationArtifactKind.Library, PowerShellCompilationMode.Strict);
        var source = Path.Combine(fixture.Root, "main.ps1");
        File.WriteAllText(source, "return 7");
        var target = PowerShellCompilationTargetContractService.Create(PowerShellCompilationArtifactKind.Executable,
            PowerShellCompilationMode.Package, "net10.0", "win-x64", true, false,
            PowerShellCompilationExecutableOptimization.None, true);
        var manifests = new PowerShellCompilationProjectManifestService();
        manifests.Save(fixture.Project, manifests.Create(fixture.Project, source, "AcquiredDiagnostics", target));
        var workflow = new PowerShellCompilationProjectWorkflowService();
        Check(workflow.Lock(fixture.Project));
        Check(workflow.Restore(fixture.Project));
        Check(workflow.Build(fixture.Project));
        var environment = new Dictionary<string, string>
        {
            ["NUGET_PACKAGES"] = Directory.CreateDirectory(Path.Combine(fixture.Root, "empty-cache")).FullName
        };
        foreach (var operation in new[] { "explain", "diagnose" })
        {
            var result = await RunCliAsync(FindRepositoryRoot(),
                $"powershell project {operation} \"{fixture.Project}\" --output json", environment);
            Assert.Equal(0, result.ExitCode);
            using var document = JsonDocument.Parse(result.StdOut);
            var report = document.RootElement.GetProperty("result").GetProperty("targets")[0].GetProperty("diagnosticReport");
            Assert.True(report.GetProperty("canProceed").GetBoolean());
            Assert.Empty(report.GetProperty("issues").EnumerateArray());
        }
        // Without acquired evidence, a missing runtime pack is a dependency failure, not a semantic one.
        var environmentPath = Path.Combine(fixture.Root, ".powerforge", "environment", "environment.json");
        File.Move(environmentPath, environmentPath + ".saved");
        var missing = await RunCliAsync(FindRepositoryRoot(),
            $"powershell project explain \"{fixture.Project}\" --output json", environment);
        Assert.Equal(1, missing.ExitCode);
        using var failure = JsonDocument.Parse(missing.StdOut);
        var issues = failure.RootElement.GetProperty("result").GetProperty("targets")[0]
            .GetProperty("diagnosticReport").GetProperty("issues").EnumerateArray();
        Assert.Equal("Dependency", Assert.Single(issues).GetProperty("stage").GetString());

        static void Check(PowerShellCompilationProjectResult result)
            => Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Targets.Select(item => item.Message)));
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public async Task ProjectDiagnosticsCli_ShowsGroupedShapingCallsAndCorrectFailureStatus()
    {
        using var fixture = PowerShellCompilationProjectDiagnosticsTests.Fixture.Create(
            PowerShellCompilationProjectDiagnosticsTests.ShapingSource,
            PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Strict);
        var args = $"powershell project explain \"{fixture.Project}\"";
        var json = await RunCliAsync(FindRepositoryRoot(), args + " --output json");
        Assert.Equal(1, json.ExitCode);
        using var document = JsonDocument.Parse(json.StdOut);
        var result = document.RootElement;
        Assert.False(result.GetProperty("success").GetBoolean());
        var report = result.GetProperty("result").GetProperty("targets")[0].GetProperty("diagnosticReport");
        Assert.False(report.GetProperty("canProceed").GetBoolean());
        Assert.Contains(report.GetProperty("units").EnumerateArray(), group =>
            group.GetProperty("unit").GetProperty("name").GetString() == "Get-Middle" &&
            group.GetProperty("localCalls")[0].GetProperty("line").GetInt32() == 7);
        var text = await RunCliAsync(FindRepositoryRoot(), args);
        Assert.Equal(1, text.ExitCode);
        Assert.Contains("[Shaping]", text.StdOut + text.StdErr);
        Assert.Contains("Get-Leaf", text.StdOut + text.StdErr);
        Assert.Contains("7:5", text.StdOut + text.StdErr);
        var diagnose = await RunCliAsync(FindRepositoryRoot(),
            $"powershell project diagnose \"{fixture.Project}\" --output json");
        Assert.Equal(1, diagnose.ExitCode);
        using var diagnosis = JsonDocument.Parse(diagnose.StdOut);
        Assert.Contains(diagnosis.RootElement.GetProperty("result").GetProperty("targets")[0]
            .GetProperty("diagnosticReport").GetProperty("issues").EnumerateArray(),
            issue => issue.GetProperty("stage").GetString() == "Integrity");
    }
}
