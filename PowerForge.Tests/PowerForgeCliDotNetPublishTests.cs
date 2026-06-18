using System.Diagnostics;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class PowerForgeCliDotNetPublishTests
{
    [Fact]
    public async Task DotNetPublish_CliProjectRootOverrideWinsOverExplicitConfigRoot()
    {
        var repoRoot = FindRepositoryRoot();
        var tempRoot = CreateTempDirectory();

        try
        {
            var configPath = Path.Combine(tempRoot, "powerforge.dotnetpublish.json");
            var spec = new DotNetPublishSpec
            {
                DotNet = new DotNetPublishDotNetOptions
                {
                    ProjectRoot = tempRoot,
                    SolutionPath = "PSPublishModule.sln",
                    Restore = false,
                    Build = false,
                    Runtimes = new[] { "win-x64" }
                },
                Targets = new[]
                {
                    new DotNetPublishTarget
                    {
                        Name = "PowerForge.Cli",
                        ProjectPath = "PowerForge.Cli/PowerForge.Cli.csproj",
                        Publish = new DotNetPublishPublishOptions
                        {
                            Framework = "net10.0",
                            Runtimes = new[] { "win-x64" },
                            UseStaging = false,
                            Zip = false
                        }
                    }
                }
            };
            File.WriteAllText(configPath, JsonSerializer.Serialize(spec, new JsonSerializerOptions { WriteIndented = true }));

            var (exitCode, stdout, stderr) = await RunCliAsync(
                repoRoot,
                $"run --project \"{Path.Combine(repoRoot, "PowerForge.Cli", "PowerForge.Cli.csproj")}\" -c Release --framework net10.0 -- dotnet publish --config \"{configPath}\" --project-root \"{repoRoot}\" --validate --output json");

            Assert.True(exitCode == 0, $"CLI exit code {exitCode}\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");

            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            Assert.True(root.GetProperty("success").GetBoolean());
            Assert.Equal(repoRoot, root.GetProperty("plan").GetProperty("projectRoot").GetString(), ignoreCase: true);
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
            throw new TimeoutException("PowerForge CLI dotnet publish validation test timed out.");
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
        var path = Path.Combine(Path.GetTempPath(), "PowerForgeCliDotNetPublish-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
