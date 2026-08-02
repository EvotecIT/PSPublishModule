using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;
using PowerForge.Web;

namespace PowerForge.Tests;

public partial class WebVisualStoryStagerTests
{
    [Fact]
    public void Stage_RequiresSchemaVersion()
    {
        var root = CreateBundle();
        try
        {
            var manifest = Path.Combine(root, "source", "story.json");
            var json = File.ReadAllText(manifest).Replace("\"schemaVersion\": 1,", string.Empty, StringComparison.Ordinal);
            File.WriteAllText(manifest, json);

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = manifest,
                    OutputPath = Path.Combine(root, "published")
                }));

            Assert.Contains("schemaVersion is required", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_RejectsPropertiesOutsideThePublishedManifestSchema()
    {
        var root = CreateBundle();
        try
        {
            var manifest = Path.Combine(root, "source", "story.json");
            var json = File.ReadAllText(manifest)
                .Replace("\"role\": \"animated\",", "\"role\": \"animated\", \"sha265\": \"typo\",", StringComparison.Ordinal);
            File.WriteAllText(manifest, json);

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = manifest,
                    OutputPath = Path.Combine(root, "published")
                }));

            Assert.Contains("published schema", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_AcceptsAndPreservesThePublishedSchemaDeclaration()
    {
        var root = CreateBundle();
        try
        {
            var manifest = Path.Combine(root, "source", "story.json");
            var bundle = JsonSerializer.Deserialize<WebVisualStoryBundle>(File.ReadAllText(manifest), WebJsonForTests.Options)!;
            bundle.Schema = "https://example.invalid/powerforge.web.visualstory.schema.json";
            File.WriteAllText(manifest, JsonSerializer.Serialize(bundle, WebJsonForTests.Options));

            var result = WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
            {
                ManifestPath = manifest,
                OutputPath = Path.Combine(root, "published")
            });

            Assert.Equal(bundle.Schema, result.Bundle.Schema);
            Assert.Contains("\"$schema\"", File.ReadAllText(result.ManifestPath), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void PublishedSchemaRequiresExactlyOneCompletedPng()
    {
        var schemaPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "Schemas",
            "powerforge.web.visualstory.schema.json"));
        var schemaDocument = JsonNode.Parse(File.ReadAllText(schemaPath))!;
        var schema = JsonSchema.FromText(
            schemaDocument["properties"]!["artifacts"]!.ToJsonString());
        var valid = JsonNode.Parse(
            """
            [
                { "role": "ANIMATED", "format": "SVG", "path": "story.svg" },
                { "role": "COMPLETED", "format": "PNG", "path": "story.png" },
                { "role": "Transcript", "format": "Txt", "path": "story.txt" }
            ]
            """)!;
        var invalidFormat = valid.DeepClone();
        invalidFormat[1]!["format"] = "GIF";
        var duplicateCompleted = valid.DeepClone();
        duplicateCompleted.AsArray().Add(JsonNode.Parse(
            """{ "role": "completed", "format": "png", "path": "second.png" }"""));

        Assert.True(schema.Evaluate(valid, new EvaluationOptions()).IsValid);
        Assert.False(schema.Evaluate(invalidFormat, new EvaluationOptions()).IsValid);
        Assert.False(schema.Evaluate(duplicateCompleted, new EvaluationOptions()).IsValid);
    }

    [Theory]
    [InlineData("demo.png", true)]
    [InlineData("media/demo.png", true)]
    [InlineData("media\\demo.png", true)]
    [InlineData("../demo.png", false)]
    [InlineData("media/../../demo.png", false)]
    [InlineData("/demo.png", false)]
    [InlineData("C:\\demo.png", false)]
    public void PublishedSchemaRequiresBundleRelativeArtifactPaths(string path, bool expected)
    {
        var schemaPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "Schemas",
            "powerforge.web.visualstory.schema.json"));
        var schemaDocument = JsonNode.Parse(File.ReadAllText(schemaPath))!;
        var pathSchema = schemaDocument["properties"]!["artifacts"]!["items"]!["properties"]!["path"]!;
        var schema = JsonSchema.FromText(pathSchema.ToJsonString());
        var result = schema.Evaluate(
            JsonValue.Create(path),
            new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.Equal(expected, result.IsValid);
    }
}
