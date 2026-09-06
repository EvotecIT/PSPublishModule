using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData(PowerShellCompilationMode.Strict, "return 7", "7")]
    [InlineData(PowerShellCompilationMode.Hybrid, "Write-Host 'ready'", "ready")]
    public void Project_RestoreBuildAndRunPreservesSingleFilePackageClosure(
        PowerShellCompilationMode mode, string source, string expectedOutput)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = ArtifactFixture.Create(source, compactPath: true);
        var projectPath = Path.Combine(fixture.RootPath, "powerforge.psproject.json");
        var target = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.Executable, mode, "net10.0", "win-x64",
            selfContained: false, singleFile: true, PowerShellCompilationExecutableOptimization.None,
            explicitContract: true);
        var manifests = new PowerShellCompilationProjectManifestService();
        manifests.Save(projectPath, manifests.Create(projectPath, fixture.ScriptPath, "SingleFileRestore", target));
        var workflow = new PowerShellCompilationProjectWorkflowService();
        Assert.True(workflow.Lock(projectPath).Succeeded);
        var restore = workflow.Restore(projectPath);
        Assert.True(restore.Succeeded, string.Join(Environment.NewLine, restore.Targets.Select(item => item.Message)));
        var offline = workflow.Restore(projectPath, offline: true);
        Assert.True(offline.Succeeded, string.Join(Environment.NewLine, offline.Targets.Select(item => item.Message)));
        var build = workflow.Build(projectPath);
        Assert.True(build.Succeeded, string.Join(Environment.NewLine, build.Targets.Select(item => item.Message)));
        var run = RunProcess(Assert.Single(build.Targets).Path!);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal(expectedOutput, run.StandardOutput.Trim());
        Assert.Empty(run.StandardError);
    }
}
