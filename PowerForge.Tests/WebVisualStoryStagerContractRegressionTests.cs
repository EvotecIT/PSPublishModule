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
}
