using PowerForge.Web.Cli;
using System.Reflection;
using System.Text.Json;

namespace PowerForge.Tests;

public class WebPipelineRunnerVisualStoryTests
{
    [Theory]
    [InlineData("visual-story")]
    [InlineData("visualstory")]
    public void VisualStoryStepsAreNotCached(string task)
    {
        using var document = JsonDocument.Parse(
            $$"""{"task":"{{task}}","outputPath":"static/stories/demo"}""");
        var method = typeof(WebPipelineRunner).GetMethod(
            "IsCacheableStep",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        Assert.False((bool)method.Invoke(null, [task, document.RootElement])!);
    }

    [Fact]
    public void RunPipeline_VisualStory_StagesResolvedBundleWithoutExecution()
    {
        var root = WebVisualStoryStagerTests.CreateBundle();
        try
        {
            File.WriteAllText(Path.Combine(root, "pipeline.json"),
                """
                {
                  "steps": [
                    {
                      "task": "visual-story",
                      "manifest": "source/story.json",
                      "out": "static/stories/chart-five-lines"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(Path.Combine(root, "pipeline.json"), logger: null);

            Assert.True(result.Success);
            Assert.Contains("staged 3 artifacts", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(root, "static", "stories", "chart-five-lines", "visual-story.json")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RunPipeline_VisualStory_AppliesAggregateArtifactBudget()
    {
        var root = WebVisualStoryStagerTests.CreateBundle();
        try
        {
            File.WriteAllText(Path.Combine(root, "pipeline.json"),
                """
                {
                  "steps": [
                    {
                      "task": "visual-story",
                      "manifest": "source/story.json",
                      "out": "static/stories/chart-five-lines",
                      "maximumTotalArtifactBytes": 1
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(Path.Combine(root, "pipeline.json"), logger: null);

            Assert.False(result.Success);
            Assert.Contains("aggregate limit", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RunPipeline_VisualStory_RejectsOutputOutsidePipelineRoot()
    {
        var root = WebVisualStoryStagerTests.CreateBundle();
        try
        {
            File.WriteAllText(Path.Combine(root, "pipeline.json"),
                """
                {
                  "steps": [
                    {
                      "task": "visual-story",
                      "command": "this-command-must-not-run",
                      "manifest": "source/story.json",
                      "out": "../escaped-story"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(Path.Combine(root, "pipeline.json"), logger: null);

            Assert.False(result.Success);
            Assert.Contains("pipeline root", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RunPipeline_VisualStory_RejectsOutputThroughSymbolicLink()
    {
        var root = WebVisualStoryStagerTests.CreateBundle();
        try
        {
            var outside = Path.Combine(Path.GetTempPath(), "pf-story-outside-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outside);
            Directory.CreateSymbolicLink(Path.Combine(root, "linked-output"), outside);
            File.WriteAllText(Path.Combine(root, "pipeline.json"),
                """
                {
                  "steps": [
                    {
                      "task": "visual-story",
                      "manifest": "source/story.json",
                      "out": "linked-output/story"
                    }
                  ]
                }
                """);

            try
            {
                var result = WebPipelineRunner.RunPipeline(Path.Combine(root, "pipeline.json"), logger: null);

                Assert.False(result.Success);
                Assert.Contains("symbolic link", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                Directory.Delete(outside, true);
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RunPipeline_VisualStory_RechecksProducerCreatedManifestLinks()
    {
        var bundleRoot = WebVisualStoryStagerTests.CreateBundle();
        var root = Path.Combine(Path.GetTempPath(), "pf-story-pipeline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var producer = Path.Combine(root, "producer.ps1");
            File.WriteAllText(
                producer,
                "param([string] $Link, [string] $Target)\n" +
                "[System.IO.Directory]::CreateSymbolicLink($Link, $Target) | Out-Null\n");
            var pipeline = new
            {
                steps = new[]
                {
                    new
                    {
                        task = "visual-story",
                        command = "pwsh",
                        argsList = new[]
                        {
                            "-NoLogo",
                            "-NoProfile",
                            "-File",
                            producer,
                            Path.Combine(root, "source"),
                            Path.Combine(bundleRoot, "source")
                        },
                        manifest = "source/story.json",
                        output = "published"
                    }
                }
            };
            File.WriteAllText(
                Path.Combine(root, "pipeline.json"),
                JsonSerializer.Serialize(pipeline));

            var result = WebPipelineRunner.RunPipeline(Path.Combine(root, "pipeline.json"), logger: null);

            Assert.False(result.Success);
            Assert.Contains("symbolic link", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(root, "published", "visual-story.json")));
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(bundleRoot, true);
        }
    }
}
