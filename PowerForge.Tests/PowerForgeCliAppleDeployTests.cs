using System.Diagnostics;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class PowerForgeCliAppleDeployTests
{
    [Fact]
    public void AppleDeploy_diagnostic_redacts_credentials_before_truncation()
    {
        var cliAssembly = System.Reflection.Assembly.LoadFrom(
            GetCliPath(FindRepositoryRoot()));
        var program = cliAssembly.GetType("Program", throwOnError: true)!;
        var formatter = program.GetMethod(
            "ResolveAppleDeployDiagnostic",
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Static)
            ?? throw new MissingMethodException(
                "Program.ResolveAppleDeployDiagnostic was not found.");
        const string credential = "credential-sensitive-value";
        var failure = new ProcessRunResult(
            1,
            credential + new string('x', 1980),
            string.Empty,
            "xcodebuild",
            TimeSpan.FromSeconds(1),
            timedOut: false);

        var diagnostic = (string?)formatter.Invoke(
            null,
            new object[]
            {
                new[] { credential },
                new ProcessRunResult?[] { failure }
            });

        Assert.NotNull(diagnostic);
        Assert.Contains("[REDACTED]", diagnostic, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "sensitive-value",
            diagnostic,
            StringComparison.Ordinal);
        Assert.True(diagnostic.Length <= 2000);
    }

    [Fact]
    public async Task AppleDeploy_plan_uses_nonempty_alias_credentials_when_primary_environment_values_are_empty()
    {
        var repoRoot = FindRepositoryRoot();
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "PowerForgeCliAppleDeploy-" + Guid.NewGuid().ToString("N"));
        var credentialRoot = tempRoot + ".credentials";
        Directory.CreateDirectory(tempRoot);
        Directory.CreateDirectory(credentialRoot);
        try
        {
            var project = Directory.CreateDirectory(Path.Combine(
                tempRoot,
                "Sample.xcodeproj"));
            File.WriteAllText(
                Path.Combine(project.FullName, "project.pbxproj"),
                string.Empty);
            var keyPath = Path.Combine(credentialRoot, "AuthKey_ALIAS.p8");
            File.WriteAllText(keyPath, "private-key");
            var configPath = Path.Combine(tempRoot, "powerforge.release.json");
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
                $"\"{GetCliPath(repoRoot)}\" apple-deploy --config \"{configPath}\" --plan --output json",
                new Dictionary<string, string>
                {
                    ["APP_STORE_CONNECT_PRIVATE_KEY_PATH"] = "",
                    ["APP_STORE_CONNECT_KEY_ID"] = "",
                    ["APP_STORE_CONNECT_ISSUER_ID"] = "",
                    ["ASC_PRIVATE_KEY_PATH"] = keyPath,
                    ["ASC_KEY_ID"] = "ALIASKEY123",
                    ["ASC_ISSUER_ID"] = "alias-issuer-id"
                });

            Assert.Equal(0, result.ExitCode);
            using var document = JsonDocument.Parse(result.StdOut);
            Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
            if (Directory.Exists(credentialRoot))
                Directory.Delete(credentialRoot, recursive: true);
        }
    }

    [Fact]
    public async Task AppleDeploy_plan_redacts_configured_authentication_metadata_from_errors()
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
                string.Empty);
            var missingKeyPath = Path.Combine(
                Path.GetTempPath(),
                "private-signing",
                "AuthKey_DO_NOT_PRINT.p8");
            var keyId = "DO_NOT_PRINT_KEY_ID";
            var issuerId = "do-not-print-issuer-id";
            var configPath = Path.Combine(tempRoot, "powerforge.release.json");
            File.WriteAllText(configPath, $$"""
            {
              "SchemaVersion": 1,
              "AppleApps": {
                "ProjectRoot": ".",
                "AppStoreConnectApiKeyPath": "{{missingKeyPath}}",
                "AppStoreConnectApiKeyId": "{{keyId}}",
                "AppStoreConnectApiIssuerId": "{{issuerId}}",
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
            var output = result.StdOut + result.StdErr;
            Assert.DoesNotContain(missingKeyPath, output, StringComparison.Ordinal);
            Assert.DoesNotContain(keyId, output, StringComparison.Ordinal);
            Assert.DoesNotContain(issuerId, output, StringComparison.Ordinal);
            Assert.Contains("[REDACTED]", output, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task AppleDeploy_plan_redacts_resolved_relative_environment_key_paths()
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
                string.Empty);
            var configPath = Path.Combine(tempRoot, "powerforge.release.json");
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
            const string relativeKeyPath = "../private-signing/AuthKey_ENV.p8";
            var resolvedKeyPath = Path.GetFullPath(Path.Combine(
                tempRoot,
                relativeKeyPath));
            const string keyId = "ENV_DO_NOT_PRINT_KEY_ID";
            const string issuerId = "env-do-not-print-issuer-id";

            var result = await RunCliAsync(
                repoRoot,
                $"\"{GetCliPath(repoRoot)}\" apple-deploy --config \"{configPath}\" --plan --output json",
                new Dictionary<string, string>
                {
                    ["APP_STORE_CONNECT_PRIVATE_KEY_PATH"] = relativeKeyPath,
                    ["APP_STORE_CONNECT_KEY_ID"] = keyId,
                    ["APP_STORE_CONNECT_ISSUER_ID"] = issuerId,
                    ["ASC_PRIVATE_KEY_PATH"] = "",
                    ["ASC_KEY_ID"] = "",
                    ["ASC_ISSUER_ID"] = ""
                });

            Assert.Equal(1, result.ExitCode);
            var output = result.StdOut + result.StdErr;
            Assert.DoesNotContain(relativeKeyPath, output, StringComparison.Ordinal);
            Assert.DoesNotContain(resolvedKeyPath, output, StringComparison.Ordinal);
            Assert.DoesNotContain(keyId, output, StringComparison.Ordinal);
            Assert.DoesNotContain(issuerId, output, StringComparison.Ordinal);
            Assert.Contains("[REDACTED]", output, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task AppleDeploy_plan_preserves_json_errors_for_malformed_credential_paths()
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
                string.Empty);
            var configPath = Path.Combine(tempRoot, "powerforge.release.json");
            File.WriteAllText(configPath, """
            {
              "SchemaVersion": 1,
              "AppleApps": {
                "ProjectRoot": ".",
                "AppStoreConnectApiKeyPath": "\u0000bad-path",
                "AppStoreConnectApiKeyId": "MALFORMEDKEY",
                "AppStoreConnectApiIssuerId": "malformed-issuer",
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
            using var document = JsonDocument.Parse(result.StdOut);
            Assert.False(document.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(1, document.RootElement.GetProperty("exitCode").GetInt32());
            Assert.False(string.IsNullOrWhiteSpace(
                document.RootElement.GetProperty("error").GetString()));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task AppleDeploy_plan_rejects_a_configured_xcode_wrapper()
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
                string.Empty);
            var configPath = Path.Combine(
                tempRoot,
                "powerforge.release.json");
            File.WriteAllText(configPath, """
            {
              "SchemaVersion": 1,
              "AppleApps": {
                "ProjectRoot": ".",
                "XcodeBuildExecutable": "/tmp/custom-xcodebuild",
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
            using var document = JsonDocument.Parse(result.StdOut);
            Assert.Contains(
                "requires the trusted system tool '/usr/bin/xcodebuild'",
                document.RootElement.GetProperty("error").GetString(),
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public async Task AppleDeploy_plan_rejects_a_path_bearing_product_name()
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
                string.Empty);
            var configPath = Path.Combine(tempRoot, "powerforge.release.json");
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
                    "Scheme": "Sample",
                    "ProductName": "../Escaped"
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
            using var document = JsonDocument.Parse(result.StdOut);
            Assert.Contains(
                "ProductName must be a simple app bundle name",
                document.RootElement.GetProperty("error").GetString(),
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

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
            var appRoot = Directory.CreateDirectory(Path.Combine(tempRoot, "App"));
            var project = Directory.CreateDirectory(Path.Combine(appRoot.FullName, "Sample.xcodeproj"));
            File.WriteAllText(Path.Combine(project.FullName, "project.pbxproj"), string.Empty);
            var configPath = Path.Combine(appRoot.FullName, "powerforge.release.json");
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

    [Fact]
    public async Task AppleDeploy_plan_rejects_generated_roots_inside_the_repository()
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
                  "UseBuildMirror": true
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
                $"\"{GetCliPath(repoRoot)}\" apple-deploy --config \"{configPath}\" --plan --output json",
                new Dictionary<string, string>
                {
                    ["TMPDIR"] = tempRoot,
                    ["TMP"] = tempRoot,
                    ["TEMP"] = tempRoot
                });

            Assert.Equal(1, result.ExitCode);
            Assert.Contains(
                nameof(AppleAppBuildRequest.DerivedDataPath),
                result.StdErr + result.StdOut,
                StringComparison.Ordinal);
            Assert.Contains(
                "must be outside BuildRoot",
                result.StdErr + result.StdOut,
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
                Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunCliAsync(
        string workingDirectory,
        string arguments,
        IReadOnlyDictionary<string, string>? environment = null)
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
        if (environment is not null)
        {
            foreach (var pair in environment)
                process.StartInfo.Environment[pair.Key] = pair.Value;
        }
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
