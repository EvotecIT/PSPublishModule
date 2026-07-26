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
}
