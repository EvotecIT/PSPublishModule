using System.Text.Json.Nodes;
using Json.Schema;

namespace PowerForge.Tests;

public sealed class PowerForgeReleaseSchemaTests
{
    [Theory]
    [InlineData(1, false, false, true)]
    [InlineData(1, true, false, true)]
    [InlineData(2, false, true, false)]
    [InlineData(2, true, false, false)]
    [InlineData(2, true, true, true)]
    public void Tool_lock_schema_preserves_v1_and_requires_executable_digest_and_commit_for_v2(
        int schemaVersion,
        bool includeExecutableDigest,
        bool includeCommit,
        bool expectedValid)
    {
        var schema = JsonSchema.FromText(File.ReadAllText(GetSchemaPath("powerforge.tool.schema.json")));
        var executableDigest = includeExecutableDigest
            ? ", \"executableSha256\": \"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb\""
            : string.Empty;
        var commit = includeCommit
            ? "\"commit\": \"0123456789abcdef0123456789abcdef01234567\","
            : string.Empty;
        var document = JsonNode.Parse($$"""
            {
              "schemaVersion": {{schemaVersion}},
              {{commit}}
              "version": "3.0.110",
              "assets": {
                "osx-arm64": {
                  "sha256": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"{{executableDigest}}
                }
              }
            }
            """)!;

        var result = schema.Evaluate(document, new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Theory]
    [InlineData("""{ "Module": { "ConfigPath": "powerforge.json", "ScriptPath": null } }""", true)]
    [InlineData("""{ "Module": { "ConfigPath": null, "ScriptPath": "Build/Build-Module.ps1" } }""", true)]
    [InlineData("""{ "Module": { "ConfigPath": "powerforge.json", "ScriptPath": "Build/Build-Module.ps1" } }""", false)]
    public void Module_source_exclusion_ignores_null_unused_source(string json, bool expectedValid)
    {
        var schemaPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "Schemas",
            "powerforge.release.schema.json"));
        var schemaDocument = JsonNode.Parse(File.ReadAllText(schemaPath))!;
        var moduleSchema = schemaDocument["properties"]!["Module"]!;
        var schema = JsonSchema.FromText(moduleSchema.ToJsonString());
        var node = JsonNode.Parse(json)!["Module"]!;

