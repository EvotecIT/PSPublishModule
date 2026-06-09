using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public class WebPipelineRunnerDoctorDefaultsTests
{
    [Fact]
    public void RunPipeline_Doctor_DoesNotEnableSeoMetaChecks_ByDefault()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-doctor-seo-default-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            CreateDoctorFixture(root);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "doctor",
                      "config": "./site.json",
                      "siteRoot": "./_site",
                      "noBuild": true,
                      "noVerify": true,
                      "summary": true,
                      "summaryPath": "./_reports/doctor-summary.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);

            var summaryPath = Path.Combine(root, "_site", "_reports", "doctor-summary.json");
            Assert.True(File.Exists(summaryPath));
            var summary = File.ReadAllText(summaryPath);
            Assert.DoesNotContain("seo-missing-canonical", summary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_Doctor_CanEnableSeoMetaChecks_Explicitly()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-doctor-seo-enabled-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            CreateDoctorFixture(root);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "doctor",
                      "config": "./site.json",
                      "siteRoot": "./_site",
                      "noBuild": true,
                      "noVerify": true,
                      "checkSeoMeta": true,
                      "summary": true,
                      "summaryPath": "./_reports/doctor-summary.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);

            var summaryPath = Path.Combine(root, "_site", "_reports", "doctor-summary.json");
            Assert.True(File.Exists(summaryPath));
            var summary = File.ReadAllText(summaryPath);
            Assert.Contains("seo-missing-canonical", summary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static void CreateDoctorFixture(string root)
    {
        var siteRoot = Path.Combine(root, "_site");
        Directory.CreateDirectory(siteRoot);

        File.WriteAllText(Path.Combine(siteRoot, "index.html"),
            """
            <!doctype html>
            <html>
            <head>
              <title>Doctor SEO Default Test</title>
            </head>
            <body>
              <h1>Home</h1>
            </body>
            </html>
            """);
        File.WriteAllText(Path.Combine(siteRoot, "404.html"),
            """
            <!doctype html>
            <html>
            <head>
              <title>Not Found</title>
            </head>
            <body>
              <h1>404</h1>
            </body>
            </html>
            """);

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

        var themeRoot = Path.Combine(root, "themes", "doctor-default-theme");
        Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
        File.WriteAllText(Path.Combine(themeRoot, "layouts", "home.html"),
            """
            <!doctype html>
            <html><body>{{ content }}</body></html>
            """);
        File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
            """
            {
              "name": "doctor-default-theme",
              "engine": "scriban",
              "defaultLayout": "home"
            }
            """);

        File.WriteAllText(Path.Combine(root, "site.json"),
            """
            {
              "Name": "Doctor Default Test",
              "BaseUrl": "https://example.test",
              "ContentRoot": "content",
              "DefaultTheme": "doctor-default-theme",
              "ThemesRoot": "themes",
              "Collections": [
                { "Name": "pages", "Input": "content/pages", "Output": "/" }
              ]
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
