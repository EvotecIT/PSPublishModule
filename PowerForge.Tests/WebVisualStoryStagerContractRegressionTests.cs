using System.Text.Json.Nodes;
using Json.Schema;
using PowerForge.Web;

namespace PowerForge.Tests;

public class WebVisualStoryStagerContractRegressionTests
{
    [Fact]
    public void PortablePathTopology_RejectsCaseFoldedFileDirectoryCollisions()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            WebVisualStoryStager.ValidatePortablePathTopologyForTesting(
                "Poster.png",
                "poster.png/frame.svg"));

        Assert.Contains("both a file and directory", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("title")]
    [InlineData("alt")]
    [InlineData("outcome")]
    public void PublishedSchemaRejectsWhitespaceOnlyRequiredText(string propertyName)
    {
        var schemaPath = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory,
            "..", "..", "..", "..",
            "Schemas",
            "powerforge.web.visualstory.schema.json"));
        var schemaDocument = JsonNode.Parse(File.ReadAllText(schemaPath))!;
        var propertySchema = schemaDocument["properties"]![propertyName]!;
        var schema = JsonSchema.FromText(propertySchema.ToJsonString());

        Assert.False(schema.Evaluate(JsonValue.Create(" \t"), new EvaluationOptions()).IsValid);
        Assert.True(schema.Evaluate(JsonValue.Create("story"), new EvaluationOptions()).IsValid);
    }

    [Fact]
    public void ArtifactByteReservation_EnforcesAggregateLimit()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            WebVisualStoryStager.ReserveArtifactBytes(
                currentTotalBytes: 8,
                artifactBytes: 3,
                maximumArtifactBytes: 10,
                maximumTotalArtifactBytes: 10,
                displayPath: "demo.svg"));

        Assert.Contains("aggregate limit", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_RejectsOversizedArtifactsWhenIntegrityMetadataIsOmitted()
    {
        var root = WebVisualStoryStagerTests.CreateBundle();
        try
        {
            var source = Path.Combine(root, "source");
            using (var stream = new FileStream(Path.Combine(source, "demo.svg"), FileMode.Open, FileAccess.Write))
            {
                stream.SetLength(WebVisualStoryStager.DefaultMaximumArtifactBytes + 1);
            }

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Load(Path.Combine(source, "story.json")));

            Assert.Contains("byte limit", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_PersistsConfiguredLimitsForSubsequentLoads()
    {
        var root = WebVisualStoryStagerTests.CreateBundle();
        try
        {
            var result = WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
            {
                ManifestPath = Path.Combine(root, "source", "story.json"),
                OutputPath = Path.Combine(root, "published"),
                MaximumArtifactBytes = 1024,
                MaximumTotalArtifactBytes = 4096
            });
            var persisted = JsonNode.Parse(File.ReadAllText(result.ManifestPath))!;
            Assert.Equal(1024, persisted["resourceLimits"]!["maximumArtifactBytes"]!.GetValue<long>());
            Assert.Equal(4096, persisted["resourceLimits"]!["maximumTotalArtifactBytes"]!.GetValue<long>());

            using (var stream = new FileStream(
                       Path.Combine(root, "published", "demo.txt"),
                       FileMode.Append,
                       FileAccess.Write))
            {
                stream.SetLength(2048);
            }

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Load(result.ManifestPath));

            Assert.Contains("1024-byte limit", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Load_RejectsManifestBeyondTheManifestByteLimit()
    {
        var root = WebVisualStoryStagerTests.CreateBundle();
        try
        {
            var manifest = Path.Combine(root, "source", "story.json");
            using (var stream = new FileStream(manifest, FileMode.Append, FileAccess.Write))
            {
                stream.SetLength(WebVisualStoryStager.MaximumManifestBytes + 1L);
            }

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Load(manifest));

            Assert.Contains("manifest exceeds", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("byte safety limit", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
