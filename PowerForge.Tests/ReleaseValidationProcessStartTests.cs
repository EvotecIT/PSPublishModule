namespace PowerForge.Tests;

public sealed class ReleaseValidationProcessStartTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.ProcessStart.Tests", Guid.NewGuid().ToString("N"));
    public ReleaseValidationProcessStartTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Launch_failure_is_distinct_from_child_exit_127(bool ownProcessTree, bool missingDirectory)
    {
        var request = FailureRequest(missingDirectory);

        var result = await new ProcessRunner(ownProcessTree).RunAsync(request);

        Assert.True(result.StartFailed);
        Assert.False(result.Succeeded);
        Assert.False(result.TimedOut);
        Assert.Equal(127, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.StdErr));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Rejected_started_process_boundary_marks_launch_failed(bool ownProcessTree)
    {
        var request = ShellRequest(_root);
        var processId = 0;
        request.SetStartedProcessBoundary(id => {
            processId = id;
            throw new InvalidOperationException("test launch boundary rejected");
        });

        var result = await new ProcessRunner(ownProcessTree).RunAsync(request);

        Assert.True(processId > 0);
        Assert.True(result.StartFailed);
        Assert.False(result.Succeeded);
        Assert.Contains("test launch boundary rejected", result.StdErr, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Genuine_child_exit_127_does_not_mark_launch_failed(bool ownProcessTree)
    {
        var result = await new ProcessRunner(ownProcessTree).RunAsync(ShellRequest(_root));

        Assert.False(result.StartFailed, result.StdErr);
        Assert.Equal(127, result.ExitCode);
        Assert.False(result.Succeeded);
        Assert.False(result.TimedOut);
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(127, false)]
    [InlineData(null, true)]
    [InlineData(127, true)]
    public async Task Release_report_rejects_launch_failure_even_when_exit_code_is_allowed(int? expectedExitCode, bool missingDirectory)
    {
        var failedRequest = FailureRequest(missingDirectory);
        var command = Command(failedRequest, expectedExitCode);

        var report = await new ReleaseValidationService().RunAsync(new() { Commands = [command] },
            request: new() { ProjectRoot = _root });

        Assert.False(report.Success);
        Assert.Contains(command.Name, Assert.Single(report.Errors), StringComparison.Ordinal);
        Assert.Empty(report.Checks);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(127)]
    public async Task Release_report_accepts_genuine_child_exit_127_when_allowed(int? expectedExitCode)
    {
        var command = Command(ShellRequest(_root), expectedExitCode);

        var report = await new ReleaseValidationService().RunAsync(new() { Commands = [command] },
            request: new() { ProjectRoot = _root });

        Assert.True(report.Success, string.Join("; ", report.Errors));
        Assert.Empty(report.Errors);
        Assert.Equal(command.Name, Assert.Single(report.Checks));
    }

    [Fact]
    public void Failed_start_overrides_zero_exit_code_success()
    {
        var result = new ProcessRunResult(0, "", "startup rejected", "probe", TimeSpan.Zero, false, false, false, true);

        Assert.True(result.StartFailed);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Existing_constructor_keeps_successful_launch_default()
    {
        var result = new ProcessRunResult(0, "ok", "", "probe", TimeSpan.Zero, false, false, false);

        Assert.False(result.StartFailed);
        Assert.True(result.Succeeded);
    }

    [ReleaseValidationLinuxFact]
    public void Direct_start_requirement_rejects_unverifiable_libc_fallback_before_launch()
    {
        var marker = Path.Combine(_root, "must-not-run");
        var info = new System.Diagnostics.ProcessStartInfo("/bin/sh") { WorkingDirectory = _root };
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add("touch \"$1\"");
        info.ArgumentList.Add("probe");
        info.ArgumentList.Add(marker);
        using var process = new UnixOwnedProcessExecution(info) {
            RequireDirectStart = true,
            ConfigureWorkingDirectory = (_, _) => throw new EntryPointNotFoundException()
        };

        var error = Assert.Throws<PlatformNotSupportedException>(() => process.Start());

        Assert.Contains("posix_spawn_file_actions_addchdir_np", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, process.Id);
        Assert.False(File.Exists(marker));
    }

    [Fact]
    public async Task Release_runner_requires_verified_start_from_its_process_owner()
    {
        var runner = new ReleaseValidationProcessRunner(new InspectRequestRunner());
        await runner.RunAsync(ShellRequest(_root));
    }

    private sealed class InspectRequestRunner : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Assert.True(request.RequireDirectStart);
            return Task.FromResult(new ProcessRunResult(0, "", "", request.FileName, TimeSpan.Zero, false));
        }
    }

    private ProcessRunRequest FailureRequest(bool missingDirectory)
        => missingDirectory ? ShellRequest(Path.Combine(_root, "missing-directory"))
            : new ProcessRunRequest(Path.Combine(_root, "missing-executable"), _root, [], TimeSpan.FromSeconds(10));

    private static ProcessRunRequest ShellRequest(string workingDirectory)
        => OperatingSystem.IsWindows()
            ? new ProcessRunRequest(Path.Combine(Environment.SystemDirectory, "cmd.exe"), workingDirectory,
                ["/d", "/c", "exit /b 127"], TimeSpan.FromSeconds(10))
            : new ProcessRunRequest("/bin/sh", workingDirectory, ["-c", "exit 127"], TimeSpan.FromSeconds(10));

    private static ReleaseCommandValidation Command(ProcessRunRequest request, int? expectedExitCode)
        => new() { Name = "launch-probe", FileName = request.FileName, WorkingDirectory = request.WorkingDirectory,
            Arguments = request.Arguments.ToArray(), TimeoutSeconds = 10, ExpectedExitCode = expectedExitCode };

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
