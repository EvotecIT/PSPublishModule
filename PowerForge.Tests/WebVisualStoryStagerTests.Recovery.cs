using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Tests;

public partial class WebVisualStoryStagerTests
{
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
    public void Stage_OverwriteRecoversFromInvalidPriorManifest()
    {
        var root = CreateBundle();
        try
        {
            var sourceManifest = Path.Combine(root, "source", "story.json");
            var output = Path.Combine(root, "published");
            var first = WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
            {
                ManifestPath = sourceManifest,
                OutputPath = output
            });
            File.WriteAllText(first.ManifestPath, "{ truncated");
            File.WriteAllText(Path.Combine(output, "stale.bin"), "stale");

            var replacement = WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
            {
                ManifestPath = sourceManifest,
                OutputPath = output,
                Overwrite = true
            });

            Assert.Equal(3, WebVisualStoryStager.Load(replacement.ManifestPath).Artifacts.Length);
            Assert.False(File.Exists(Path.Combine(output, "stale.bin")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_OverwriteRemovesUndeclaredFilesFromAValidPriorBundle()
    {
        var root = CreateBundle();
        try
        {
            var sourceManifest = Path.Combine(root, "source", "story.json");
            var output = Path.Combine(root, "published");
            _ = WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
            {
                ManifestPath = sourceManifest,
                OutputPath = output
            });
            File.WriteAllText(Path.Combine(output, "stale.html"), "<script>alert('stale')</script>");

            _ = WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
            {
                ManifestPath = sourceManifest,
                OutputPath = output,
                Overwrite = true
            });

            Assert.False(File.Exists(Path.Combine(output, "stale.html")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Stage_OverwriteRecoversFromMalformedPriorArtifactPath()
    {
        var root = CreateBundle();
        try
        {
            var sourceManifest = Path.Combine(root, "source", "story.json");
            var output = Path.Combine(root, "published");
            var first = WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
            {
                ManifestPath = sourceManifest,
                OutputPath = output
            });
            var prior = JsonSerializer.Deserialize<WebVisualStoryBundle>(
                File.ReadAllText(first.ManifestPath),
                WebJsonForTests.Options)!;
            prior.Artifacts[0].Path = "bad\0.png";
            File.WriteAllText(first.ManifestPath, JsonSerializer.Serialize(prior, WebJsonForTests.Options));
            File.WriteAllText(Path.Combine(output, "stale.bin"), "stale");

            var replacement = WebVisualStoryStager.Stage(new WebVisualStoryStageOptions
            {
                ManifestPath = sourceManifest,
                OutputPath = output,
                Overwrite = true
            });

            Assert.Equal(3, WebVisualStoryStager.Load(replacement.ManifestPath).Artifacts.Length);
            Assert.False(File.Exists(Path.Combine(output, "stale.bin")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
