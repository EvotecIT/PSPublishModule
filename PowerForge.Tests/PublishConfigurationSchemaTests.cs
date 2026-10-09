using System.Text.Json.Nodes;
using Json.Schema;

namespace PowerForge.Tests;

public sealed class PublishConfigurationSchemaTests
{
    [Fact]
    public void Schema_accepts_dependency_skip_and_provider_endpoint_defaults_as_boolean_options()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "Schemas", "powerforge.segments.schema.json"));
        var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        document["$id"] = "https://schemas.example.test/powerforge.segments.schema.json";
        document["$ref"] = "#/$defs/PublishConfiguration";
        var schema = JsonSchema.FromText(document.ToJsonString());
        var options = new EvaluationOptions();
        options.SchemaRegistry.Register(new Uri("https://schemas.example.test/powerforge.common.schema.json"),
            JsonSchema.FromText(File.ReadAllText(Path.Combine(Path.GetDirectoryName(path)!, "powerforge.common.schema.json"))));
        var configuration = JsonNode.Parse("""
            { "SkipDependenciesCheck": true, "Repository": {
              "Uri": "https://packages.example.test/index.json", "UseProviderEndpointDefaults": true } }
            """)!;
        Assert.True(schema.Evaluate(configuration, options).IsValid);
        configuration["Repository"]!["UseProviderEndpointDefaults"] = "true";
        Assert.False(schema.Evaluate(configuration, options).IsValid);
    }
}
