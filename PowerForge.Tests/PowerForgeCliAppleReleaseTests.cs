using System.Diagnostics;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class PowerForgeCliAppleReleaseTests
{
    [Fact]
    public async Task AppleRelease_CliUsesDedicatedEnvelopeAndReportsLegacyConfirmation()
    {
        var repoRoot = FindRepositoryRoot();
        var tempRoot = CreateTempDirectory();

        try
        {
            var projectDirectory = Directory.CreateDirectory(Path.Combine(tempRoot, "Sample.xcodeproj"));
            File.WriteAllText(
                Path.Combine(projectDirectory.FullName, "project.pbxproj"),
                """
                {
                  MARKETING_VERSION = 1.2.0;
                  CURRENT_PROJECT_VERSION = 9;
                }
                """);
            var configPath = Path.Combine(tempRoot, "powerforge.release.json");
            WriteReleaseConfig(configPath, submitForReview: false, includeInvalidModule: true);

            var status = await RunCliAsync(
                repoRoot,
                $"\"{GetCliPath(repoRoot)}\" apple-release Status --config \"{configPath}\" --plan --summary --output json");

            Assert.True(
                status.ExitCode == 0,
                $"CLI exit code {status.ExitCode}\nSTDOUT:\n{status.StdOut}\nSTDERR:\n{status.StdErr}");
            using (var document = JsonDocument.Parse(status.StdOut))
            {
                var root = document.RootElement;
                Assert.Equal("apple-release", root.GetProperty("command").GetString());
                Assert.True(root.GetProperty("success").GetBoolean());
                var result = root.GetProperty("result");
                Assert.Equal("Status", result.GetProperty("action").GetString());
                Assert.False(result.GetProperty("requiresConfirmation").GetBoolean());
                Assert.Equal("status", Assert.Single(result.GetProperty("enabledSteps").EnumerateArray()).GetString());
            }

            var configuredDedicated = await RunCliAsync(
                repoRoot,
                $"\"{GetCliPath(repoRoot)}\" apple-release Configured --config \"{configPath}\" --plan --summary --output json");
            Assert.Equal(2, configuredDedicated.ExitCode);
            using (var rejectedDocument = JsonDocument.Parse(configuredDedicated.StdOut))
            {
                var rejected = rejectedDocument.RootElement;
                Assert.False(rejected.GetProperty("success").GetBoolean());
                Assert.Contains(
                    "requires an explicit named action",
                    rejected.GetProperty("error").GetString(),
                    StringComparison.OrdinalIgnoreCase);
            }

            var undefinedNumericAction = await RunCliAsync(
                repoRoot,
                $"\"{GetCliPath(repoRoot)}\" apple-release 999 --config \"{configPath}\" --plan --summary --output json");
            Assert.Equal(2, undefinedNumericAction.ExitCode);
            using (var rejectedDocument = JsonDocument.Parse(undefinedNumericAction.StdOut))
            {
                var rejected = rejectedDocument.RootElement;
                Assert.False(rejected.GetProperty("success").GetBoolean());
                Assert.Contains(
                    "Unknown Apple release action",
                    rejected.GetProperty("error").GetString(),
                    StringComparison.OrdinalIgnoreCase);
            }

            WriteReleaseConfig(configPath, submitForReview: true, includeInvalidModule: false);
            var configured = await RunCliAsync(
                repoRoot,
                $"\"{GetCliPath(repoRoot)}\" release --config \"{configPath}\" --plan --summary --output json");

            Assert.True(
                configured.ExitCode == 0,
                $"CLI exit code {configured.ExitCode}\nSTDOUT:\n{configured.StdOut}\nSTDERR:\n{configured.StdErr}");
            using var configuredDocument = JsonDocument.Parse(configured.StdOut);
            var configuredRoot = configuredDocument.RootElement;
            Assert.Equal("release", configuredRoot.GetProperty("command").GetString());
            Assert.True(configuredRoot.GetProperty("success").GetBoolean());
            var configuredResult = configuredRoot.GetProperty("result");
            Assert.Equal("Configured", configuredResult.GetProperty("action").GetString());
            Assert.True(configuredResult.GetProperty("requiresConfirmation").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static void WriteReleaseConfig(
        string path,
        bool submitForReview,
        bool includeInvalidModule)
        => File.WriteAllText(
            path,
            $$"""
            {
              "SchemaVersion": 1,
              {{(includeInvalidModule ? """
              "Module": {
                "ScriptPath": "missing-module-build.ps1"
              },
              """ : string.Empty)}}
              "AppleApps": {
                "ProjectRoot": ".",
                "Archive": false,
                "Upload": false,
                "SubmitForReview": {{submitForReview.ToString().ToLowerInvariant()}},
                "Apps": [
                  {
                    "Name": "Sample iOS",
                    "BundleId": "com.example.sample",
                    "Platform": "iOS",
                    "ProjectPath": "Sample.xcodeproj",
                    "Scheme": "Sample",
                    "AppStoreConnectAppId": "1234567890"
                  }
                ]
              }
            }
            """);

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunCliAsync(
        string workingDirectory,
        string arguments)
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
        if (await Task.WhenAny(exitTask, Task.Delay(TimeSpan.FromSeconds(180))) != exitTask)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }
            throw new TimeoutException("PowerForge CLI Apple release test timed out.");
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

    private static string GetCliPath(string repoRoot)
    {
        var testOutputDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = testOutputDirectory.Parent?.Name
            ?? throw new DirectoryNotFoundException(
                $"Unable to derive the current build configuration from '{AppContext.BaseDirectory}'.");
        var path = Path.Combine(
            repoRoot,
            "PowerForge.Cli",
            "bin",
            configuration,
            "net10.0",
            "PowerForge.Cli.dll");
        Assert.True(File.Exists(path), $"PowerForge CLI test dependency was not built: {path}");
        return path;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "PowerForgeCliAppleRelease-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
