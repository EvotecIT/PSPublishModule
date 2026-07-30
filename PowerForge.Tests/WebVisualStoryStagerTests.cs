using System.Text.Json;
using System.Text.Json.Nodes;
using ImageMagick;
using PowerForge.Web;

namespace PowerForge.Tests;

public class WebVisualStoryStagerTests
{
    [Fact]
    public void Stage_ProducesSelfContainedManifest_WithCompletedOutcome()
    {
        var root = CreateBundle();
        try
        {
            var output = Path.Combine(root, "published");
            var result = WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
            {
                ManifestPath = Path.Combine(root, "source", "story.json"),
                OutputPath = output
            });

            Assert.Equal(3, result.ArtifactCount);
            Assert.True(File.Exists(Path.Combine(output, "visual-story.json")));
            Assert.True(File.Exists(Path.Combine(output, "demo.svg")));
            Assert.True(File.Exists(Path.Combine(output, "demo.png")));
            var staged = WebVisualStoryStager.Load(result.ManifestPath);
            var completed = Assert.Single(staged.Artifacts, a => a.Role == "completed");
            Assert.Equal("png", completed.Format);
            Assert.NotNull(completed.Sha256);
            Assert.True(completed.Bytes > 0);
            Assert.Equal("The chart is visible.", staged.Outcome);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_AllowsNormalizingBundleInPlace_WithoutCopyingArtifactsOntoThemselves()
    {
        var root = CreateBundle();
        try
        {
            var source = Path.Combine(root, "source");
            var result = WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
            {
                ManifestPath = Path.Combine(source, "story.json"),
                OutputPath = source,
                Overwrite = false
            });

            Assert.Equal(Path.Combine(source, "visual-story.json"), result.ManifestPath);
            Assert.True(File.Exists(Path.Combine(source, "demo.svg")));
            Assert.True(File.Exists(Path.Combine(source, "demo.png")));
            Assert.Equal(3, WebVisualStoryStager.Load(result.ManifestPath).Artifacts.Length);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_RejectsDeclaredIntegrityThatDoesNotMatchSource()
    {
        var root = CreateBundle();
        try
        {
            var manifest = Path.Combine(root, "source", "story.json");
            var bundle = JsonSerializer.Deserialize<WebVisualStoryBundle>(File.ReadAllText(manifest), WebJsonForTests.Options)!;
            bundle.Artifacts[0].Bytes = 0;
            bundle.Artifacts[0].Sha256 = new string('0', 64);
            File.WriteAllText(manifest, JsonSerializer.Serialize(bundle, WebJsonForTests.Options));

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = manifest,
                    OutputPath = Path.Combine(root, "published")
                }));

            Assert.Contains("size does not match", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_PreservesNestedPathsAndCanonicalizesRoles()
    {
        var root = CreateBundle();
        try
        {
            var source = Path.Combine(root, "source");
            var nested = Path.Combine(source, "media", "animated");
            Directory.CreateDirectory(nested);
            File.Move(Path.Combine(source, "demo.svg"), Path.Combine(nested, "demo.svg"));
            var manifest = Path.Combine(source, "story.json");
            var bundle = JsonSerializer.Deserialize<WebVisualStoryBundle>(File.ReadAllText(manifest), WebJsonForTests.Options)!;
            bundle.Artifacts[0].Role = "ANIMATED";
            bundle.Artifacts[0].Path = "./media/animated/demo.svg";
            File.WriteAllText(manifest, JsonSerializer.Serialize(bundle, WebJsonForTests.Options));

            var result = WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
            {
                ManifestPath = manifest,
                OutputPath = Path.Combine(root, "published")
            });

            var animated = Assert.Single(result.Bundle.Artifacts, artifact => artifact.Role == "animated");
            Assert.Equal("media/animated/demo.svg", animated.Path);
            Assert.True(File.Exists(Path.Combine(root, "published", "media", "animated", "demo.svg")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_HonorsOverwriteForManifestAndRemovesObsoleteDeclaredArtifacts()
    {
        var root = CreateBundle();
        try
        {
            var sourceManifest = Path.Combine(root, "source", "story.json");
            var output = Path.Combine(root, "published");
            WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
            {
                ManifestPath = sourceManifest,
                OutputPath = output
            });

            Assert.Throws<IOException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = sourceManifest,
                    OutputPath = output,
                    Overwrite = false
                }));

            var bundle = JsonSerializer.Deserialize<WebVisualStoryBundle>(File.ReadAllText(sourceManifest), WebJsonForTests.Options)!;
            bundle.Artifacts = bundle.Artifacts.Where(artifact => artifact.Role != "transcript").ToArray();
            File.WriteAllText(sourceManifest, JsonSerializer.Serialize(bundle, WebJsonForTests.Options));
            WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
            {
                ManifestPath = sourceManifest,
                OutputPath = output
            });

            Assert.False(File.Exists(Path.Combine(output, "demo.txt")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_AppliesCaseOnlyArtifactRenamesExactly()
    {
        var root = CreateBundle();
        try
        {
            var source = Path.Combine(root, "source");
            var sourceDirectory = Path.Combine(source, "media");
            Directory.CreateDirectory(sourceDirectory);
            File.Move(Path.Combine(source, "demo.svg"), Path.Combine(sourceDirectory, "demo.svg"));
            var sourceManifest = Path.Combine(source, "story.json");
            var bundle = JsonSerializer.Deserialize<WebVisualStoryBundle>(File.ReadAllText(sourceManifest), WebJsonForTests.Options)!;
            bundle.Artifacts[0].Path = "media/demo.svg";
            File.WriteAllText(sourceManifest, JsonSerializer.Serialize(bundle, WebJsonForTests.Options));

            var output = Path.Combine(root, "published");
            WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
            {
                ManifestPath = sourceManifest,
                OutputPath = output
            });
            File.WriteAllText(Path.Combine(output, "media", "retained.txt"), "retained");

            var sourceTemporary = Path.Combine(source, "source-case-" + Guid.NewGuid().ToString("N"));
            Directory.Move(sourceDirectory, sourceTemporary);
            var renamedSourceDirectory = Path.Combine(source, "Media");
            Directory.Move(sourceTemporary, renamedSourceDirectory);
            var fileTemporary = Path.Combine(renamedSourceDirectory, "file-case-" + Guid.NewGuid().ToString("N"));
            File.Move(Path.Combine(renamedSourceDirectory, "demo.svg"), fileTemporary);
            File.Move(fileTemporary, Path.Combine(renamedSourceDirectory, "Demo.svg"));
            bundle.Artifacts[0].Path = "Media/Demo.svg";
            File.WriteAllText(sourceManifest, JsonSerializer.Serialize(bundle, WebJsonForTests.Options));

            WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
            {
                ManifestPath = sourceManifest,
                OutputPath = output
            });

            Assert.Contains(
                Directory.EnumerateDirectories(output),
                path => string.Equals(Path.GetFileName(path), "Media", StringComparison.Ordinal));
            Assert.Contains(
                Directory.EnumerateFiles(Path.Combine(output, "Media")),
                path => string.Equals(Path.GetFileName(path), "Demo.svg", StringComparison.Ordinal));
            Assert.True(File.Exists(Path.Combine(output, "Media", "retained.txt")));
            var staged = WebVisualStoryStager.Load(Path.Combine(output, "visual-story.json"));
            Assert.Contains(staged.Artifacts, artifact => string.Equals(artifact.Path, "Media/Demo.svg", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

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
    }

    [Fact]
    public void Stage_RequiresTextTranscriptArtifacts()
    {
        var root = CreateBundle();
        try
        {
            var manifest = Path.Combine(root, "source", "story.json");
            var bundle = JsonSerializer.Deserialize<WebVisualStoryBundle>(File.ReadAllText(manifest), WebJsonForTests.Options)!;
            var transcript = Assert.Single(bundle.Artifacts, artifact => artifact.Role == "transcript");
            transcript.Format = "png";
            transcript.Path = "demo.png";
            File.WriteAllText(manifest, JsonSerializer.Serialize(bundle, WebJsonForTests.Options));

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = manifest,
                    OutputPath = Path.Combine(root, "published")
                }));

            Assert.Contains("transcript artifacts must use the text format", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_RejectsArtifactPathThatConflictsWithStagedManifest()
    {
        var root = CreateBundle();
        try
        {
            var source = Path.Combine(root, "source");
            var reservedDirectory = Path.Combine(source, "visual-story.json");
            Directory.CreateDirectory(reservedDirectory);
            File.Move(Path.Combine(source, "demo.svg"), Path.Combine(reservedDirectory, "demo.svg"));
            var manifest = Path.Combine(source, "story.json");
            var bundle = JsonSerializer.Deserialize<WebVisualStoryBundle>(File.ReadAllText(manifest), WebJsonForTests.Options)!;
            bundle.Artifacts[0].Path = "visual-story.json/demo.svg";
            File.WriteAllText(manifest, JsonSerializer.Serialize(bundle, WebJsonForTests.Options));

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = manifest,
                    OutputPath = Path.Combine(root, "published")
                }));

            Assert.Contains("reserved staged manifest", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void PromoteStagedDirectory_RestoresExistingBundleWhenPromotionFails()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-story-promotion-" + Guid.NewGuid().ToString("N"));
        var output = Path.Combine(root, "published");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "visual-story.json"), "previous");
        try
        {
            Assert.Throws<DirectoryNotFoundException>(() =>
                WebVisualStoryStager.PromoteStagedDirectory(
                    Path.Combine(root, "missing-stage"),
                    output));

            Assert.Equal("previous", File.ReadAllText(Path.Combine(output, "visual-story.json")));
            Assert.Empty(Directory.GetDirectories(root, "*.pf-story-backup-*"));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_RejectsArtifactOutsideBundle()
    {
        var root = CreateBundle();
        try
        {
            var manifest = Path.Combine(root, "source", "story.json");
            var bundle = JsonSerializer.Deserialize<WebVisualStoryBundle>(File.ReadAllText(manifest), WebJsonForTests.Options)!;
            bundle.Artifacts[0].Path = "../outside.svg";
            File.WriteAllText(manifest, JsonSerializer.Serialize(bundle, WebJsonForTests.Options));

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = manifest,
                    OutputPath = Path.Combine(root, "published")
                }));
            Assert.Contains("escapes", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_RequiresExactlyOneCompletedPng()
    {
        var root = CreateBundle();
        try
        {
            var manifest = Path.Combine(root, "source", "story.json");
            var bundle = JsonSerializer.Deserialize<WebVisualStoryBundle>(File.ReadAllText(manifest), WebJsonForTests.Options)!;
            bundle.Artifacts = bundle.Artifacts.Where(a => a.Role != "completed").ToArray();
            File.WriteAllText(manifest, JsonSerializer.Serialize(bundle, WebJsonForTests.Options));

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = manifest,
                    OutputPath = Path.Combine(root, "published")
                }));
            Assert.Contains("completed PNG", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_RejectsArtifactThroughSymbolicLink()
    {
        var root = CreateBundle();
        try
        {
            var outside = Path.Combine(root, "outside.svg");
            File.WriteAllText(outside, "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");
            var link = Path.Combine(root, "source", "linked.svg");
            File.CreateSymbolicLink(link, outside);

            var manifest = Path.Combine(root, "source", "story.json");
            var bundle = JsonSerializer.Deserialize<WebVisualStoryBundle>(File.ReadAllText(manifest), WebJsonForTests.Options)!;
            bundle.Artifacts[0].Path = "linked.svg";
            File.WriteAllText(manifest, JsonSerializer.Serialize(bundle, WebJsonForTests.Options));

            var error = Assert.Throws<InvalidOperationException>(() =>
                WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
                {
                    ManifestPath = manifest,
                    OutputPath = Path.Combine(root, "published")
                }));
            Assert.Contains("symbolic link", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Load_RejectsCompletedArtifactWhoseExtensionDoesNotMatchPng()
    {
        var root = CreateBundle();
        try
        {
            var manifest = Path.Combine(root, "source", "story.json");
            var bundle = JsonSerializer.Deserialize<WebVisualStoryBundle>(File.ReadAllText(manifest), WebJsonForTests.Options)!;
            var completed = Assert.Single(bundle.Artifacts, artifact => artifact.Role == "completed");
            completed.Path = "demo.svg";
            File.WriteAllText(manifest, JsonSerializer.Serialize(bundle, WebJsonForTests.Options));

            var error = Assert.Throws<InvalidOperationException>(() => WebVisualStoryStager.Load(manifest));

            Assert.Contains("does not match", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Load_RejectsCompletedArtifactWithCorruptPngBytes()
    {
        var root = CreateBundle();
        try
        {
            var manifest = Path.Combine(root, "source", "story.json");
            File.WriteAllText(Path.Combine(root, "source", "demo.png"), "not a PNG");

            var error = Assert.Throws<InvalidOperationException>(() => WebVisualStoryStager.Load(manifest));

            Assert.Contains("decodable PNG", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    internal static string CreateBundle()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-story-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "demo.svg"), "<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>");
        using (var image = new MagickImage(MagickColors.Transparent, 2, 2))
        {
            image.Write(Path.Combine(source, "demo.png"), MagickFormat.Png);
        }
        File.WriteAllText(Path.Combine(source, "demo.txt"), "Run demo\nThe chart is visible.");
        var bundle = new WebVisualStoryBundle
        {
            SchemaVersion = 1,
            Id = "chart-five-lines",
            Title = "Create a chart in five lines",
            Alt = "Source code followed by the generated chart.",
            Outcome = "The chart is visible.",
            Artifacts =
            [
                new WebVisualStoryArtifact { Role = "animated", Format = "svg", Path = "demo.svg" },
                new WebVisualStoryArtifact { Role = "completed", Format = "png", Path = "demo.png" },
                new WebVisualStoryArtifact { Role = "transcript", Format = "text", Path = "demo.txt" }
            ]
        };
        File.WriteAllText(
            Path.Combine(source, "story.json"),
            JsonSerializer.Serialize(bundle, WebJsonForTests.Options));
        return root;
    }

    private static class WebJsonForTests
    {
        internal static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };
    }
}
