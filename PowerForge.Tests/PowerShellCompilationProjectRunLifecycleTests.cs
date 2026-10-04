using System.Diagnostics;
using PowerForge;

namespace PowerForge.Tests;

public sealed partial class PowerShellCompilationArtifactBuilderTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "PowerShellCompilerGate")]
    public async Task Project_RunRejectsArtifactReplacementAtLaunchBoundary(bool replaceExecutable)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = ArtifactFixture.Create("[IO.File]::ReadAllText(\"$PSScriptRoot/data.txt\")", compactPath: true);
        File.WriteAllText(Path.Combine(fixture.RootPath, "data.txt"), "original");
        var (project, artifact) = CreateRunProject(fixture, PowerShellCompilationMode.Package);
        var manifests = new PowerShellCompilationProjectManifestService();
        var manifest = manifests.Load(project);
        manifest.Resources.Include = new[] { "data.txt" };
        manifest.Artifacts[0].Target = PowerShellCompilationTargetContractService.Create(
            PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Package,
            "net10.0", "win-x64", false, false, PowerShellCompilationExecutableOptimization.None, true);
        manifests.Save(project, manifest);
        var workflow = new PowerShellCompilationProjectWorkflowService();
        AssertProjectSuccess(workflow.Lock(project));
        AssertProjectSuccess(workflow.Restore(project));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        var started = false;
        var result = await workflow.RunAsync(project,
            new PowerShellCompilationProjectRunOptions { CaptureOutput = true, CaptureError = true }, update =>
            {
                if (update.State == "running") started = true;
                if (update.State != "starting") return;
                var receipt = ReadRunReceipt(fixture, artifact);
                var path = replaceExecutable ? receipt.ArtifactPath! :
                    receipt.Manifest!.Files.Single(file => file.Role == "GeneratedAssembly").Path;
                // Appending preserves a runnable PE, including the assembly's embedded
                // resource. Neither replacement may run without the final inventory check.
                using var output = new FileStream(path, FileMode.Append, FileAccess.Write);
                output.WriteByte(0x20);
            }, cancellation.Token);
        Assert.False(started);
        Assert.Null(result.Process);
        Assert.Contains("Artifact output changed before launch", result.Error);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public async Task Project_RunInvalidatesCalleesResourcesAndRejectsChangedProjectContract()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var fixture = ArtifactFixture.Create("""
            . "$PSScriptRoot/helper.ps1"
            Get-LocalValue
            [IO.File]::ReadAllText("$PSScriptRoot/data.txt")
            """, compactPath: true);
        var helper = Path.Combine(fixture.RootPath, "helper.ps1");
        var resource = Path.Combine(fixture.RootPath, "data.txt");
        File.WriteAllText(helper, "function Get-LocalValue { 'first' }");
        File.WriteAllText(resource, "resource-one");
        var (project, _) = CreateRunProject(fixture, PowerShellCompilationMode.Package);
        var manifests = new PowerShellCompilationProjectManifestService();
        var manifest = manifests.Load(project);
        manifest.Resources.Include = new[] { "data.txt" };
        manifests.Save(project, manifest);
        var workflow = new PowerShellCompilationProjectWorkflowService();
        AssertProjectSuccess(workflow.Lock(project));
        AssertProjectSuccess(workflow.Restore(project));
        var options = new PowerShellCompilationProjectRunOptions { CaptureOutput = true, CaptureError = true };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        var initial = await workflow.RunAsync(project, options, cancellationToken: cancellation.Token);
        AssertRun(initial, "first" + Environment.NewLine + "resource-one");
        File.WriteAllText(helper, "function Get-LocalValue { 'second' }");
        File.WriteAllText(resource, "resource-two");
        AssertRun(await workflow.RunAsync(project, options, cancellationToken: cancellation.Token),
            "second" + Environment.NewLine + "resource-two");

        // A changed dependency declaration cannot be accepted as an ordinary source-content edit.
        File.WriteAllText(helper, "#requires -Modules Missing.Development.Dependency\nfunction Get-LocalValue { 'third' }");
        var dependency = await workflow.RunAsync(project, options, cancellationToken: cancellation.Token);
        Assert.Null(dependency.Process);
        Assert.NotEmpty(dependency.Error);
        File.WriteAllText(helper, "function Get-LocalValue { 'second' }");
        manifest.Artifacts[0].EmitIr = !manifest.Artifacts[0].EmitIr;
        manifests.Save(project, manifest);
        var contract = await workflow.RunAsync(project, options, cancellationToken: cancellation.Token);
        Assert.Null(contract.Process);
        Assert.Contains("restore", contract.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public async Task Project_WatchStopsRunningApplicationAndDescendantBeforeRebuildAndCancellation()
    {
        if (!OperatingSystem.IsWindows()) return;
        const string source = """
            $start = [Diagnostics.ProcessStartInfo]::new('pwsh')
            $start.Arguments = '-NoProfile -NonInteractive -Command Start-Sleep -Seconds 300'
            $start.UseShellExecute = $false
            $start.CreateNoWindow = $true
            $child = [Diagnostics.Process]::Start($start)
            [IO.File]::WriteAllText($args[0], "$PID,$($child.Id)")
            while ($true) { Start-Sleep -Milliseconds 50 }
            """;
        using var fixture = ArtifactFixture.Create(source, compactPath: true);
        var marker = Path.GetTempFileName();
        var (project, _) = CreateRunProject(fixture, PowerShellCompilationMode.Package);
        var workflow = new PowerShellCompilationProjectWorkflowService();
        AssertProjectSuccess(workflow.Lock(project));
        AssertProjectSuccess(workflow.Restore(project));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMinutes(8));
        var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var watch = workflow.WatchAsync(project, new PowerShellCompilationProjectRunOptions
        {
            Arguments = new[] { marker }, CaptureOutput = true, CaptureError = true,
            PollIntervalMilliseconds = 50, DebounceMilliseconds = 100
        }, update => { if (update.State == "failed") errors.Enqueue(update.Message); }, cancellation.Token);
        Process[] observed = Array.Empty<Process>();
        try
        {
            var first = await ReadProcessesAfter(string.Empty);
            observed = first.Processes;
            File.WriteAllText(fixture.ScriptPath, source + "\n# edited source\n");
            var second = await ReadProcessesAfter(first.Marker);
            Assert.All(first.Processes, process => Assert.True(process.HasExited));
            observed = observed.Concat(second.Processes).ToArray();
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => watch);
            Assert.All(second.Processes, process => Assert.True(process.HasExited));
        }
        finally
        {
            cancellation.Cancel();
            try { await watch; } catch (OperationCanceledException) { }
            foreach (var process in observed) process.Dispose();
            File.Delete(marker);
        }

        async Task<(string Marker, Process[] Processes)> ReadProcessesAfter(string previous)
        {
            while (true)
            {
                if (errors.TryDequeue(out var error)) Assert.Fail(error);
                var content = File.ReadAllText(marker);
                if (content.Length > 0 && content != previous)
                    return (content, content.Split(',').Select(value => Process.GetProcessById(int.Parse(value))).ToArray());
                await Task.Delay(50, cancellation.Token);
            }
        }
    }
}
