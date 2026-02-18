using System.Xml.Linq;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public class WebPipelineRunnerSitemapTests
{
    [Fact]
    public void RunPipeline_Sitemap_CanGenerateNewsAndIndexOutputs()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-sitemap-news-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var siteRoot = Path.Combine(root, "site");
            Directory.CreateDirectory(siteRoot);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "sitemap",
                      "siteRoot": "./site",
                      "baseUrl": "https://example.test",
                      "includeHtmlFiles": false,
                      "includeTextFiles": false,
                      "entries": [
                        {
                          "path": "/news/launch/",
                          "title": "Launch",
                          "lastmod": "2026-02-18"
                        }
                      ],
                      "newsOut": "./site/sitemap-news.xml",
                      "newsPaths": ["/news/**"],
                      "newsMetadata": {
                        "publicationName": "Example Product",
                        "publicationLanguage": "en"
                      },
                      "sitemapIndex": "./site/sitemap-index.xml"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success, result.Steps[0].Message);
            Assert.Contains("news", result.Steps[0].Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("index", result.Steps[0].Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);

            var sitemapPath = Path.Combine(siteRoot, "sitemap.xml");
            var newsPath = Path.Combine(siteRoot, "sitemap-news.xml");
            var indexPath = Path.Combine(siteRoot, "sitemap-index.xml");
            Assert.True(File.Exists(sitemapPath));
            Assert.True(File.Exists(newsPath));
            Assert.True(File.Exists(indexPath));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_Sitemap_HyphenAliases_CanGenerateNewsMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-sitemap-news-alias-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var siteRoot = Path.Combine(root, "site");
            Directory.CreateDirectory(siteRoot);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "sitemap",
                      "siteRoot": "./site",
                      "baseUrl": "https://example.test",
                      "includeHtmlFiles": false,
                      "includeTextFiles": false,
                      "entries": [
                        {
                          "path": "/news/coverage/",
                          "title": "Coverage"
                        }
                      ],
                      "news-out": "./site/sitemap-news.xml",
                      "news-paths": ["/news/**"],
                      "news-metadata": {
                        "publication-name": "Alias Publication",
                        "publication-language": "pl",
                        "keywords": "release,coverage"
                      }
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success, result.Steps[0].Message);

            var newsPath = Path.Combine(siteRoot, "sitemap-news.xml");
            Assert.True(File.Exists(newsPath));

            var sitemapNs = XNamespace.Get("http://www.sitemaps.org/schemas/sitemap/0.9");
            var newsNs = XNamespace.Get("http://www.google.com/schemas/sitemap-news/0.9");
            var doc = XDocument.Load(newsPath);
            var newsNode = doc.Descendants(newsNs + "news").FirstOrDefault();
            Assert.NotNull(newsNode);
            Assert.Equal(
                "Alias Publication",
                newsNode!
                    .Element(newsNs + "publication")?
                    .Element(newsNs + "name")?
                    .Value);
            Assert.Equal(
                "pl",
                newsNode
                    .Element(newsNs + "publication")?
                    .Element(newsNs + "language")?
                    .Value);
            Assert.Contains(
                "https://example.test/news/coverage/",
                doc.Descendants(sitemapNs + "loc").Select(node => node.Value),
                StringComparer.OrdinalIgnoreCase);
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
            // ignore cleanup failures
        }
    }
}
