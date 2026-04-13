using System;
using System.IO;
using PowerForge.Web.Cli;
using Xunit;

public class WebPipelineRunnerRouteFallbacksTests
{
    [Fact]
    public void RunPipeline_RouteFallbacks_WritesRootAndRouteFilesFromManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-route-fallbacks-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var siteRoot = Path.Combine(root, "_site");
            var templateRoot = Path.Combine(root, "templates");
            var dataRoot = Path.Combine(root, "data");
            Directory.CreateDirectory(siteRoot);
            Directory.CreateDirectory(templateRoot);
            Directory.CreateDirectory(dataRoot);

            var templatePath = Path.Combine(templateRoot, "tool-shell.html");
            File.WriteAllText(templatePath,
                """
                <html>
                <head>
                  <title>__PF_TITLE__</title>
                  <meta name="description" content="__PF_DESCRIPTION__" />
                  <link rel="canonical" href="__PF_CANONICAL__" />
                </head>
                <body>
                  <strong>__PF_LOADING_TITLE__</strong>
                  <p>__PF_LOADING_TEXT__</p>
                </body>
                </html>
                """);

            var itemsPath = Path.Combine(dataRoot, "routes.json");
            File.WriteAllText(itemsPath,
                """
                {
                  "routes": [
                    {
                      "slug": "spf",
                      "name": "SPF & MX",
                      "description": "Check SPF <records> & alignment."
                    },
                    {
                      "slug": "dnssec",
                      "name": "DNSSEC Inspector",
                      "description": "Validate DNSSEC state."
                    }
                  ]
                }
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "route-fallbacks",
                      "siteRoot": "./_site",
                      "template": "./templates/tool-shell.html",
                      "items": "./data/routes.json",
                      "destinationTemplate": "tools/{slug}/index.html",
                      "rootOutput": "tools/index.html",
                      "rootValues": {
                        "__PF_TITLE__": "Domain Detective Tools",
                        "__PF_DESCRIPTION__": "Tool directory.",
                        "__PF_CANONICAL__": "https://example.com/tools/",
                        "__PF_LOADING_TITLE__": "Preparing tools",
                        "__PF_LOADING_TEXT__": "Loading the shared toolbox."
                      },
                      "replacements": {
                        "__PF_TITLE__": "{name} | Domain Detective Tools",
                        "__PF_DESCRIPTION__": "{description}",
                        "__PF_CANONICAL__": "https://example.com/tools/{slug}/",
                        "__PF_LOADING_TITLE__": "Preparing {name}",
                        "__PF_LOADING_TEXT__": "Loading the {name} workspace."
                      },
                      "reportPath": "./_reports/route-fallbacks.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("route-fallbacks ok", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            var rootHtml = File.ReadAllText(Path.Combine(siteRoot, "tools", "index.html"));
            Assert.Contains("Domain Detective Tools", rootHtml, StringComparison.Ordinal);
            Assert.Contains("https://example.com/tools/", rootHtml, StringComparison.Ordinal);

            var spfHtml = File.ReadAllText(Path.Combine(siteRoot, "tools", "spf", "index.html"));
            Assert.Contains("SPF &amp; MX | Domain Detective Tools", spfHtml, StringComparison.Ordinal);
            Assert.Contains("Check SPF &lt;records&gt; &amp; alignment.", spfHtml, StringComparison.Ordinal);
            Assert.Contains("https://example.com/tools/spf/", spfHtml, StringComparison.Ordinal);
            Assert.Contains("Preparing SPF &amp; MX", spfHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("__PF_TITLE__", spfHtml, StringComparison.Ordinal);

            Assert.True(File.Exists(Path.Combine(root, "_reports", "route-fallbacks.json")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_RouteFallbacks_FailsWhenRequiredItemValueIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-route-fallbacks-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var siteRoot = Path.Combine(root, "_site");
            Directory.CreateDirectory(siteRoot);

            File.WriteAllText(Path.Combine(root, "template.html"), "<title>__PF_TITLE__</title>");
            File.WriteAllText(Path.Combine(root, "items.json"), """[{ "name": "SPF Lookup" }]""");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "route-fallbacks",
                      "siteRoot": "./_site",
                      "template": "./template.html",
                      "items": "./items.json",
                      "destinationTemplate": "tools/{slug}/index.html",
                      "replacements": {
                        "__PF_TITLE__": "{name}"
                      }
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("missing value 'slug'", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_RouteFallbacks_FailsWhenDestinationEscapesSiteRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-route-fallbacks-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var siteRoot = Path.Combine(root, "_site");
            Directory.CreateDirectory(siteRoot);

            File.WriteAllText(Path.Combine(root, "template.html"), "<title>__PF_TITLE__</title>");
            File.WriteAllText(Path.Combine(root, "items.json"), """[{ "slug": "../../../escape", "name": "SPF Lookup" }]""");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "route-fallbacks",
                      "siteRoot": "./_site",
                      "template": "./template.html",
                      "items": "./items.json",
                      "destinationTemplate": "tools/{slug}/index.html",
                      "replacements": {
                        "__PF_TITLE__": "{name}"
                      }
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("must stay under siteRoot", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
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
