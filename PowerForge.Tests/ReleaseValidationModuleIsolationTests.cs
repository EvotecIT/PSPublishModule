using System.IO.Compression;

namespace PowerForge.Tests;

public sealed class ReleaseValidationModuleIsolationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.ModuleIsolation.Tests", Guid.NewGuid().ToString("N"));
    public ReleaseValidationModuleIsolationTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(false, "success")]
    [InlineData(true, "success")]
    [InlineData(false, "failure")]
    [InlineData(true, "failure")]
    [InlineData(false, "cancel")]
    [InlineData(true, "cancel")]
    public async Task Every_host_receives_a_pristine_module_copy_and_never_mutates_the_input(bool archive, string outcome)
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        File.WriteAllText(Path.Combine(source, "Example.psd1"), "@{ ModuleVersion = '1.2.3' }");
        foreach (var directory in new[] { "bin", "obj", ".git", "empty" }) { Directory.CreateDirectory(Path.Combine(source, directory)); }
        foreach (var directory in new[] { "bin", "obj", ".git" }) { File.WriteAllText(Path.Combine(source, directory, "payload.txt"), "original"); }
        var input = source;
        if (archive) {
            input = Path.Combine(_root, "module.zip");
            ZipFile.CreateFromDirectory(source, input);
        }
        var archiveBytes = archive ? File.ReadAllBytes(input) : null;
        using var cancellation = new CancellationTokenSource();
        var runner = new MutatingRunner(outcome == "failure", !archive, outcome == "cancel" ? cancellation : null);
        var task = new ReleaseValidationService(runner).RunAsync(new() { Modules = [new() {
            Path = input, Manifest = "Example.psd1", RequiredFiles = ["bin/payload.txt", "obj/payload.txt", ".git/payload.txt"],
            ProbeScript = "probe.ps1", Hosts = ["host-one", "host-two"]
        }] }, request: new() { ProjectRoot = _root }, cancellationToken: cancellation.Token);
        if (outcome == "cancel") {
            var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
            Assert.Equal(cancellation.Token, error.CancellationToken);
        } else {
            var report = await task;
            Assert.Equal(outcome == "success", report.Success);
            if (outcome == "failure") { Assert.Contains("failed", Assert.Single(report.Errors)); }
        }
        Assert.Equal(outcome == "success" ? 2 : 1, runner.ModuleRoots.Count);
        Assert.Equal(runner.ModuleRoots.Count, runner.ModuleRoots.Distinct().Count());
        Assert.All(runner.ModuleRoots, path => { Assert.NotEqual(source, path); Assert.False(Directory.Exists(path)); });
        Assert.All(runner.ProbeRoots, path => Assert.False(Directory.Exists(path)));
        foreach (var directory in new[] { "bin", "obj", ".git" }) { Assert.Equal("original", File.ReadAllText(Path.Combine(source, directory, "payload.txt"))); }
        Assert.False(File.Exists(Path.Combine(source, "created-by-probe.txt")));
        if (archive) { Assert.Equal(archiveBytes, File.ReadAllBytes(input)); }
    }

    [Fact]
    public async Task Real_PowerShell_probe_writes_only_to_its_isolated_module_payload()
    {
        var source = Directory.CreateDirectory(Path.Combine(_root, "live-module")).FullName;
        File.WriteAllText(Path.Combine(source, "Example.psd1"), "@{ ModuleVersion = '1.2.3' }");
        File.WriteAllText(Path.Combine(source, "payload.txt"), "original");
        var evidence = Path.Combine(_root, "observed.txt");
        var script = Path.Combine(source, "probe.ps1");
        File.WriteAllText(script, "if ((Get-Content -LiteralPath './payload.txt' -Raw) -ne 'original') { throw 'Relative payload was not pristine' }\n" +
            "Set-Content -LiteralPath './payload.txt' -Value 'relative-change'\n" +
            "[IO.File]::WriteAllText((Join-Path $env:POWERFORGE_MODULE_PATH 'payload.txt'), 'probe-change')\n" +
            "[IO.File]::WriteAllText((Join-Path $PSScriptRoot 'script-marker.txt'), 'script-change')\n" +
            "[IO.File]::AppendAllText('" + evidence.Replace("'", "''") + "', $env:POWERFORGE_MODULE_PATH + [Environment]::NewLine)\n");
        var report = await new ReleaseValidationService().RunAsync(new() { Modules = [new() {
            Path = "{ProjectRoot}", Manifest = "Example.psd1", ProbeScript = script, Hosts = ["pwsh", "pwsh"]
        }] }, request: new() { ProjectRoot = source });
        Assert.True(report.Success, string.Join("; ", report.Errors));
        var observed = File.ReadAllLines(evidence);
        Assert.Equal(2, observed.Length);
        Assert.Equal(2, observed.Distinct().Count());
        Assert.All(observed, path => { Assert.NotEqual(source, path); Assert.False(Directory.Exists(path)); });
        Assert.Equal("original", File.ReadAllText(Path.Combine(source, "payload.txt")));
        Assert.False(File.Exists(Path.Combine(source, "script-marker.txt")));
    }

    private sealed class MutatingRunner(bool fail, bool expectEmptyDirectory, CancellationTokenSource? cancellation) : IProcessRunner
    {
        internal List<string> ModuleRoots { get; } = [];
        internal List<string> ProbeRoots { get; } = [];
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            var module = request.EnvironmentVariables!["POWERFORGE_MODULE_PATH"]!;
            Assert.Equal(module, request.WorkingDirectory);
            ModuleRoots.Add(module);
            ProbeRoots.Add(request.EnvironmentVariables["POWERFORGE_TEST_ROOT"]!);
            if (expectEmptyDirectory) { Assert.True(Directory.Exists(Path.Combine(module, "empty"))); }
            foreach (var directory in new[] { "bin", "obj", ".git" }) {
                var path = Path.Combine(module, directory, "payload.txt");
                Assert.Equal("original", File.ReadAllText(path));
                File.WriteAllText(path, "modified by " + request.FileName);
            }
            File.WriteAllText(Path.Combine(module, "created-by-probe.txt"), "probe output");
            cancellation?.Cancel();
            return Task.FromResult(new ProcessRunResult(fail ? 7 : 0, "", "", request.FileName, TimeSpan.Zero, false));
        }
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
