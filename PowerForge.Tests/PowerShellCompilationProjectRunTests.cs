using System.Text.Json;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Project_ArtifactBuildCancellationBeforePublicationPreservesPriorOutput()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = ArtifactFixture.Create("return 7", compactPath: true);
        var spec = new PowerShellCompilationBuildSpec(fixture.ScriptPath, fixture.OutputPath, "CanceledBuild",
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Strict,
            allowUnreviewedDependencyResolution: true);
        var original = new PowerShellCompilationArtifactBuilder().Build(spec);
        Assert.True(original.Succeeded, original.Error);
        var prior = Directory.GetFiles(fixture.OutputPath, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);
        File.WriteAllText(fixture.ScriptPath, "return 9");
        using var cancellation = new CancellationTokenSource();
        var builder = new PowerShellCompilationArtifactBuilder(request =>
        {
            var verified = PowerShellStrictDependencyClosureVerifier.Verify(request);
            cancellation.Cancel();
            return verified;
        });
        Assert.ThrowsAny<OperationCanceledException>(() => builder.Build(spec, cancellation.Token));
        Assert.Equal(prior.Keys.Order(), Directory.GetFiles(fixture.OutputPath, "*", SearchOption.AllDirectories).Order());
        foreach (var pair in prior) Assert.Equal(pair.Value, File.ReadAllBytes(pair.Key));
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public async Task Project_RunRebuildsEditedSourceReusesCacheAndRejectsStaleExecution()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = ArtifactFixture.Create("return 7", compactPath: true);
        var (project, artifact) = CreateRunProject(fixture, PowerShellCompilationMode.Strict);
        var workflow = new PowerShellCompilationProjectWorkflowService();
        AssertProjectSuccess(workflow.Lock(project));
        AssertProjectSuccess(workflow.Restore(project));
        var reviewed = File.ReadAllBytes(Path.Combine(fixture.RootPath, artifact.DependencyLock));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        var options = new PowerShellCompilationProjectRunOptions { CaptureOutput = true, CaptureError = true };
        var first = await workflow.RunAsync(project, options, update =>
        {
            if (update.State == "starting")
                File.WriteAllText(Path.Combine(fixture.RootPath, "unrelated.ps1"), "return 'not a project input'");
        }, cancellation.Token);
        AssertRun(first, "7");
        var second = await workflow.RunAsync(project, options, cancellationToken: cancellation.Token);
        AssertRun(second, "7");
        var receipt = ReadRunReceipt(fixture, artifact);
        Assert.True(receipt.Manifest!.BuildCache!.Hit, receipt.Manifest.BuildCache.Reason);

        File.WriteAllText(fixture.ScriptPath, "return 9");
        var changed = await workflow.RunAsync(project, options, cancellationToken: cancellation.Token);
        AssertRun(changed, "9");
        Assert.False(ReadRunReceipt(fixture, artifact).Manifest!.BuildCache!.Hit);
        Assert.False(ReadRunReceipt(fixture, artifact).Manifest!.DependencyLockReviewed);
        Assert.NotEmpty(ReadRunReceipt(fixture, artifact).Manifest!.DevelopmentBaselineLockSha256!);
        var superseded = await workflow.RunAsync(project, options, update =>
        {
            if (update.State == "starting") File.WriteAllText(fixture.ScriptPath, "return 99");
        }, cancellation.Token);
        Assert.Null(superseded.Process);
        Assert.Contains("changed during the build", superseded.Error);
        File.WriteAllText(fixture.ScriptPath, "return 9");
        Assert.Equal(reviewed, File.ReadAllBytes(Path.Combine(fixture.RootPath, artifact.DependencyLock)));
        Assert.False(workflow.Test(project).Succeeded); // Release/test still requires the exact reviewed source lock.
        var priorArtifact = File.ReadAllBytes(changed.Build.Targets.Single().Path!);
        File.WriteAllText(fixture.ScriptPath, "function Broken {");
        var failed = await workflow.RunAsync(project, options, cancellationToken: cancellation.Token);
        Assert.Null(failed.Process);
        Assert.NotEmpty(failed.Error);
        Assert.Equal(priorArtifact, File.ReadAllBytes(changed.Build.Targets.Single().Path!));
        File.WriteAllText(fixture.ScriptPath, "return 11");
        AssertRun(await workflow.RunAsync(project, options, cancellationToken: cancellation.Token), "11");
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public async Task Project_WatchRecoversAfterInvalidSourceAndStopsOnCancellation()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = ArtifactFixture.Create("return 1", compactPath: true);
        var (project, _) = CreateRunProject(fixture, PowerShellCompilationMode.Strict);
        var workflow = new PowerShellCompilationProjectWorkflowService();
        AssertProjectSuccess(workflow.Lock(project));
        AssertProjectSuccess(workflow.Restore(project));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        var events = System.Threading.Channels.Channel.CreateUnbounded<PowerShellCompilationProjectRunEvent>();
        File.WriteAllText(fixture.ScriptPath, "function InitiallyBroken {");
        var watch = workflow.WatchAsync(project,
            new PowerShellCompilationProjectRunOptions
            {
                CaptureOutput = true, CaptureError = true, PollIntervalMilliseconds = 50, DebounceMilliseconds = 100
            }, update => events.Writer.TryWrite(update), cancellation.Token);
        try
        {
            Assert.Null((await Next("failed")).Result!.Process);
            File.WriteAllText(fixture.ScriptPath, "return 1");
            AssertRun((await Next("exited")).Result!, "1");
            await Assert.ThrowsAsync<IOException>(() => workflow.RunAsync(project, cancellationToken: cancellation.Token));
            File.WriteAllText(fixture.ScriptPath, "function Broken {");
            var failure = await Next("failed");
            Assert.Null(failure.Result!.Process);
            File.WriteAllText(fixture.ScriptPath, "return 2");
            File.WriteAllText(fixture.ScriptPath, "return 3");
            AssertRun((await Next("exited")).Result!, "3");
        }
        finally
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => watch);
        }

        async Task<PowerShellCompilationProjectRunEvent> Next(string state)
        {
            while (true)
            {
                var update = await events.Reader.ReadAsync(cancellation.Token);
                if (update.State == state) return update;
                if (update.State == "failed" && state == "exited") Assert.Fail(update.Message);
            }
        }
    }

    private static (string Project, PowerShellCompilationProjectArtifact Artifact) CreateRunProject(
        ArtifactFixture fixture, PowerShellCompilationMode mode)
    {
        var project = Path.Combine(fixture.RootPath, "powerforge.psproject.json");
        var service = new PowerShellCompilationProjectManifestService();
        var target = PowerShellCompilationTargetContractService.Create(PowerShellCompilationArtifactKind.Executable,
            mode, "net10.0", "win-x64", false, true, PowerShellCompilationExecutableOptimization.None, true);
        var manifest = service.Create(project, fixture.ScriptPath, "Development", target);
        service.Save(project, manifest);
        return (project, manifest.Artifacts.Single());
    }

    private static PowerShellCompilationBuildResult ReadRunReceipt(ArtifactFixture fixture, PowerShellCompilationProjectArtifact artifact)
        => JsonSerializer.Deserialize<PowerShellCompilationBuildResult>(
            File.ReadAllText(Path.Combine(fixture.RootPath, ".powerforge", "build", artifact.Name + ".json")),
            PowerShellCompilationProjectManifestService.JsonOptions)!;

    private static void AssertRun(PowerShellCompilationProjectRunResult result, string expected)
    {
        Assert.True(string.IsNullOrEmpty(result.Error), result.Error);
        Assert.NotNull(result.Process);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, result.Process.StdOut.Trim());
        Assert.Empty(result.Process.StdErr);
    }

    private static void AssertProjectSuccess(PowerShellCompilationProjectResult result)
        => Assert.True(result.Succeeded, string.Join(Environment.NewLine, result.Targets.Select(target => target.Message)));
}
