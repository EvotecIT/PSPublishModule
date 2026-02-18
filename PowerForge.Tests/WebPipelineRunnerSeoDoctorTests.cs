using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public class WebPipelineRunnerSeoDoctorTests
{
    [Fact]
    public void RunPipeline_SeoDoctor_CanGenerateBaselineAndFailOnNewIssues()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-seo-doctor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var siteRoot = Path.Combine(root, "_site");
            Directory.CreateDirectory(siteRoot);
            Directory.CreateDirectory(Path.Combine(siteRoot, "about"));

            File.WriteAllText(Path.Combine(siteRoot, "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>PowerForge SEO Doctor Home Page</title>
                  <meta name="description" content="This page is intentionally long enough to pass basic SEO doctor title and description checks in tests." />
                </head>
                <body>
                  <h1>Home</h1>
                  <a href="/about/">About</a>
                  <img src="/images/logo.png" alt="Logo" />
                </body>
                </html>
                """);

            File.WriteAllText(Path.Combine(siteRoot, "about", "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>PowerForge SEO Doctor About Page</title>
                  <meta name="description" content="This about page links back to home and should not create orphan warnings in the initial baseline." />
                </head>
                <body>
                  <h1>About</h1>
                  <a href="/">Home</a>
                </body>
                </html>
                """);

            var baselinePath = Path.Combine(root, ".powerforge", "seo-baseline.json");
            var pipelineBaseline = Path.Combine(root, "pipeline-baseline.json");
            File.WriteAllText(pipelineBaseline,
                """
                {
                  "steps": [
                    {
                      "task": "seo-doctor",
                      "siteRoot": "./_site",
                      "baseline": "./.powerforge/seo-baseline.json",
                      "baselineGenerate": true,
                      "reportPath": "./_reports/seo-doctor.json",
                      "summaryPath": "./_reports/seo-doctor.md"
                    }
                  ]
                }
                """);

            var firstResult = WebPipelineRunner.RunPipeline(pipelineBaseline, logger: null);
            Assert.True(firstResult.Success);
            Assert.Single(firstResult.Steps);
            Assert.True(firstResult.Steps[0].Success);
            Assert.True(File.Exists(baselinePath));
            Assert.True(File.Exists(Path.Combine(root, "_reports", "seo-doctor.json")));
            Assert.True(File.Exists(Path.Combine(root, "_reports", "seo-doctor.md")));

            Directory.CreateDirectory(Path.Combine(siteRoot, "orphan"));
            File.WriteAllText(Path.Combine(siteRoot, "orphan", "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>Orphan page for fail-on-new test</title>
                </head>
                <body>
                  <h1>Orphan</h1>
                </body>
                </html>
                """);

            var pipelineFailOnNew = Path.Combine(root, "pipeline-fail-on-new.json");
            File.WriteAllText(pipelineFailOnNew,
                """
                {
                  "steps": [
                    {
                      "task": "seo-doctor",
                      "siteRoot": "./_site",
                      "baseline": "./.powerforge/seo-baseline.json",
                      "failOnNew": true
                    }
                  ]
                }
                """);

            var secondResult = WebPipelineRunner.RunPipeline(pipelineFailOnNew, logger: null);
            Assert.False(secondResult.Success);
            Assert.Single(secondResult.Steps);
            Assert.False(secondResult.Steps[0].Success);
            Assert.Contains("new issues", secondResult.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
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
                Directory.Delete(path, true);
        }
        catch
        {
            // ignore cleanup failures in tests
        }
    }
}
