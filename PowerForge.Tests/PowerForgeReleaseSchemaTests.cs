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
              "RequirePublishedNuGetAssets": true
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
                    "RequirePublishedNuGetAssets": true
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
