using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebWebsiteRunnerTests
{
    [Fact]
    public void Run_RejectsUnsupportedEngineMode()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-website-runner-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pipelineConfig = Path.Combine(root, "pipeline.json");
        File.WriteAllText(pipelineConfig, "{}");

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => WebWebsiteRunner.Run(new WebWebsiteRunnerOptions
            {
                WebsiteRoot = root,
                PipelineConfig = pipelineConfig,
                EngineMode = "banana"
            }));

            Assert.Contains("Unsupported engine mode", ex.Message);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Run_ReportsMissingWebsiteRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-website-runner-test-" + Guid.NewGuid().ToString("N"));
        var pipelineConfig = Path.Combine(Path.GetTempPath(), "powerforge-website-runner-pipeline-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(pipelineConfig, "{}");

        try
        {
            var ex = Assert.Throws<DirectoryNotFoundException>(() => WebWebsiteRunner.Run(
                new WebWebsiteRunnerOptions
                {
                    WebsiteRoot = root,
                    PipelineConfig = pipelineConfig,
                    EngineMode = "source"
                }));

            Assert.Contains("Website root not found", ex.Message);
        }
        finally
        {
            if (File.Exists(pipelineConfig))
                File.Delete(pipelineConfig);
        }
    }
}
