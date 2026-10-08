using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Project_RestoreAndExecutionSupportLongInternalPathsWithCompactOutput()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = ArtifactFixture.Create("return 7", compactPath: true);
        var projectRoot = Path.Combine(fixture.RootPath, new string('d', 225 - fixture.RootPath.Length - 1));
        Directory.CreateDirectory(projectRoot);
        var sourcePath = Path.Combine(projectRoot, "main.ps1");
        File.WriteAllText(sourcePath, "return 7");
        var projectPath = Path.Combine(projectRoot, "powerforge.psproject.json");
        var target = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Strict, "net10.0", "win-x64",
            selfContained: false, singleFile: false, PowerShellCompilationExecutableOptimization.None,
            explicitContract: true);
        var manifests = new PowerShellCompilationProjectManifestService();
        var manifest = manifests.Create(projectPath, sourcePath, "Q", target);
        var artifact = Assert.Single(manifest.Artifacts);
        artifact.Name = "long";
        artifact.OutputDirectory = "out";
        manifests.Save(projectPath, manifest);
        var restoreRoot = Path.Combine(projectRoot, ".powerforge", "environment", "restore", artifact.Name);
        Assert.True(restoreRoot.Length >= 260, restoreRoot);
        var workflow = new PowerShellCompilationProjectWorkflowService();
        Assert.True(workflow.Lock(projectPath).Succeeded);
        var restored = workflow.Restore(projectPath);
        Assert.True(restored.Succeeded, string.Join(Environment.NewLine, restored.Targets.Select(item => item.Message)));
        var offline = workflow.Restore(projectPath, offline: true);
        Assert.True(offline.Succeeded, string.Join(Environment.NewLine, offline.Targets.Select(item => item.Message)));
        var build = workflow.Build(projectPath);
        Assert.True(build.Succeeded, string.Join(Environment.NewLine, build.Targets.Select(item => item.Message)));
        var tested = workflow.Test(projectPath);
        Assert.True(tested.Succeeded, string.Join(Environment.NewLine, tested.Targets.Select(item => item.Message)));
        var run = RunProcess(Assert.Single(build.Targets).Path!);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("7", run.StandardOutput.Trim());
        Assert.Empty(run.StandardError);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData(PowerShellCompilationMode.Strict, "return 7", "7")]
    [InlineData(PowerShellCompilationMode.Package, "Write-Host 'ready'", "ready")]
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
