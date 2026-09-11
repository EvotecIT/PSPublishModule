using System.Text;

namespace PowerForge.Tests;

public sealed partial class ReleaseValidationClosureRegressionTests
{
    private const int OutputCap = 1_048_576;

    [Theory]
    [InlineData("command")]
    [InlineData("module")]
    [InlineData("tool")]
    [InlineData("consumer")]
    [InlineData("signature")]
    [InlineData("script")]
    public async Task Every_validation_process_boundary_has_the_finite_capture_cap(string lane)
    {
        var calls = new List<ProcessRunRequest>();
        var package = lane is "tool" or "consumer" or "signature" ? Package("Example.Tool.1.2.3.nupkg") : null;
        var runner = new Runner((request, _) => {
            calls.Add(request);
            if (lane == "consumer" && request.Arguments[0] == "restore") {
                var cache = request.EnvironmentVariables!["NUGET_PACKAGES"]!;
                var restored = Directory.CreateDirectory(Path.Combine(cache, "example.tool", "1.2.3")).FullName;
                File.Copy(package!, Path.Combine(restored, "example.tool.1.2.3.nupkg"));
            }
            return Task.FromResult(Success());
        });
        var service = new ReleaseValidationService(runner);
        if (lane == "command") {
            var result = await service.RunCommandAsync(new() { FileName = "probe", WorkingDirectory = _root });
            Assert.True(result.Succeeded);
        } else if (lane == "script") {
            var result = new PowerForgeReleaseValidationService(new NullLogger(), runner)
                .Run(new() { FilePath = Payload("probe.ps1") }, Context(), _root, CancellationToken.None);
            Assert.True(result.Succeeded, result.StdErr);
        } else {
            var spec = new ReleaseValidationSpec();
            if (lane == "module") {
                Payload("Example.psd1", "@{ ModuleVersion = '1.2.3' }");
                spec.Modules = [new() { Path = _root, Manifest = "Example.psd1", ProbeScript = Payload("probe.ps1"), Hosts = ["probe"] }];
            } else if (lane == "tool") {
                spec.Tools = [new() { PackageRoot = _root, PackageId = "Example.Tool", CommandName = "example", IncludeManifestInstall = true }];
            } else {
                spec.Packages = new() { Path = _root, Items = [new() { Id = "Example.Tool" }], VerifySignatures = lane == "signature" };
                if (lane == "consumer") {
                    var source = Directory.CreateDirectory(Path.Combine(_root, "consumer")).FullName;
                    File.WriteAllText(Path.Combine(source, "Probe.csproj"), "<Project />");
                    spec.Consumers = [new() { SourceDirectory = source, ProjectFile = "Probe.csproj", Frameworks = ["net10.0"] }];
                }
            }
            var report = await service.RunAsync(spec, request: new() { ProjectRoot = _root });
            if (lane == "signature") {
                Assert.False(report.Success);
                Assert.Contains("unsigned", Assert.Single(report.Errors), StringComparison.OrdinalIgnoreCase);
            } else Assert.True(report.Success, string.Join("\n", report.Errors));
        }

        Assert.NotEmpty(calls);
        Assert.All(calls, request => {
            Assert.Equal(OutputCap, request.MaxCapturedOutputCharacters);
            Assert.True(request.CaptureOutput);
            Assert.True(request.CaptureError);
        });
        if (lane == "tool") Assert.Equal(3, calls.Count);
        if (lane == "consumer") Assert.Equal(2, calls.Count);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Ignoring_exit_code_never_ignores_either_output_limit(bool stdoutExceeded, bool stderrExceeded)
    {
        var runner = new Runner((_, _) => Task.FromResult(new ProcessRunResult(0, "partial out", "partial err", "probe", TimeSpan.Zero,
            false, stdoutExceeded, stderrExceeded)));
        var report = await new ReleaseValidationService(runner).RunAsync(new() {
            Commands = [new() { FileName = "probe", ExpectedExitCode = null }]
        }, request: new() { ProjectRoot = _root });

        Assert.False(report.Success);
        Assert.Single(report.Errors);
        Assert.Empty(report.Checks);
    }

    [Fact]
    public async Task Real_noisy_command_is_capped_and_fails_validation_even_without_exit_assertion()
    {
        var windows = OperatingSystem.IsWindows();
        var script = windows ? "[Console]::Out.Write(('x' * 2097152))" : "head -c 2097152 /dev/zero | tr '\\000' x";
        var executable = windows
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe")
            : "/bin/sh";
        string[] arguments = windows
            ? ["-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))]
            : ["-c", script];
        ProcessRunResult? captured = null;
        var runner = new Runner(async (request, token) => {
            captured = await new ProcessRunner(ownProcessTree: true).RunAsync(request, token);
            return captured;
        });

        var report = await new ReleaseValidationService(runner).RunAsync(new() { Commands = [new() {
            FileName = executable, Arguments = arguments, ExpectedExitCode = null, TimeoutSeconds = 15
        }] }, request: new() { ProjectRoot = _root }).WaitAsync(TimeSpan.FromSeconds(25));

        Assert.NotNull(captured);
        Assert.False(captured.TimedOut);
        Assert.Equal(0, captured.ExitCode);
        Assert.True(captured.StandardOutputLimitExceeded);
        Assert.False(captured.StandardErrorLimitExceeded);
        Assert.Equal(OutputCap, captured.StdOut.Length);
        Assert.False(report.Success);
        Assert.Single(report.Errors);
        Assert.Empty(report.Checks);
    }
}
