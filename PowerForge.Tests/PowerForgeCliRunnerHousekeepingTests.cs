using System.Diagnostics;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class PowerForgeCliRunnerHousekeepingTests
{
    [Fact]
    public async Task GitHubHousekeeping_ResolvesDotNetRootRelativeToConfigFile()
    {
        var repoRoot = FindRepositoryRoot();
        var tempRoot = CreateTempDirectory();

        try
        {
            var configRoot = Directory.CreateDirectory(Path.Combine(tempRoot, "config")).FullName;
            var invocationRoot = Directory.CreateDirectory(Path.Combine(tempRoot, "invocation")).FullName;
            Directory.CreateDirectory(Path.Combine(configRoot, "runner", "_work", "_temp"));
            Directory.CreateDirectory(Path.Combine(configRoot, "dotnet", "sdk"));

            var configPath = Path.Combine(configRoot, "github-housekeeping.json");
            File.WriteAllText(
                configPath,
                """
                {
                  "DryRun": true,
                  "Artifacts": { "Enabled": false },
                  "Caches": { "Enabled": false },
                  "Runner": {
                    "Enabled": true,
                    "RunnerTempPath": "runner/_work/_temp",
                    "WorkRootPath": "runner/_work",
                    "RunnerRootPath": "runner",
                    "DiagnosticsRootPath": "runner/_diag",
                    "ToolCachePath": "runner/_work/_tool",
                    "DotNetRootPath": "dotnet",
                    "MinFreeGb": null,
                    "Aggressive": false,
                    "CleanDiagnostics": false,
                    "CleanRunnerTemp": false,
                    "CleanActionsCache": false,
                    "CleanWorkspaces": false,
                    "CleanToolCache": false,
                    "ClearDotNetCaches": false,
                    "PruneDotNetSdks": false,
                    "PruneDocker": false
                  }
                }
                """);

            var (exitCode, stdout, stderr) = await RunCliAsync(
                invocationRoot,
                $"run --project \"{Path.Combine(repoRoot, "PowerForge.Cli", "PowerForge.Cli.csproj")}\" -c Release --framework net10.0 -- github housekeeping --config \"{configPath}\" --dry-run --output json");

            Assert.True(exitCode == 0, $"CLI exit code {exitCode}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

            using var document = JsonDocument.Parse(stdout);
            var runner = document.RootElement.GetProperty("result").GetProperty("runner");
            Assert.Equal(
                Path.GetFullPath(Path.Combine(configRoot, "dotnet")),
                runner.GetProperty("dotNetRootPath").GetString(),
                ignoreCase: true);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunCliAsync(string workingDirectory, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var exitTask = process.WaitForExitAsync();

        if (await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(120))) != exitTask)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw new TimeoutException("PowerForge CLI runner housekeeping path test timed out.");
        }

        return (process.ExitCode, await stdoutTask, await stderrTask);
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
        var path = Path.Combine(Path.GetTempPath(), "PowerForgeCliRunnerHousekeeping-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
