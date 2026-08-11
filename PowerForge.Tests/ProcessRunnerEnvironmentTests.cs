using System.Reflection;

namespace PowerForge.Tests;

public sealed class ProcessRunnerEnvironmentTests
{
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
