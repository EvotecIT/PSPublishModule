using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Json.Schema;

namespace PowerForge.Tests;

public sealed class PowerShellCompilationSchemaTests
{
    [Fact]
    public void Module_compilation_schema_rejects_unstructured_dependency_lock()
    {
        var schema = LoadConfigurationSchema();
        var configuration = JsonNode.Parse("""{ "Enabled": true, "Mode": "Hybrid", "DependencyLock": {} }""")!;

        var result = schema.Evaluate(configuration, new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Module_compilation_schema_accepts_complete_dependency_lock_shape()
    {
        var graph = new PowerShellCompilationDependencyGraph
        {
            RootNodeId = "root",
            LockSha256 = new string('a', 64),
            Nodes = new[]
            {
                new PowerShellCompilationDependencyNode
                {
                    Id = "root",
                    Kind = PowerShellCompilationDependencyNodeKind.ScriptModule,
                    Roles = PowerShellCompilationDependencyGraphRole.Semantic,
                    Identity = new PowerShellCompilationDependencyIdentity { Name = "Generic.Module" }
                }
            }
        };
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());
        var configuration = new JsonObject
        {
            ["Enabled"] = true,
            ["Mode"] = "Hybrid",
            ["DependencyLock"] = JsonSerializer.SerializeToNode(graph, options)
        };

        var result = LoadConfigurationSchema().Evaluate(
            configuration,
            new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.True(result.IsValid);
    }

    private static JsonSchema LoadConfigurationSchema()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "Schemas",
            "powerforge.common.schema.json"));
        var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        document["$ref"] = "#/$defs/PowerShellModuleCompilationConfiguration";
        return JsonSchema.FromText(document.ToJsonString());
    }
}