        var result = schema.Evaluate(node, new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 1, "appId": "123", "accessibility": [ { "deviceFamily": "IPHONE", "supportsVoiceover": true } ] }""", true)]
    [InlineData("""{ "schemaVersion": 1, "appId": "123", "accessibility": [ { "deviceFamily": "CARPLAY" } ] }""", false)]
    [InlineData("""{ "schemaVersion": 1, "appId": "123", "inventedLegalAnswer": true }""", false)]
    public void Apple_governance_schema_rejects_unknown_or_unsupported_contracts(string json, bool expectedValid)
    {
        var schema = JsonSchema.FromText(File.ReadAllText(GetSchemaPath("appstore-connect-governance.schema.json")));
        var result = schema.Evaluate(JsonNode.Parse(json)!, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void Release_schema_accepts_governance_drift_gate()
    {
        var schemaDocument = JsonNode.Parse(File.ReadAllText(GetSchemaPath("powerforge.release.schema.json")))!;
        var appleSchemaNode = schemaDocument["properties"]!["AppleApps"]!.DeepClone();
        appleSchemaNode["properties"]!.AsObject().Remove("Apps");
        var appleSchema = JsonSchema.FromText(appleSchemaNode.ToJsonString());
        var node = JsonNode.Parse("""{ "GovernanceConfigPath": "build/governance.json", "GovernanceConfigPaths": [], "CheckGovernance": true }""")!;
        Assert.True(appleSchema.Evaluate(node, new EvaluationOptions { OutputFormat = OutputFormat.List }).IsValid);
    }

    [Fact]
    public void Release_schema_and_runtime_accept_local_Apple_deployment_profiles()
    {
        var schemaDocument = JsonNode.Parse(File.ReadAllText(GetSchemaPath("powerforge.release.schema.json")))!;
        var localSchema = JsonSchema.FromText(
            schemaDocument["properties"]!["AppleApps"]!["properties"]!["LocalDeployment"]!.ToJsonString());
        var json = """
            {
              "DefaultPlatform": "iOS",
              "DefaultDevice": "EvoPhone",
              "Configuration": "Debug",
              "InstallRoot": "/Applications",
              "DefaultProfile": "Plus",
              "Profiles": [
                {
                  "Name": "Free",
                  "Environment": { "CASARAY_ENABLE_SANDBOX_PURCHASES": "1" }
                },
                {
                  "Name": "Plus",
                  "Environment": { "CASARAY_ENABLE_SANDBOX_PURCHASES": "0" }
                }
              ]
            }
            """;

        Assert.True(localSchema.Evaluate(JsonNode.Parse(json)!, new EvaluationOptions { OutputFormat = OutputFormat.List }).IsValid);

        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, $$"""{ "AppleApps": { "LocalDeployment": {{json}} } }""");
            var local = PowerForgeReleaseService.LoadConfiguration(path).AppleApps!.LocalDeployment;
            Assert.Equal(ApplePlatform.iOS, local.DefaultPlatform);
            Assert.Equal("EvoPhone", local.DefaultDevice);
            Assert.Equal("Plus", local.DefaultProfile);
            Assert.Equal("1", Assert.Single(local.Profiles, profile => profile.Name == "Free").Environment["CASARAY_ENABLE_SANDBOX_PURCHASES"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("1.X.0", true)]
    [InlineData("1.6.X", true)]
    [InlineData("1.X", true)]
    [InlineData("X.0.0", true)]
    [InlineData("1.6.0", false)]
    [InlineData("1.X.X", false)]
    [InlineData("X.X.X", false)]
    [InlineData("1.2.3.X", false)]
    public void Release_schema_accepts_only_supported_Apple_marketing_version_patterns(
        string pattern,
        bool expectedValid)
    {
        var schemaDocument = JsonNode.Parse(File.ReadAllText(GetSchemaPath("powerforge.release.schema.json")))!;
        var automationSchema = JsonSchema.FromText(
            schemaDocument["properties"]!["AppleApps"]!["properties"]!["Automation"]!.ToJsonString());
        var node = JsonNode.Parse($$"""{ "MarketingVersionPattern": "{{pattern}}" }""")!;

        var result = automationSchema.Evaluate(node, new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void Release_runtime_loads_Apple_marketing_version_pattern()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, """
                {
                  "AppleApps": {
                    "Automation": {
                      "VersionSourcePath": "project.yml",
                      "MarketingVersionPattern": "1.X"
                    }
                  }
                }
                """);

            var automation = Assert.IsType<PowerForgeAppleReleaseAutomationOptions>(
                PowerForgeReleaseService.LoadConfiguration(path).AppleApps?.Automation);
            Assert.Equal("1.X", automation.MarketingVersionPattern);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Release_schema_accepts_exact_verified_github_recovery_binding()
    {
        var schemaDocument = JsonNode.Parse(File.ReadAllText(GetSchemaPath("powerforge.release.schema.json")))!;
        var gitHubSchema = JsonSchema.FromText(schemaDocument["properties"]!["GitHub"]!.ToJsonString());
        var node = JsonNode.Parse("""
            {
              "Publish": true,
              "ReuseExistingRelease": true,
              "RequireExpectedExistingRelease": true,
              "ExpectedExistingReleaseId": 42,
              "RequirePublishedStableRelease": true,
              "ReplaceExistingAssets": true,
              "RequirePublishedNuGetAssets": true,
              "RequirePublishedModuleAssets": true,
              "PublishedModuleSource": "https://www.powershellgallery.com/api/v2"
            }
            """)!;

        Assert.True(gitHubSchema.Evaluate(node, new EvaluationOptions { OutputFormat = OutputFormat.List }).IsValid);
    }

    [Fact]
    public void Release_runtime_loads_exact_verified_github_recovery_binding()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, """
                {
                  "GitHub": {
                    "Publish": true,
                    "ReuseExistingRelease": true,
                    "RequireExpectedExistingRelease": true,
                    "ExpectedExistingReleaseId": 42,
                    "RequirePublishedStableRelease": true,
                    "ReplaceExistingAssets": true,
                    "RequirePublishedNuGetAssets": true,
                    "RequirePublishedModuleAssets": true,
                    "PublishedModuleSource": "https://www.powershellgallery.com/api/v2"
                  }
                }
                """);

            var gitHub = Assert.IsType<PowerForgeReleaseGitHubOptions>(
                PowerForgeReleaseService.LoadConfiguration(path).GitHub);
            Assert.True(gitHub.ReuseExistingRelease);
            Assert.True(gitHub.RequireExpectedExistingRelease);
            Assert.Equal(42, gitHub.ExpectedExistingReleaseId);
            Assert.True(gitHub.RequirePublishedStableRelease);
            Assert.True(gitHub.ReplaceExistingAssets);
            Assert.True(gitHub.RequirePublishedNuGetAssets);
            Assert.True(gitHub.RequirePublishedModuleAssets);
            Assert.Equal("https://www.powershellgallery.com/api/v2", gitHub.PublishedModuleSource);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Release_schema_and_runtime_accept_opt_in_VirusTotal_Monitor_configuration()
    {
        const string json = """
            {
              "Enabled": true,
              "ProjectName": "ExampleApp",
              "ApiKeyEnvName": "VIRUSTOTAL_MONITOR_API_KEY",
              "ArtifactKinds": [ "PowerShellModule", "NuGetPackage", "MsiPackage" ],
              "DestinationPathTemplate": "/{Project}/{Version}/{Kind}/{RelativePath}",
              "VerifySha256": true
            }
            """;
        var schemaDocument = JsonNode.Parse(File.ReadAllText(GetSchemaPath("powerforge.release.schema.json")))!;
        var virusTotalSchema = JsonSchema.FromText(schemaDocument["properties"]!["VirusTotal"]!.ToJsonString());

        Assert.True(virusTotalSchema.Evaluate(
            JsonNode.Parse(json)!,
            new EvaluationOptions { OutputFormat = OutputFormat.List }).IsValid);

        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, $$"""{ "VirusTotal": {{json}} }""");
            var options = Assert.IsType<PowerForgeVirusTotalOptions>(
                PowerForgeReleaseService.LoadConfiguration(path).VirusTotal);

            Assert.True(options.Enabled);
            Assert.Equal("VIRUSTOTAL_MONITOR_API_KEY", options.ApiKeyEnvName);
            Assert.Equal(
                new[]
                {
                    VirusTotalArtifactKind.PowerShellModule,
                    VirusTotalArtifactKind.NuGetPackage,
                    VirusTotalArtifactKind.MsiPackage
                },
                options.ArtifactKinds);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Release_schema_accepts_null_unused_VirusTotal_credentials()
    {
        const string json = """
            {
              "Enabled": true,
              "ApiKey": null,
              "ApiKeyFilePath": null,
              "ApiKeyEnvName": "VIRUSTOTAL_MONITOR_API_KEY",
              "ArtifactKinds": [ "MsiPackage" ]
            }
            """;
        var schemaDocument = JsonNode.Parse(File.ReadAllText(GetSchemaPath("powerforge.release.schema.json")))!;
        var virusTotalSchema = JsonSchema.FromText(schemaDocument["properties"]!["VirusTotal"]!.ToJsonString());

        Assert.True(virusTotalSchema.Evaluate(
            JsonNode.Parse(json)!,
            new EvaluationOptions { OutputFormat = OutputFormat.List }).IsValid);
    }

    [Theory]
    [InlineData("""{ "Enabled": true, "ArtifactKinds": [ "MsiPackage" ] }""")]
    [InlineData("""{ "Enabled": true, "ApiKey": "one", "ApiKeyEnvName": "TWO", "ArtifactKinds": [ "MsiPackage" ] }""")]
    [InlineData("""{ "Enabled": true, "ApiKeyEnvName": "KEY", "ArtifactKinds": [] }""")]
    [InlineData("""{ "Enabled": true, "ApiKeyEnvName": "KEY", "ArtifactKinds": [ "SourceArchive" ] }""")]
    public void Release_schema_rejects_incomplete_or_unsupported_VirusTotal_configuration(string json)
    {
        var schemaDocument = JsonNode.Parse(File.ReadAllText(GetSchemaPath("powerforge.release.schema.json")))!;
        var virusTotalSchema = JsonSchema.FromText(schemaDocument["properties"]!["VirusTotal"]!.ToJsonString());

        Assert.False(virusTotalSchema.Evaluate(
            JsonNode.Parse(json)!,
            new EvaluationOptions { OutputFormat = OutputFormat.List }).IsValid);
    }

    [Fact]
    public void Release_runtime_loads_pre_github_registry_recovery_binding()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, """
                {
                  "GitHub": {
                    "Publish": true,
                    "Commitish": "0123456789abcdef0123456789abcdef01234567",
                    "RequirePublishedNuGetAssets": true,
                    "RequirePublishedModuleAssets": true,
                    "PublishedModuleSource": "https://www.powershellgallery.com/api/v2",
                    "RecoverPublishedRegistryAssetsBeforeGitHubRelease": true,
                    "PublishedModuleAlreadyExists": true
                  }
                }
                """);

            var gitHub = Assert.IsType<PowerForgeReleaseGitHubOptions>(
                PowerForgeReleaseService.LoadConfiguration(path).GitHub);
            Assert.True(gitHub.RecoverPublishedRegistryAssetsBeforeGitHubRelease);
            Assert.True(gitHub.PublishedModuleAlreadyExists);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string GetSchemaPath(string fileName) => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory,
        "..", "..", "..", "..",
        "Schemas",
        fileName));
}
