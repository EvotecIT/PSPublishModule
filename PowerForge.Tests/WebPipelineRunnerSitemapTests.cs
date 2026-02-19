using System.Xml.Linq;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public class WebPipelineRunnerSitemapTests
{
    [Fact]
    public void RunPipeline_Sitemap_CanResolveSiteRootAndBaseUrlFromConfigAndBuild()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-sitemap-config-fallback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var contentRoot = Path.Combine(root, "content", "pages");
            Directory.CreateDirectory(contentRoot);
            File.WriteAllText(Path.Combine(contentRoot, "index.md"),
                """
                ---
                title: Home
                ---

                Hello
                """);

            var themeRoot = Path.Combine(root, "themes", "base", "layouts");
            Directory.CreateDirectory(themeRoot);
            File.WriteAllText(Path.Combine(themeRoot, "page.html"),
                """
                <!doctype html><html><head><title>{{TITLE}}</title></head><body>{{CONTENT}}</body></html>
                """);

            File.WriteAllText(Path.Combine(root, "site.json"),
                """
                {
                  "name": "Sitemap Config Fallback",
                  "baseUrl": "https://example.test",
                  "contentRoot": "content",
                  "themesRoot": "themes",
                  "defaultTheme": "base",
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
                    { "task": "build", "config": "./site.json", "out": "./site", "clean": true },
                    { "task": "sitemap", "dependsOn": "build", "config": "./site.json" }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.Equal(2, result.Steps.Count);
            Assert.True(result.Steps[1].Success, result.Steps[1].Message);

            var sitemapPath = Path.Combine(root, "site", "sitemap.xml");
            Assert.True(File.Exists(sitemapPath));
            var content = File.ReadAllText(sitemapPath);
            Assert.Contains("https://example.test/", content, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

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

    [Fact]
    public void RunPipeline_Sitemap_CanGenerateImageAndVideoOutputs()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-sitemap-media-" + Guid.NewGuid().ToString("N"));
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
                          "path": "/showcase/reel/",
                          "title": "Reel",
                          "images": ["/assets/reel.png"],
                          "videos": ["/media/reel.mp4"]
                        }
                      ],
                      "imageOut": "./site/sitemap-images.xml",
                      "imagePaths": ["/showcase/**"],
                      "videoOut": "./site/sitemap-videos.xml",
                      "videoPaths": ["/showcase/**"],
                      "indexOut": "./site/sitemap-index.xml"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success, result.Steps[0].Message);
            Assert.Contains("images", result.Steps[0].Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("videos", result.Steps[0].Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("index", result.Steps[0].Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);

            var imagePath = Path.Combine(siteRoot, "sitemap-images.xml");
            var videoPath = Path.Combine(siteRoot, "sitemap-videos.xml");
            var indexPath = Path.Combine(siteRoot, "sitemap-index.xml");
            Assert.True(File.Exists(imagePath));
            Assert.True(File.Exists(videoPath));
            Assert.True(File.Exists(indexPath));
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
