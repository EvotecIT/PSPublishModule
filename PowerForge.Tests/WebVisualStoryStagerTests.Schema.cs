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
        var artifacts = schemaDocument["properties"]!["artifacts"]!;
        Assert.Equal(64, artifacts["maxItems"]!.GetValue<int>());
        Assert.Equal(1, artifacts["minContains"]!.GetValue<int>());
        Assert.Equal(1, artifacts["maxContains"]!.GetValue<int>());
        Assert.Equal(
            "completed",
            artifacts["contains"]!["properties"]!["role"]!["const"]!.GetValue<string>());
        Assert.Equal(
            "png",
            artifacts["items"]!["allOf"]![0]!["then"]!["properties"]!["format"]!["const"]!.GetValue<string>());
        var transcriptFormats = artifacts["items"]!["allOf"]![1]!["then"]!["properties"]!["format"]!["enum"]!;
        Assert.Equal(new[] { "text", "txt" }, transcriptFormats.AsArray().Select(node => node!.GetValue<string>()));
        var animatedFormats = artifacts["items"]!["allOf"]![2]!["then"]!["properties"]!["format"]!["enum"]!;
        Assert.Equal(new[] { "svg", "gif", "apng" }, animatedFormats.AsArray().Select(node => node!.GetValue<string>()));
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
