using System;
using System.IO;
using PowerForge.Web.Cli;
using Xunit;

namespace PowerForge.Tests;

public class WebPipelineRunnerHostingStepTests
{
    [Fact]
    public void RunPipeline_HostingStep_KeepsOnlySelectedHostArtifacts()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-hosting-keep-selected-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            CreateSiteFixture(root);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "build",
                      "config": "./site.json",
                      "out": "./_site",
                      "clean": true
                    },
                    {
                      "task": "hosting",
                      "siteRoot": "./_site",
                      "targets": "apache,iis",
                      "removeUnselected": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Equal(2, result.Steps.Count);
            Assert.True(result.Steps[1].Success);

            Assert.True(File.Exists(Path.Combine(root, "_site", ".htaccess")));
            Assert.True(File.Exists(Path.Combine(root, "_site", "web.config")));
            Assert.False(File.Exists(Path.Combine(root, "_site", "_redirects")));
            Assert.False(File.Exists(Path.Combine(root, "_site", "staticwebapp.config.json")));
            Assert.False(File.Exists(Path.Combine(root, "_site", "vercel.json")));
            Assert.False(File.Exists(Path.Combine(root, "_site", "nginx.redirects.conf")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_HostingStep_AcceptsApacheIisAndNginxAliases()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-hosting-aliases-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            CreateSiteFixture(root);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "build",
                      "config": "./site.json",
                      "out": "./_site",
                      "clean": true
                    },
                    {
                      "task": "hosting",
                      "siteRoot": "./_site",
                      "targets": "apache2,microsoft-iis,nginx-conf",
                      "removeUnselected": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Equal(2, result.Steps.Count);
            Assert.True(result.Steps[1].Success);

            Assert.True(File.Exists(Path.Combine(root, "_site", ".htaccess")));
            Assert.True(File.Exists(Path.Combine(root, "_site", "nginx.redirects.conf")));
            Assert.True(File.Exists(Path.Combine(root, "_site", "web.config")));
            Assert.False(File.Exists(Path.Combine(root, "_site", "_redirects")));
            Assert.False(File.Exists(Path.Combine(root, "_site", "staticwebapp.config.json")));
            Assert.False(File.Exists(Path.Combine(root, "_site", "vercel.json")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_HostingStep_StrictFailsWhenSelectedArtifactsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-hosting-strict-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var siteRoot = Path.Combine(root, "_site");
            Directory.CreateDirectory(siteRoot);
            File.WriteAllText(Path.Combine(siteRoot, "index.html"), "<!doctype html><html><body>ok</body></html>");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "hosting",
                      "siteRoot": "./_site",
                      "targets": "apache",
                      "strict": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("missing selected artifacts", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_HostingStep_FailsForUnknownTarget()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-hosting-unknown-target-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var siteRoot = Path.Combine(root, "_site");
            Directory.CreateDirectory(siteRoot);
            File.WriteAllText(Path.Combine(siteRoot, "index.html"), "<!doctype html><html><body>ok</body></html>");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "hosting",
                      "siteRoot": "./_site",
                      "targets": "unknown-target"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("unsupported target", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_HostingStep_AcceptsSiteRootHyphenAlias()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-hosting-site-root-alias-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            CreateSiteFixture(root);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "build",
                      "config": "./site.json",
                      "out": "./_site",
                      "clean": true
                    },
                    {
                      "task": "hosting",
                      "site-root": "./_site",
                      "targets": "nginx",
                      "removeUnselected": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Equal(2, result.Steps.Count);
            Assert.True(result.Steps[1].Success);
            Assert.True(File.Exists(Path.Combine(root, "_site", "nginx.redirects.conf")));
            Assert.False(File.Exists(Path.Combine(root, "_site", "web.config")));
            Assert.False(File.Exists(Path.Combine(root, "_site", ".htaccess")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static void CreateSiteFixture(string root)
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

        var themeRoot = Path.Combine(root, "themes", "pipeline-hosting-test");
        Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
        File.WriteAllText(Path.Combine(themeRoot, "layouts", "home.html"),
            """
            <!doctype html>
            <html><body>{{ content }}</body></html>
            """);
        File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
            """
            {
              "name": "pipeline-hosting-test",
              "engine": "scriban",
              "defaultLayout": "home"
            }
            """);

        File.WriteAllText(Path.Combine(root, "site.json"),
            """
            {
              "Name": "Hosting Pipeline Test",
              "BaseUrl": "https://example.test",
              "ContentRoot": "content",
              "DefaultTheme": "pipeline-hosting-test",
              "ThemesRoot": "themes",
              "Collections": [
                {
                  "Name": "pages",
                  "Input": "content/pages",
                  "Output": "/"
                }
              ],
              "Redirects": [
                {
                  "From": "/docs/api/*",
                  "To": "/api/{path}",
                  "Status": 301,
                  "MatchType": "Wildcard",
                  "PreserveQuery": true
                }
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
