using System.Runtime.InteropServices;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [LinuxX64RuntimeClosureFact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Project_LinuxBundledSelfContainedRuntimeClosureRunsAfterInstall()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Linux runtime-pack closure qualification requires Linux.");
        // This exact bundled profile includes Linux System.Net.Ping's Process.Start
        // reference and CoreLib QCall while the SDK drops optional diagnostic binaries.
        using var fixture = ArtifactFixture.Create("return 7", compactPath: true);
        var project = Path.Combine(fixture.RootPath, "powerforge.psproject.json");
        var manifests = new PowerShellCompilationProjectManifestService();
        var target = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Strict,
            "net10.0", "linux-x64", selfContained: true, singleFile: true,
            PowerShellCompilationExecutableOptimization.None, explicitContract: true);
        var projectManifest = manifests.Create(project, fixture.ScriptPath, "LinuxRuntimeClosure", target);
        manifests.Save(project, projectManifest);
        var workflow = new PowerShellCompilationProjectWorkflowService();
        AssertProjectSuccess(workflow.Lock(project));
        AssertProjectSuccess(workflow.Restore(project));
        var build = workflow.Build(project);
        AssertProjectSuccess(build);
        var primary = Assert.Single(build.Targets).Path!;
        var outputRoot = Path.GetDirectoryName(primary)!;
        using (var evidence = JsonDocument.Parse(File.ReadAllText(
                   Assert.Single(Directory.EnumerateFiles(outputRoot, "*.powerforge-compilation.json")))))
        {
            var manifest = evidence.RootElement;
            Assert.True(manifest.GetProperty("dependencyClosureVerified").GetBoolean());
            Assert.False(manifest.GetProperty("requiresPowerShellRuntime").GetBoolean());
            Assert.False(manifest.GetProperty("allowsPowerShellRuntimeEvaluation").GetBoolean());
            Assert.True(manifest.GetProperty("selfContained").GetBoolean());
            Assert.True(manifest.GetProperty("singleFile").GetBoolean());
            Assert.Equal("None", manifest.GetProperty("optimization").GetString());
            var closure = manifest.GetProperty("dependencyClosure");
            Assert.True(closure.GetProperty("verified").GetBoolean());
            Assert.Equal("linux-x64", closure.GetProperty("runtimeIdentifier").GetString());
            Assert.Contains(closure.GetProperty("deliveredDependencies").EnumerateArray(),
                dependency => dependency.GetProperty("identity").GetString()!.StartsWith("System.Net.Ping,", StringComparison.Ordinal));
            Assert.Contains(closure.GetProperty("targetAbiNativeImports").EnumerateArray(),
                import => import.GetString() == "System.Private.CoreLib:QCall");
        }
        AssertProjectSuccess(workflow.Test(project));
        AssertProjectSuccess(workflow.Pack(project));
        var install = workflow.Install(project);
        AssertProjectSuccess(install);
        var installRoot = Assert.Single(install.Targets).Path!;
        var repeatedInstall = workflow.Install(project);
        AssertProjectSuccess(repeatedInstall);
        Assert.Equal(installRoot, Assert.Single(repeatedInstall.Targets).Path);
        var artifactOutputRoot = Path.Combine(fixture.RootPath, Assert.Single(projectManifest.Artifacts).OutputDirectory);
        var installed = Path.Combine(installRoot, "artifact", Path.GetRelativePath(artifactOutputRoot, primary));
        var run = RunProcess(installed);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("7", run.StandardOutput.Trim());
        Assert.Empty(run.StandardError);
        Assert.Equal(PowerShellCompilationProjectManifestService.ComputeSha256(primary),
            PowerShellCompilationProjectManifestService.ComputeSha256(installed));
        Assert.Equal(File.GetUnixFileMode(primary), File.GetUnixFileMode(installed));
    }
}

internal sealed class LinuxX64RuntimeClosureFactAttribute : FactAttribute
{
    public LinuxX64RuntimeClosureFactAttribute()
    {
        if (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64)
            Skip = "Linux x64 runtime-pack closure qualification requires a Linux x64 host.";
    }
}
