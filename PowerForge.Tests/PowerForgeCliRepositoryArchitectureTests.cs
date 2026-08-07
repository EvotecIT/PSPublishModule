using System.Diagnostics;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class PowerForgeCliRepositoryArchitectureTests
{
    [Fact]
    public async Task ArchitectureVerify_ProducesMachineReadableImpactAndEvidencePlan()
    {
        var sourceRoot = FindRepositoryRoot();
        var repositoryRoot = Path.Combine(Path.GetTempPath(), "PowerForgeCliArchitecture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(repositoryRoot, ".powerforge"));
        Directory.CreateDirectory(Path.Combine(repositoryRoot, "Core"));
        try
        {
            File.WriteAllText(Path.Combine(repositoryRoot, "Core", "Core.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(repositoryRoot, "Core", "Capability.cs"), "public static class Capability { }");
            File.WriteAllText(Path.Combine(repositoryRoot, "Core", "RemovedCapability.cs"), "public static class RemovedCapability { }");
            File.WriteAllText(Path.Combine(repositoryRoot, ".powerforge", "workspace.validation.json"), """
                {
                  "schemaVersion": 1,
                  "projectRoot": "..",
                  "profiles": [ { "name": "architecture" } ],
                  "steps": [
                    { "id": "core-contract", "profiles": ["architecture"], "arguments": ["--info"] }
                  ]
                }
                """);
            var architecturePath = Path.Combine(repositoryRoot, ".powerforge", "architecture.json");
            File.WriteAllText(architecturePath, """
                {
                  "schemaVersion": 1,
                  "repositoryRoot": "..",
                  "workspaceValidationConfig": ".powerforge/workspace.validation.json",
                  "workspaceValidationProfile": "architecture",
                  "capabilities": [
                    {
                      "id": "core-capability",
                      "ownerProjects": ["Core/Core.csproj"],
                      "ownerPaths": ["Core/*.cs"],
                      "consumerProjects": [],
                      "requiredEvidenceKinds": ["contract"],
                      "evidence": [
                        {
                          "id": "core-contract",
                          "kind": "contract",
                          "stepId": "core-contract",
                          "path": "Core/Capability.cs",
                          "coversProjects": ["Core/Core.csproj"]
                        }
                      ]
                    }
                  ]
                }
                """);

            await RunProcessAsync(repositoryRoot, "git", "init", "--initial-branch=main");
            await RunProcessAsync(repositoryRoot, "git", "add", ".");
            await RunProcessAsync(
                repositoryRoot,
                "git",
                "-c",
                "user.name=PowerForge Tests",
                "-c",
                "user.email=powerforge-tests@example.invalid",
                "commit",
                "-m",
                "baseline");
            File.Delete(Path.Combine(repositoryRoot, "Core", "RemovedCapability.cs"));
            var summaryPath = Path.Combine(repositoryRoot, "architecture-summary.md");

            var result = await RunCliAsync(
                repositoryRoot,
                sourceRoot,
                "architecture",
                "verify",
                "--config",
                architecturePath,
                "--working-tree",
                "--summary-markdown",
                summaryPath,
                "--output",
                "json");

            Assert.True(result.ExitCode == 0, $"CLI exit code {result.ExitCode}\nSTDOUT:\n{result.StdOut}\nSTDERR:\n{result.StdErr}");
            using var document = JsonDocument.Parse(result.StdOut);
            var root = document.RootElement;
            Assert.True(root.GetProperty("success").GetBoolean());
            Assert.Equal("architecture.verify", root.GetProperty("command").GetString());
            var architecture = root.GetProperty("result").GetProperty("architecture");
            Assert.Equal("core-capability", architecture.GetProperty("capabilities")[0].GetProperty("id").GetString());
            Assert.True(architecture.GetProperty("capabilities")[0].GetProperty("impacted").GetBoolean());
            Assert.Contains(
                architecture.GetProperty("changedFiles").EnumerateArray(),
                item => item.GetString() == "Core/RemovedCapability.cs");
            Assert.Equal("core-contract", architecture.GetProperty("requiredValidationStepIds")[0].GetString());
            Assert.Contains(
                "Required evidence was planned but not run.",
                File.ReadAllText(summaryPath),
                StringComparison.Ordinal);
        }
        finally
        {
            try
            {
                if (Directory.Exists(repositoryRoot))
                    Directory.Delete(repositoryRoot, recursive: true);
            }
            catch
            {
                // Best-effort cleanup on Windows, where a terminating dotnet process may briefly retain a file handle.
            }
        }
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunCliAsync(
        string workingDirectory,
        string sourceRoot,
        params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("run");
        process.StartInfo.ArgumentList.Add("--project");
        process.StartInfo.ArgumentList.Add(Path.Combine(sourceRoot, "PowerForge.Cli", "PowerForge.Cli.csproj"));
        process.StartInfo.ArgumentList.Add("--configuration");
        process.StartInfo.ArgumentList.Add("Release");
        process.StartInfo.ArgumentList.Add("--framework");
        process.StartInfo.ArgumentList.Add("net10.0");
        process.StartInfo.ArgumentList.Add("--");
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var exitTask = process.WaitForExitAsync();
        if (await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(120))) != exitTask)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("PowerForge architecture CLI test timed out.");
        }

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static async Task RunProcessAsync(string workingDirectory, string fileName, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{fileName} {string.Join(" ", arguments)} failed with exit code {process.ExitCode}.\n{await standardOutput}\n{await standardError}");
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

        throw new DirectoryNotFoundException("Unable to locate PowerForge source root.");
    }
}
