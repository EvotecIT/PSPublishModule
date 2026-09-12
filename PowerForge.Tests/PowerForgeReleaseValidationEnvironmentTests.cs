using System.Text.Json.Nodes;
using Json.Schema;

namespace PowerForge.Tests;

public sealed class PowerForgeReleaseValidationEnvironmentTests
{
    [Theory]
    [InlineData("", false)]
    [InlineData("VALID_NAME", true)]
    [InlineData("name=value", false)]
    [InlineData("name\0value", false)]
    public void Release_schema_rejects_invalid_validation_environment_names(string name, bool expectedValid)
    {
        var schema = LoadActionSchema();
        var environment = new JsonObject { [name] = "value" };
        var document = new JsonObject
        {
            ["FilePath"] = "validate.ps1",
            ["Environment"] = environment
        };

        var result = schema.Evaluate(document, new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Fact]
    public void Release_schema_rejects_validation_environment_values_containing_NUL()
    {
        var schema = LoadActionSchema();
        var document = JsonNode.Parse("""
            {
              "FilePath": "validate.ps1",
              "Environment": { "VALID_NAME": "bad\u0000value" }
            }
            """)!;

        Assert.False(schema.Evaluate(document, new EvaluationOptions { OutputFormat = OutputFormat.List }).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("name=value")]
    [InlineData("name\0value")]
    public void Execute_rejects_invalid_validation_environment_before_release_work(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.ReleaseValidation.Environment", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var validationPath = Path.Combine(root, "validate.ps1");
            File.WriteAllText(validationPath, "exit 0");
            var configPath = Path.Combine(root, "powerforge.release.json");
            File.WriteAllText(configPath, "{}");
            var action = new PowerForgeReleaseValidationAction { FilePath = validationPath };
            action.Environment[name] = "value";

            var exception = Assert.Throws<InvalidOperationException>(() =>
                new PowerForgeReleaseService(new NullLogger()).Execute(
                    new PowerForgeReleaseSpec
                    {
                        Validation = new PowerForgeReleaseValidationOptions { AfterStaging = [action] }
                    },
                    new PowerForgeReleaseRequest
                    {
                        ConfigPath = configPath,
                        StageRoot = Path.Combine(root, "stage")
                    }));

            Assert.Contains("environment variable names", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Execute_rejects_NUL_validation_environment_value_before_release_work()
    {
        var action = new PowerForgeReleaseValidationAction { FilePath = "validate.ps1" };
        action.Environment["VALID_NAME"] = "bad\0value";

        var exception = Assert.Throws<InvalidOperationException>(() =>
            PowerForgeReleaseValidationService.ValidateActionEnvironment(action.Environment));

        Assert.Contains("cannot contain a NUL value", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static JsonSchema LoadActionSchema()
    {
        var path = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "Schemas",
            "powerforge.release.schema.json"));
        var schemaDocument = JsonNode.Parse(File.ReadAllText(path))!;
        var actionSchema = schemaDocument["properties"]!["Validation"]!["properties"]!["AfterStaging"]!["items"]!;
        return JsonSchema.FromText(actionSchema.ToJsonString());
    }
}
