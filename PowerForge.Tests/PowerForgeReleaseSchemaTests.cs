using System.Text.Json.Nodes;
using Json.Schema;

namespace PowerForge.Tests;

public sealed class PowerForgeReleaseSchemaTests
{
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
