using System.Text.Json;
using ImageMagick;
using PowerForge.Web;

namespace PowerForge.Tests;

public partial class WebVisualStoryStagerTests
{
    internal static string CreateBundle()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-story-" + Guid.NewGuid().ToString("N"));
        var source = Path.Combine(root, "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(
            Path.Combine(source, "demo.svg"),
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><style>@keyframes pulse{from{opacity:.5}to{opacity:1}}rect{animation:pulse 1s infinite}</style><rect width=\"1\" height=\"1\"/></svg>");
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
