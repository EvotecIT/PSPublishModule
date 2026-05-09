using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class PowerForgeCliProjectReleaseTests
{
    [Fact]
    public async Task ProjectRelease_CliPreservesProjectDefaultsFromConfig()
    {
        var repoRoot = FindRepositoryRoot();
        var tempRoot = CreateTempDirectory();

        try
        {
            var outputRoot = Path.Combine(repoRoot, "Artifacts", "CliProjectReleaseTests", Guid.NewGuid().ToString("N"));
            var configPath = Path.Combine(tempRoot, "project.release.json");
            var service = new PowerForgeProjectConfigurationJsonService();
            service.Save(
                new ConfigurationProject
                {
                    Name = "PowerForge.Cli",
                    ProjectRoot = repoRoot,
                    Release = new ConfigurationProjectRelease
                    {
                        PublishToolGitHub = true,
                        SkipRestore = true,
                        SkipBuild = true,
                        ToolOutput = new[] { ConfigurationProjectReleaseOutputType.Portable }
                    },
                    Output = new ConfigurationProjectOutput
                    {
                        OutputRoot = outputRoot
                    },
                    Targets = new[]
                    {
                        new ConfigurationProjectTarget
                        {
                            Name = "PowerForge.Cli",
                            ProjectPath = Path.Combine(repoRoot, "PowerForge.Cli", "PowerForge.Cli.csproj"),
                            Framework = "net8.0",
                            Runtimes = new[] { "win-x64" }
                        }
                    }
                },
                configPath,
                overwrite: true);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = $"run --project \"{Path.Combine(repoRoot, "PowerForge.Cli", "PowerForge.Cli.csproj")}\" -c Release --framework net10.0 -- project release --config \"{configPath}\" --plan --output json",
                    WorkingDirectory = repoRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            var stdoutBuffer = new StringBuilder();
            var stderrBuffer = new StringBuilder();
            var stdoutClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var stderrClosed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null)
                    stdoutClosed.TrySetResult();
                else
                    stdoutBuffer.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null)
                    stderrClosed.TrySetResult();
                else
                    stderrBuffer.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            if (!process.WaitForExit(120_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
                throw new TimeoutException("PowerForge CLI project release plan test timed out.");
            }

            var drainTask = Task.WhenAll(stdoutClosed.Task, stderrClosed.Task);
            if (await Task.WhenAny(drainTask, Task.Delay(TimeSpan.FromSeconds(10))) != drainTask)
            {
                try { process.CancelOutputRead(); } catch { /* best effort */ }
                try { process.CancelErrorRead(); } catch { /* best effort */ }
            }

            var stdout = stdoutBuffer.ToString();
            var stderr = stderrBuffer.ToString();

            Assert.True(process.ExitCode == 0, $"CLI exit code {process.ExitCode}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            Assert.True(root.GetProperty("success").GetBoolean());

            var result = root.GetProperty("result");
            var plan = result.GetProperty("dotNetToolPlan");
            Assert.False(plan.GetProperty("restore").GetBoolean());
            Assert.False(plan.GetProperty("build").GetBoolean());

            var bundles = plan.GetProperty("bundles");
            Assert.Equal(1, bundles.GetArrayLength());

            var bundleSteps = plan.GetProperty("steps").EnumerateArray()
                .Where(step => string.Equals(step.GetProperty("kind").GetString(), "Bundle", StringComparison.Ordinal))
                .ToArray();
            Assert.Single(bundleSteps);
            Assert.Contains(outputRoot, bundleSteps[0].GetProperty("bundleOutputPath").GetString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "PowerForge.Cli", "PowerForge.Cli.csproj")))
                return current.FullName;

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root for PowerForge CLI tests.");
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "PowerForgeCliProjectRelease-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
