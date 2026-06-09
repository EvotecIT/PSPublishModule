using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public class WebPipelineRunnerModeStrictDefaultsTests
{
    [Fact]
    public void RunPipeline_Verify_ModeCi_EnablesStrictNavLintDefaults()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-mode-ci-verify-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            CreateVerifyFixture(root);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "verify",
                      "config": "./site.json"
                    }
                  ]
                }
                """);

            var defaultMode = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(defaultMode.Success);
            Assert.Single(defaultMode.Steps);
            Assert.True(defaultMode.Steps[0].Success, defaultMode.Steps[0].Message);

            var ciMode = WebPipelineRunner.RunPipeline(pipelinePath, logger: null, mode: "ci");
            Assert.False(ciMode.Success);
            Assert.Single(ciMode.Steps);
            Assert.False(ciMode.Steps[0].Success);
            Assert.Contains("fail-on-nav-lint", ciMode.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static void CreateVerifyFixture(string root)
    {
        var pagesPath = Path.Combine(root, "content", "pages");
        Directory.CreateDirectory(pagesPath);
        File.WriteAllText(Path.Combine(pagesPath, "index.md"),
            """
            ---
            title: Home
            slug: index
            ---

            Home
            """);

        var themeRoot = Path.Combine(root, "themes", "mode-ci-theme");
        Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
        File.WriteAllText(Path.Combine(themeRoot, "layouts", "home.html"),
            """
            <!doctype html>
            <html>
            <body>{{ content }}</body>
            </html>
            """);
        File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
            """
            {
              "name": "mode-ci-theme",
              "engine": "scriban",
              "defaultLayout": "home"
            }
            """);

        File.WriteAllText(Path.Combine(root, "site.json"),
            """
            {
              "Name": "Pipeline Mode CI Verify Test",
              "BaseUrl": "https://example.test",
              "ContentRoot": "content",
              "DefaultTheme": "mode-ci-theme",
              "ThemesRoot": "themes",
              "Features": [ "docs", "apiDocs" ],
              "Collections": [
                { "Name": "pages", "Input": "content/pages", "Output": "/" }
              ],
              "Navigation": {
                "Menus": [
                  {
                    "Name": "main",
                    "Items": [
                      { "Text": "Home", "Url": "/" },
                      { "Text": "Docs", "Url": "/docs/" },
                      { "Text": "API", "Url": "/api/" }
                    ]
                  }
                ]
              }
            }
            """);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
            // ignore cleanup failures in tests
        }
    }
}
