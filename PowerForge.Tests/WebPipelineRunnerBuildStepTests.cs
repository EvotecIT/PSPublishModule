using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public class WebPipelineRunnerBuildStepTests
{
    [Fact]
    public void RunPipeline_BuildStep_CanDisableGeneratedSocialCards()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-build-social-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var contentRoot = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(contentRoot);
            File.WriteAllText(Path.Combine(contentRoot, "index.md"),
                """
                ---
                title: Home
                description: Home description used for generated metadata.
                ---

                Hello from the build override test.
                """);

            var themeRoot = Path.Combine(root, "themes", "base", "layouts");
            Directory.CreateDirectory(themeRoot);
            File.WriteAllText(Path.Combine(themeRoot, "page.html"),
                """
                <!doctype html><html lang="en"><head><title>{{TITLE}}</title>{{HEAD_HTML}}</head><body><main>{{CONTENT}}</main></body></html>
                """);

            File.WriteAllText(Path.Combine(root, "site.json"),
                """
                {
                  "name": "Build Step Social Test",
                  "baseUrl": "https://example.test",
                  "contentRoot": "content",
                  "themesRoot": "themes",
                  "defaultTheme": "base",
                  "social": {
                    "enabled": true,
                    "image": "/social/default.png",
                    "autoGenerateCards": true,
                    "generatedCardsPath": "/assets/social/generated"
                  },
                  "collections": [
                    { "name": "pages", "input": "content/pages", "output": "/", "defaultLayout": "page" }
                  ]
                }
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "build",
                      "config": "./site.json",
                      "out": "./site",
                      "clean": true,
                      "socialAutoGenerate": false
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success, result.Steps[0].Message);

            var html = File.ReadAllText(Path.Combine(root, "site", "index.html"));
            Assert.DoesNotContain("/assets/social/generated/", html);
            Assert.False(Directory.Exists(Path.Combine(root, "site", "assets", "social", "generated")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort cleanup for Windows test runners that may still hold file handles briefly.
        }
    }
}
