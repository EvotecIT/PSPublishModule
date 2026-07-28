using System.Diagnostics;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class PowerForgeCliAppleReleaseTests
{
    private const string ApprovedSourceCommit = "0123456789abcdef0123456789abcdef01234567";

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
            var apiKeyDirectory = Directory.CreateDirectory(Path.Combine(tempRoot, ".appstoreconnect"));
            File.WriteAllText(Path.Combine(apiKeyDirectory.FullName, "AuthKey_TEST123456.p8"), "test-private-key");
            File.WriteAllText(Path.Combine(tempRoot, "governance.json"), """{ "schemaVersion": 1, "appId": "1234567890", "accessibility": [ { "deviceFamily": "IPHONE", "supportsVoiceover": true } ] }""");
            var configPath = Path.Combine(tempRoot, "powerforge.release.json");
            WriteReleaseConfig(configPath, submitForReview: false, includeInvalidModule: true, includeGovernance: true);

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

            var doctor = await RunCliAsync(
                repoRoot,
                $"\"{GetCliPath(repoRoot)}\" apple-release Doctor --config \"{configPath}\" --plan --summary --output json");
            Assert.Equal(0, doctor.ExitCode);
            using (var doctorDocument = JsonDocument.Parse(doctor.StdOut))
            {
                var steps = doctorDocument.RootElement.GetProperty("result").GetProperty("enabledSteps").EnumerateArray().Select(step => step.GetString()).ToArray();
                Assert.Contains("doctor", steps);
                Assert.Contains("checkGovernance", steps);
            }

            var advance = await RunCliAsync(
                repoRoot,
                $"\"{GetCliPath(repoRoot)}\" apple-release Advance --config \"{configPath}\" --plan --summary --output json");
            Assert.Equal(0, advance.ExitCode);
            using (var advanceDocument = JsonDocument.Parse(advance.StdOut))
            {
                var result = advanceDocument.RootElement.GetProperty("result");
                Assert.Equal("Advance", result.GetProperty("action").GetString());
                Assert.True(result.GetProperty("requiresConfirmation").GetBoolean());
                Assert.Contains(
                    result.GetProperty("enabledSteps").EnumerateArray(),
                    static step => step.GetString() == "stopBeforeReview");
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

    [Fact]
    public async Task AppleScreenshots_ManifestResolvesBlankAppIdFromSelectedReleaseTarget()
    {
        var repoRoot = FindRepositoryRoot();
        var tempRoot = CreateTempDirectory();

        try
        {
            var screenshotDirectory = Directory.CreateDirectory(Path.Combine(tempRoot, "screenshots"));
            File.WriteAllBytes(
                Path.Combine(screenshotDirectory.FullName, "01-home.png"),
                Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2n1sAAAAASUVORK5CYII="));
            var screenshotConfigPath = Path.Combine(tempRoot, "screenshots.json");
            File.WriteAllText(
                screenshotConfigPath,
                """
                {
                  "AppId": "",
                  "UseReleaseVersion": true,
                  "Platform": "iOS",
                  "Locale": "en-US",
                  "ScreenshotSets": [
                    {
                      "ScreenshotDisplayType": "APP_IPHONE_67",
                      "Path": "screenshots",
                      "AllowedDimensions": [ "1x1" ]
                    }
                  ],
                  "Quality": {
                    "Enabled": true,
                    "MinimumFileBytes": 1,
                    "MinimumKilobytesPerMegapixel": 0,
                    "RequireApprovalManifest": true,
                    "ApprovalManifestPath": "approval.json"
                  }
                }
                """);
            var releaseConfigPath = Path.Combine(tempRoot, "powerforge.release.json");
            WriteReleaseConfig(releaseConfigPath, submitForReview: false, includeInvalidModule: false);
            var manifestPath = Path.Combine(tempRoot, "approval.json");

            var result = await RunCliAsync(
                repoRoot,
                $"\"{GetCliPath(repoRoot)}\" apple-screenshots manifest --config \"{screenshotConfigPath}\" --release-config \"{releaseConfigPath}\" --target Sample --version 1.5.0 --source-commit {ApprovedSourceCommit} --approved-by release-owner --allowed-root \"{screenshotDirectory.FullName}\" --out \"{manifestPath}\" --output json");

            Assert.True(
                result.ExitCode == 0,
                $"CLI exit code {result.ExitCode}\nSTDOUT:\n{result.StdOut}\nSTDERR:\n{result.StdErr}");
            var manifest = JsonSerializer.Deserialize<AppStoreConnectScreenshotApprovalManifest>(
                File.ReadAllText(manifestPath),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                });
            Assert.NotNull(manifest);
            Assert.Equal("1234567890", manifest!.AppId);
            Assert.Equal("1.5.0", manifest.VersionString);
            Assert.Equal(ApprovedSourceCommit, manifest.SourceCommit);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task AppleGovernance_ValidatesOfflineAndBlocksUnconfirmedApplyBeforeCredentials()
    {
        var repoRoot = FindRepositoryRoot();
        var tempRoot = CreateTempDirectory();
        try
        {
            var configPath = Path.Combine(tempRoot, "governance.json");
            File.WriteAllText(configPath,
                """
                {
                  "schemaVersion": 1,
                  "appId": "1234567890",
                  "accessibility": [
                    { "deviceFamily": "IPHONE", "supportsVoiceover": true }
                  ]
                }
                """);

            var validation = await RunCliAsync(
                repoRoot,
                $"\"{GetCliPath(repoRoot)}\" apple-governance validate --config \"{configPath}\" --output json");
            Assert.Equal(0, validation.ExitCode);
            using (var document = JsonDocument.Parse(validation.StdOut))
            {
                Assert.Equal("apple-governance validate", document.RootElement.GetProperty("command").GetString());
                Assert.True(document.RootElement.GetProperty("success").GetBoolean());
                Assert.Empty(document.RootElement.GetProperty("result").GetProperty("findings").EnumerateArray());
            }

            var apply = await RunCliAsync(
                repoRoot,
                $"\"{GetCliPath(repoRoot)}\" apple-governance apply --config \"{configPath}\" --output json");
            Assert.Equal(2, apply.ExitCode);
            using var rejected = JsonDocument.Parse(apply.StdOut);
            Assert.Contains("requires --confirm", rejected.RootElement.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static void WriteReleaseConfig(
        string path,
        bool submitForReview,
        bool includeInvalidModule,
        bool includeGovernance = false)
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
                "AppStoreConnectApiKeyPath": ".appstoreconnect/AuthKey_TEST123456.p8",
                "AppStoreConnectApiKeyId": "TEST123456",
                "AppStoreConnectApiIssuerId": "00000000-0000-0000-0000-000000000000",
                {{(includeGovernance ? "\"GovernanceConfigPath\": \"governance.json\"," : string.Empty)}}
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
