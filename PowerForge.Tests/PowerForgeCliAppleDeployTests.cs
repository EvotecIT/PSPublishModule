using System.Diagnostics;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class PowerForgeCliAppleDeployTests
{
    [Fact]
    public async Task AppleDeploy_plan_uses_configured_target_profile_and_platform_defaults()
    {
        var repoRoot = FindRepositoryRoot();
        var tempRoot = Path.Combine(Path.GetTempPath(), "PowerForgeCliAppleDeploy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(
                tempRoot,
                "Sample.xcodeproj"));
            File.WriteAllText(
                Path.Combine(project.FullName, "project.pbxproj"),
                string.Empty);
            var configPath = Path.Combine(tempRoot, "powerforge.release.json");
            File.WriteAllText(configPath, """
            {
              "SchemaVersion": 1,
              "AppleApps": {
                "ProjectRoot": ".",
                "LocalDeployment": {
                  "DefaultPlatform": "iOS",
                  "DefaultDevice": "EvoPhone",
                  "Configuration": "Debug",
                  "InstallRoot": "/Applications",
                  "DefaultProfile": "Plus",
                  "Profiles": [
                    {
                      "Name": "Free",
                      "Environment": { "SAMPLE_SANDBOX": "1" }
                    },
                    {
                      "Name": "Plus",
                      "Environment": { "SAMPLE_SANDBOX": "0" }
                    }
                  ]
                },
                "Apps": [
                  {
                    "Name": "Sample iOS",
                    "BundleId": "com.example.sample",
                    "Platform": "iOS",
                    "ProjectPath": "Sample.xcodeproj",
                    "Scheme": "Sample"
                  },
                  {
                    "Name": "Sample Mac",
                    "BundleId": "com.example.sample",
                    "Platform": "macOS",
                    "ArchiveVariant": "MacCatalyst",
                    "ProjectPath": "Sample.xcodeproj",
                    "Scheme": "Sample"
                  },
                  {
                    "Name": "Sample CarPlay capability",
                    "BundleId": "com.example.sample",
                    "Platform": "iOS",
                    "ProductRole": "Capability",
                    "ProjectPath": "Sample.xcodeproj",
                    "Scheme": "Sample"
                  }
                ]
              }
            }
            """);
            InitializeGitRepository(tempRoot);
            var expectedRevision = RunGit(
                tempRoot,
                "rev-parse",
                "HEAD").Trim().ToLowerInvariant();

            var ios = await RunCliAsync(repoRoot, $"\"{GetCliPath(repoRoot)}\" apple-deploy --config \"{configPath}\" --plan --output json");
            Assert.Equal(0, ios.ExitCode);
            using (var document = JsonDocument.Parse(ios.StdOut))
            {
                var result = document.RootElement.GetProperty("result");
                Assert.Equal("Sample iOS", result.GetProperty("target").GetString());
                Assert.Equal("iOS", result.GetProperty("platform").GetString());
                Assert.Equal("Plus", result.GetProperty("profile").GetString());
                Assert.Equal("EvoPhone", result.GetProperty("device").GetString());
                Assert.Equal("Debug", result.GetProperty("configuration").GetString());
                Assert.Equal(
                    expectedRevision,
                    result.GetProperty("sourceRevision").GetString());
            }

            var textPlan = await RunCliAsync(
                repoRoot,
                $"\"{GetCliPath(repoRoot)}\" apple-deploy --config \"{configPath}\" --plan");
            Assert.Equal(0, textPlan.ExitCode);
            Assert.Contains($"Source: {expectedRevision}", textPlan.StdOut);

            var mac = await RunCliAsync(repoRoot, $"\"{GetCliPath(repoRoot)}\" apple-deploy --config \"{configPath}\" --platform macOS --profile Free --plan --output json");
            Assert.Equal(0, mac.ExitCode);
            using var macDocument = JsonDocument.Parse(mac.StdOut);
            var macResult = macDocument.RootElement.GetProperty("result");
            Assert.Equal("Sample Mac", macResult.GetProperty("target").GetString());
            Assert.Equal("MacCatalyst", macResult.GetProperty("archiveVariant").GetString());
            Assert.Equal("Free", macResult.GetProperty("profile").GetString());
            Assert.Equal("/Applications", macResult.GetProperty("installRoot").GetString());

            File.WriteAllText(Path.Combine(tempRoot, ".gitignore"), "Local.xcconfig\n");
            RunGit(tempRoot, "add", ".gitignore");
            RunGit(tempRoot, "commit", "-m", "ignore local input");
            File.WriteAllText(Path.Combine(tempRoot, "Local.xcconfig"), "SETTING = local");

            var unsafePlan = await RunCliAsync(
                repoRoot,
                $"\"{GetCliPath(repoRoot)}\" apple-deploy --config \"{configPath}\" --plan --output json");
            Assert.Equal(1, unsafePlan.ExitCode);
            Assert.Contains("Git-ignored files", unsafePlan.StdErr + unsafePlan.StdOut, StringComparison.Ordinal);
            Assert.Contains("Local.xcconfig", unsafePlan.StdErr + unsafePlan.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task AppleDeploy_plan_uses_the_execution_repository_root()
    {
        var repoRoot = FindRepositoryRoot();
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "PowerForgeCliAppleDeploy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var appRoot = Directory.CreateDirectory(Path.Combine(tempRoot, "App"));
            var project = Directory.CreateDirectory(Path.Combine(
                appRoot.FullName,
                "Sample.xcodeproj"));
            File.WriteAllText(
                Path.Combine(project.FullName, "project.pbxproj"),
                string.Empty);
            var configPath = Path.Combine(appRoot.FullName, "powerforge.release.json");
            File.WriteAllText(configPath, """
            {
              "SchemaVersion": 1,
              "AppleApps": {
                "ProjectRoot": ".",
                "LocalDeployment": {
                  "DefaultPlatform": "iOS",
                  "DefaultDevice": "EvoPhone"
                },
                "Apps": [
                  {
                    "Name": "Sample iOS",
                    "BundleId": "com.example.sample",
                    "Platform": "iOS",
                    "ProjectPath": "Sample.xcodeproj",
                    "Scheme": "Sample"
                  }
                ]
              }
            }
            """);
            File.WriteAllText(
                Path.Combine(tempRoot, ".gitignore"),
                "RepositoryLocal.xcconfig\n");
            InitializeGitRepository(tempRoot);
            File.WriteAllText(
                Path.Combine(tempRoot, "RepositoryLocal.xcconfig"),
                "SETTING = local");

            var result = await RunCliAsync(
                repoRoot,
                $"\"{GetCliPath(repoRoot)}\" apple-deploy --config \"{configPath}\" --plan --output json");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains(
                "RepositoryLocal.xcconfig",
                result.StdErr + result.StdOut,
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task AppleDeploy_plan_rejects_absolute_xcode_inputs()
    {
        var repoRoot = FindRepositoryRoot();
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "PowerForgeCliAppleDeploy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(
                tempRoot,
                "Sample.xcodeproj"));
            File.WriteAllText(
                Path.Combine(project.FullName, "project.pbxproj"),
                """
                {
                    objects = {
                        AA0000000000000000000001 = {
                            isa = PBXFileReference;
                            path = /tmp/PowerForge-External.xcconfig;
                            sourceTree = "<absolute>";
                        };
                    };
                }
                """);
            var configPath = Path.Combine(
                tempRoot,
                "powerforge.release.json");
            File.WriteAllText(configPath, """
            {
              "SchemaVersion": 1,
              "AppleApps": {
                "ProjectRoot": ".",
                "LocalDeployment": {
                  "DefaultPlatform": "iOS",
                  "DefaultDevice": "EvoPhone"
                },
                "Apps": [
                  {
                    "Name": "Sample iOS",
                    "BundleId": "com.example.sample",
                    "Platform": "iOS",
                    "ProjectPath": "Sample.xcodeproj",
                    "Scheme": "Sample"
                  }
                ]
              }
            }
            """);
            InitializeGitRepository(tempRoot);

            var result = await RunCliAsync(
                repoRoot,
                $"\"{GetCliPath(repoRoot)}\" apple-deploy --config \"{configPath}\" --plan --output json");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains(
                "Absolute Xcode project inputs",
                result.StdErr + result.StdOut,
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task AppleDeploy_mac_plan_rejects_an_install_root_inside_the_repository()
    {
        var repoRoot = FindRepositoryRoot();
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "PowerForgeCliAppleDeploy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(tempRoot, "Sample.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            var configPath = Path.Combine(tempRoot, "powerforge.release.json");
            File.WriteAllText(configPath, """
            {
              "SchemaVersion": 1,
              "AppleApps": {
                "ProjectRoot": ".",
                "LocalDeployment": {
                  "DefaultPlatform": "macOS"
                },
                "Apps": [
                  {
                    "Name": "Sample Mac",
                    "BundleId": "com.example.sample",
                    "Platform": "macOS",
                    "ProjectPath": "Sample.xcodeproj",
                    "Scheme": "Sample"
                  }
                ]
              }
            }
            """);
            InitializeGitRepository(tempRoot);
            var installRoot = Path.Combine(tempRoot, "Applications");

            var result = await RunCliAsync(
                repoRoot,
                $"\"{GetCliPath(repoRoot)}\" apple-deploy --config \"{configPath}\" --install-root \"{installRoot}\" --plan --output json");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains(
                nameof(AppleMacAppDeploymentRequest.InstallRoot),
                result.StdErr + result.StdOut,
                StringComparison.Ordinal);
            Assert.Contains("must be outside BuildRoot", result.StdErr + result.StdOut, StringComparison.Ordinal);
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
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await stdout, await stderr);
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
        throw new DirectoryNotFoundException("Unable to locate repository root.");
    }

    private static string GetCliPath(string repoRoot)
        => Path.Combine(repoRoot, "PowerForge.Cli", "bin", "Release", "net10.0", "PowerForge.Cli.dll");

    private static void InitializeGitRepository(string workingDirectory)
    {
        AppleDeploymentTestFixture.WriteSharedSchemes(
            workingDirectory,
            "Sample");
        RunGit(workingDirectory, "init");
        RunGit(workingDirectory, "config", "user.name", "PowerForge Tests");
        RunGit(
            workingDirectory,
            "config",
            "user.email",
            "powerforge-tests@example.invalid");
        RunGit(workingDirectory, "add", ".");
        RunGit(workingDirectory, "commit", "-m", "fixture");
    }

    private static string RunGit(
        string workingDirectory,
        params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            }
        };
        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var standardOutput = process.StandardOutput.ReadToEnd();
        var standardError = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(standardError);
        return standardOutput;
    }

}
