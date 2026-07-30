using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public class WebPipelineRunnerVisualStoryTests
{
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
}
