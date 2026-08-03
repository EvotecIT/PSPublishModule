using System.Diagnostics;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class PowerForgeCliAppleReleaseTests
{
    private const string ApprovedSourceCommit = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public async Task AppleRelease_JsonRedactionPreservesPropertyNames()
    {
        var repoRoot = FindRepositoryRoot();
        var tempRoot = CreateTempDirectory();
        try
        {
            var projectDirectory = Directory.CreateDirectory(Path.Combine(tempRoot, "Sample.xcodeproj"));
            File.WriteAllText(
                Path.Combine(projectDirectory.FullName, "project.pbxproj"),
                "{ MARKETING_VERSION = 1.2.0; CURRENT_PROJECT_VERSION = 9; }");
            File.WriteAllText(Path.Combine(tempRoot, "result"), "test-private-key");
            var configPath = Path.Combine(tempRoot, "powerforge.release.json");
            WriteReleaseConfig(configPath, submitForReview: false, includeInvalidModule: false, apiKeyPath: "result");

            var result = await RunCliAsync(
                repoRoot,
                $"\"{GetCliPath(repoRoot)}\" release --config \"{configPath}\" --plan --summary --output json");

            Assert.True(result.ExitCode == 0, $"STDOUT:\n{result.StdOut}\nSTDERR:\n{result.StdErr}");
            using var document = JsonDocument.Parse(result.StdOut);
            Assert.True(document.RootElement.TryGetProperty("result", out var releaseResult));
            Assert.Equal("Configured", releaseResult.GetProperty("action").GetString());
            Assert.DoesNotContain("\"[REDACTED]\":", result.StdOut, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

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
                Assert.Matches("^[0-9A-F]{64}$", result.GetProperty("planSha256").GetString()!);
                Assert.False(result.GetProperty("requiresConfirmation").GetBoolean());
                Assert.Equal("status", Assert.Single(result.GetProperty("enabledSteps").EnumerateArray()).GetString());
            }

            var detailedStatus = await RunCliAsync(
                repoRoot,
                $"\"{GetCliPath(repoRoot)}\" apple-release Status --config \"{configPath}\" --plan --output json");
            Assert.Equal(0, detailedStatus.ExitCode);
            Assert.DoesNotContain("TEST123456", detailedStatus.StdOut + detailedStatus.StdErr, StringComparison.Ordinal);
            Assert.DoesNotContain("00000000-0000-0000-0000-000000000000", detailedStatus.StdOut + detailedStatus.StdErr, StringComparison.Ordinal);
            Assert.DoesNotContain("AuthKey_TEST123456.p8", detailedStatus.StdOut + detailedStatus.StdErr, StringComparison.Ordinal);

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
    public async Task AppleRelease_CliRedactsCredentialMetadataFromFailureJson()
    {
        var repoRoot = FindRepositoryRoot();
        var tempRoot = CreateTempDirectory();
        try
        {
            var projectDirectory = Directory.CreateDirectory(Path.Combine(tempRoot, "Sample.xcodeproj"));
            File.WriteAllText(Path.Combine(projectDirectory.FullName, "project.pbxproj"),
                "{ MARKETING_VERSION = 1.2.0; CURRENT_PROJECT_VERSION = 9; }");
            var configPath = Path.Combine(tempRoot, "powerforge.release.json");
            WriteReleaseConfig(configPath, submitForReview: false, includeInvalidModule: false);

            var result = await RunCliAsync(
                repoRoot,
                $"\"{GetCliPath(repoRoot)}\" apple-release Status --config \"{configPath}\" --plan --output json");

            Assert.NotEqual(0, result.ExitCode);
            var combined = result.StdOut + result.StdErr;
            Assert.DoesNotContain("TEST123456", combined, StringComparison.Ordinal);
            Assert.DoesNotContain("00000000-0000-0000-0000-000000000000", combined, StringComparison.Ordinal);
            Assert.DoesNotContain("AuthKey_TEST123456.p8", combined, StringComparison.Ordinal);
            using var document = JsonDocument.Parse(result.StdOut);
            Assert.False(document.RootElement.GetProperty("success").GetBoolean());
            Assert.Contains("[REDACTED]", document.RootElement.GetProperty("error").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
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
                $"\"{GetCliPath(repoRoot)}\" apple-screenshots manifest --config \"{screenshotConfigPath}\" --release-config \"{releaseConfigPath}\" --target Sample --version 1.5.0 --source-commit {ApprovedSourceCommit} --approved-by release-owner --allowed-root \"{screenshotDirectory.FullName}\" --out \"{manifestPath}\" --write-root \"{tempRoot}\" --output json");

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
    public async Task AppleScreenshots_ManifestDerivesVersionAndSourceFromCaptureProvenance()
    {
        var repoRoot = FindRepositoryRoot();
        var tempRoot = CreateTempDirectory();
        try
        {
            var screenshotDirectory = Directory.CreateDirectory(Path.Combine(tempRoot, "screenshots"));
            File.WriteAllBytes(Path.Combine(screenshotDirectory.FullName, "01-home.png"),
                Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2n1sAAAAASUVORK5CYII="));
            var configPath = Path.Combine(tempRoot, "screenshots.json");
            File.WriteAllText(configPath,
                """{ "AppId": "1234567890", "Platform": "iOS", "Locale": "en-US", "ScreenshotSets": [ { "ScreenshotDisplayType": "APP_IPHONE_67", "Path": "screenshots", "AllowedDimensions": [ "1x1" ] } ], "Quality": { "Enabled": true, "MinimumFileBytes": 1, "MinimumKilobytesPerMegapixel": 0 } }""");
            var provenancePath = Path.Combine(tempRoot, "powerforge-apple-screenshot-provenance.json");
            File.WriteAllText(provenancePath,
                $$"""{ "schemaVersion": 2, "repository": "EvotecIT/Sample", "captureRunId": "123", "sourceCommit": "{{ApprovedSourceCommit}}", "marketingVersion": "1.5.0", "workflowRef": "EvotecIT/Sample/.github/workflows/apple-screenshots.yml@refs/heads/main", "xcodeVersion": "Xcode 26", "runtime": "macOS 26 arm64", "device": "Mac", "theme": "all", "scenario": "app-store", "screenshots": [ { "path": "01-home.png", "sha256": "d268b9b4a10c5990e181efed7c66f7369e43f3382bdef6c6ea9858098e0fab95", "width": 1, "height": 1 } ] }""");
            var manifestPath = Path.Combine(tempRoot, "approval.json");

            var result = await RunCliAsync(repoRoot,
                $"\"{GetCliPath(repoRoot)}\" apple-screenshots manifest --config \"{configPath}\" --capture-provenance \"{provenancePath}\" --expected-repository EvotecIT/Sample --expected-workflow-ref EvotecIT/Sample/.github/workflows/apple-screenshots.yml@refs/heads/main --approved-by release-owner --allowed-root \"{screenshotDirectory.FullName}\" --out \"{manifestPath}\" --output json");

            Assert.Equal(0, result.ExitCode);
            var manifest = JsonSerializer.Deserialize<AppStoreConnectScreenshotApprovalManifest>(
                File.ReadAllText(manifestPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } });
            Assert.NotNull(manifest);
            Assert.Equal("1.5.0", manifest!.VersionString);
            Assert.Equal(ApprovedSourceCommit, manifest.SourceCommit);
            Assert.Equal("Xcode 26", manifest.XcodeVersion);
            Assert.Equal("123", manifest.CaptureRunId);
            Assert.Equal("EvotecIT/Sample", manifest.CaptureRepository);
            Assert.Equal("EvotecIT/Sample/.github/workflows/apple-screenshots.yml@refs/heads/main", manifest.CaptureWorkflowRef);

            var mismatch = await RunCliAsync(repoRoot,
                $"\"{GetCliPath(repoRoot)}\" apple-screenshots manifest --config \"{configPath}\" --capture-provenance \"{provenancePath}\" --expected-repository Other/Repo --expected-workflow-ref EvotecIT/Sample/.github/workflows/apple-screenshots.yml@refs/heads/main --approved-by release-owner --allowed-root \"{screenshotDirectory.FullName}\" --out \"{manifestPath}\" --output json");
            Assert.Equal(1, mismatch.ExitCode);
            Assert.Contains("does not match expected repository", mismatch.StdOut + mismatch.StdErr, StringComparison.OrdinalIgnoreCase);

            File.WriteAllText(provenancePath,
                $$"""{ "schemaVersion": 2, "repository": "EvotecIT/Sample", "captureRunId": "123", "sourceCommit": "{{ApprovedSourceCommit}}", "marketingVersion": "1.5.0", "workflowRef": "EvotecIT/Sample/.github/workflows/apple-screenshots.yml@refs/heads/main", "xcodeVersion": "Xcode 26", "runtime": "macOS 26 arm64", "device": "Mac", "theme": "all", "scenario": "app-store", "screenshots": [ { "path": "01-home.png", "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "width": 1, "height": 1 } ] }""");
            var byteMismatch = await RunCliAsync(repoRoot,
                $"\"{GetCliPath(repoRoot)}\" apple-screenshots manifest --config \"{configPath}\" --capture-provenance \"{provenancePath}\" --expected-repository EvotecIT/Sample --expected-workflow-ref EvotecIT/Sample/.github/workflows/apple-screenshots.yml@refs/heads/main --approved-by release-owner --allowed-root \"{screenshotDirectory.FullName}\" --out \"{manifestPath}\" --output json");
            Assert.Equal(1, byteMismatch.ExitCode);
            Assert.Contains("do not exactly match", byteMismatch.StdOut + byteMismatch.StdErr, StringComparison.OrdinalIgnoreCase);

            File.WriteAllText(provenancePath,
                $$"""{ "schemaVersion": 2, "repository": "EvotecIT/Sample", "captureRunId": "123", "sourceCommit": "{{ApprovedSourceCommit}}", "marketingVersion": "1.5.0", "workflowRef": "EvotecIT/Sample/.github/workflows/apple-screenshots.yml@refs/heads/main", "xcodeVersion": "Xcode 26", "runtime": "macOS 26 arm64", "device": "Mac", "theme": "all", "scenario": "app-store", "screenshots": [ { "path": "/01-home.png", "sha256": "d268b9b4a10c5990e181efed7c66f7369e43f3382bdef6c6ea9858098e0fab95", "width": 1, "height": 1 } ] }""");
            var unsafePath = await RunCliAsync(repoRoot,
                $"\"{GetCliPath(repoRoot)}\" apple-screenshots manifest --config \"{configPath}\" --capture-provenance \"{provenancePath}\" --expected-repository EvotecIT/Sample --expected-workflow-ref EvotecIT/Sample/.github/workflows/apple-screenshots.yml@refs/heads/main --approved-by release-owner --allowed-root \"{screenshotDirectory.FullName}\" --out \"{manifestPath}\" --output json");
            Assert.Equal(1, unsafePath.ExitCode);
            Assert.Contains("unsafe screenshot path", unsafePath.StdOut + unsafePath.StdErr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task AppleScreenshots_ManifestRejectsSymlinkOutputEscapeFromTrustedWriteRoot()
    {
        if (OperatingSystem.IsWindows()) return;
        var repoRoot = FindRepositoryRoot();
        var tempRoot = CreateTempDirectory();
        var outsideRoot = CreateTempDirectory();
        var linkPath = Path.Combine(tempRoot, "manifest-link");

        try
        {
            var screenshotDirectory = Directory.CreateDirectory(Path.Combine(tempRoot, "screenshots"));
            File.WriteAllBytes(Path.Combine(screenshotDirectory.FullName, "01-home.png"),
                Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9Wl2n1sAAAAASUVORK5CYII="));
            var screenshotConfigPath = Path.Combine(tempRoot, "screenshots.json");
            File.WriteAllText(screenshotConfigPath,
                """{ "AppId": "1234567890", "Platform": "iOS", "Locale": "en-US", "ScreenshotSets": [ { "ScreenshotDisplayType": "APP_IPHONE_67", "Path": "screenshots", "AllowedDimensions": [ "1x1" ] } ] }""");
            Directory.CreateSymbolicLink(linkPath, outsideRoot);
            var manifestPath = Path.Combine(linkPath, "approval.json");

            var result = await RunCliAsync(repoRoot,
                $"\"{GetCliPath(repoRoot)}\" apple-screenshots manifest --config \"{screenshotConfigPath}\" --version 1.5.0 --source-commit {ApprovedSourceCommit} --approved-by release-owner --allowed-root \"{screenshotDirectory.FullName}\" --out \"{manifestPath}\" --write-root \"{tempRoot}\" --output json");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("link or reparse point", result.StdOut + result.StdErr, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(outsideRoot, "approval.json")));
        }
        finally
        {
            if (Directory.Exists(linkPath)) Directory.Delete(linkPath);
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
            if (Directory.Exists(outsideRoot)) Directory.Delete(outsideRoot, recursive: true);
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

            var compactValidation = await RunCliAsync(
                repoRoot,
                $"\"{GetCliPath(repoRoot)}\" apple-governance validate --config \"{configPath}\" --summary --output json");
            Assert.Equal(0, compactValidation.ExitCode);
            using (var document = JsonDocument.Parse(compactValidation.StdOut))
            {
                var result = document.RootElement.GetProperty("result");
                Assert.Equal(0, result.GetProperty("driftCount").GetInt32());
                Assert.Equal(0, result.GetProperty("findingCount").GetInt32());
                Assert.False(result.TryGetProperty("changes", out _));
            }

            var apply = await RunCliAsync(
                repoRoot,
                $"\"{GetCliPath(repoRoot)}\" apple-governance apply --config \"{configPath}\" --output json");
            Assert.Equal(2, apply.ExitCode);
            using var rejected = JsonDocument.Parse(apply.StdOut);
            Assert.Contains("requires --confirm", rejected.RootElement.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);

            var confirmedWithoutPlan = await RunCliAsync(
                repoRoot,
                $"\"{GetCliPath(repoRoot)}\" apple-governance apply --config \"{configPath}\" --confirm --output json");
            Assert.Equal(2, confirmedWithoutPlan.ExitCode);
            using (var missingPlan = JsonDocument.Parse(confirmedWithoutPlan.StdOut))
            {
                Assert.Contains("requires --reviewed-plan", missingPlan.RootElement.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
            }

            var reviewedPlanPath = Path.Combine(tempRoot, "reviewed-plan.json");
            File.WriteAllText(reviewedPlanPath,
                """{ "appId": "1234567890", "checkedAtUtc": "2026-07-28T00:00:00Z", "changes": [], "findings": [], "driftCount": 0, "blockedCount": 0, "isConverged": true, "canApply": true }""");
            var missingKeyPath = Path.Combine(tempRoot, "missing-auth-key.p8");
            var confirmedWithPlan = await RunCliAsync(
                repoRoot,
                $"\"{GetCliPath(repoRoot)}\" apple-governance apply --config \"{configPath}\" --reviewed-plan \"{reviewedPlanPath}\" --confirm --key-path \"{missingKeyPath}\" --key-id TEST --issuer-id TEST --output json");
            Assert.Equal(2, confirmedWithPlan.ExitCode);
            using var parsedPlan = JsonDocument.Parse(confirmedWithPlan.StdOut);
            Assert.Contains("private key was not found", parsedPlan.RootElement.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
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
        bool includeGovernance = false,
        string apiKeyPath = ".appstoreconnect/AuthKey_TEST123456.p8")
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
                "AppStoreConnectApiKeyPath": "{{apiKeyPath}}",
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
