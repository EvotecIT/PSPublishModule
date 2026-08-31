using System.Reflection;

namespace PowerForge.Tests;

public sealed class ProcessRunnerEnvironmentTests
{
    [Fact]
    public async Task RunAsync_exposes_started_process_before_external_work_boundary()
    {
        var order = new List<string>();
        var processId = 0;
        var request = new ProcessRunRequest(
            "dotnet",
            Path.GetTempPath(),
            new[] { "--version" },
            TimeSpan.FromSeconds(30));
        request.SetStartedProcessBoundary(value =>
        {
            processId = value;
            order.Add("process");
        });
        request.SetStartBoundary(() => order.Add("start"));

        var result = await new ProcessRunner().RunAsync(request);

        Assert.True(result.Succeeded, result.StdErr);
        Assert.True(processId > 0);
        Assert.Equal(new[] { "process", "start" }, order);
    }

    [Fact]
    public async Task RunAsync_fails_closed_when_started_process_boundary_rejects_launch()
    {
        var request = new ProcessRunRequest(
            "dotnet",
            Path.GetTempPath(),
            new[] { "--version" },
            TimeSpan.FromSeconds(30));
        request.SetStartedProcessBoundary(_ => throw new InvalidOperationException("reject started process"));

        var result = await new ProcessRunner().RunAsync(request);

        Assert.Equal(127, result.ExitCode);
        Assert.Contains("reject started process", result.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAsync_counts_the_started_process_boundary_against_the_request_timeout()
    {
        var request = new ProcessRunRequest(
            "dotnet",
            Path.GetTempPath(),
            new[] { "--version" },
            TimeSpan.FromMilliseconds(100));
        request.SetStartedProcessBoundary(_ => Thread.Sleep(250));

        var result = await new ProcessRunner().RunAsync(request);

        Assert.True(result.TimedOut);
        Assert.Equal(124, result.ExitCode);
        Assert.Equal("Timeout", result.StdErr);
    }

    [Fact]
    public async Task RunAsync_invokes_completion_boundary_before_inherited_output_pipe_drain()
    {
        if (OperatingSystem.IsWindows()) return;
        var boundary = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var request = new ProcessRunRequest(
            "/bin/sh",
            Path.GetTempPath(),
            new[] { "-c", "sleep 2 &" },
            TimeSpan.FromSeconds(10),
            captureOutput: true,
            captureError: true);
        request.SetCompletionBoundary(_ => boundary.TrySetResult());

        var run = new ProcessRunner().RunAsync(request);
        await boundary.Task.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.False(run.IsCompleted);
        var result = await run;
        Assert.True(result.Succeeded, result.StdErr);
    }

    [Fact]
    public void Completion_boundary_is_available_to_external_process_runners()
    {
        var method = typeof(ProcessRunRequest).GetMethod(
            "InvokeCompletionBoundary",
            BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(method);
    }

    [Fact]
    public async Task RunAsync_can_start_from_an_explicit_environment_allowlist()
    {
        if (OperatingSystem.IsWindows()) return;
        const string variable = "POWERFORGE_TEST_UNAPPROVED_PARENT_VALUE";
        var original = Environment.GetEnvironmentVariable(variable);
        Environment.SetEnvironmentVariable(variable, "must-not-leak");
        try
        {
            var result = await new ProcessRunner().RunAsync(new ProcessRunRequest(
                "/usr/bin/env",
                Path.GetTempPath(),
                Array.Empty<string>(),
                TimeSpan.FromSeconds(10),
                new Dictionary<string, string?> { ["PATH"] = "/usr/bin:/bin" },
                captureOutput: true,
                captureError: true,
                inheritEnvironment: false));

            Assert.True(result.Succeeded, result.StdErr);
            Assert.DoesNotContain(variable, result.StdOut, StringComparison.Ordinal);
            Assert.Contains("PATH=/usr/bin:/bin", result.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, original);
        }
    }
}
