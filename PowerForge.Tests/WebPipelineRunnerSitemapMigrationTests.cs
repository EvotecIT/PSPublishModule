using System;
using System.IO;
using System.Text.Json;
using PowerForge.Web.Cli;
using Xunit;

namespace PowerForge.Tests;

public sealed class WebPipelineRunnerSitemapMigrationTests
{
    [Fact]
    public void RunPipeline_LinksCompareSitemaps_WritesRedirectAndReviewArtifacts()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-links-sitemaps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "_site"));
            Directory.CreateDirectory(Path.Combine(root, "data", "migration"));
            File.WriteAllText(Path.Combine(root, "data", "migration", "legacy-sitemap.xml"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url><loc>https://evotec.xyz/old-post/</loc></url>
                  <url><loc>https://evotec.xyz/category/powershell/</loc></url>
                  <url><loc>https://evotec.xyz/missing/</loc></url>
                </urlset>
                """);
            File.WriteAllText(Path.Combine(root, "_site", "sitemap.xml"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url><loc>https://evotec.xyz/blog/old-post/</loc></url>
                  <url><loc>https://evotec.xyz/categories/powershell/</loc></url>
                </urlset>
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "links-compare-sitemaps",
                      "legacySitemaps": ["./data/migration/legacy-sitemap.xml"],
                      "newSitemap": "./_site/sitemap.xml",
                      "newSiteRoot": "./_site",
                      "out": "./Build/link-reports/sitemap-migration.json",
                      "redirectCsv": "./data/redirects/legacy-generated.csv",
                      "reviewCsv": "./Build/link-reports/sitemap-review.csv"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.Contains("links-compare-sitemaps ok", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            var redirectCsv = File.ReadAllText(Path.Combine(root, "data", "redirects", "legacy-generated.csv"));
            Assert.Contains("https://evotec.xyz/old-post/,https://evotec.xyz/blog/old-post/,301,root-to-blog", redirectCsv, StringComparison.Ordinal);
            Assert.Contains("https://evotec.xyz/category/powershell/,https://evotec.xyz/categories/powershell/,301,category-to-categories", redirectCsv, StringComparison.Ordinal);

            var reviewCsv = File.ReadAllText(Path.Combine(root, "Build", "link-reports", "sitemap-review.csv"));
            Assert.Contains("https://evotec.xyz/missing/,,missing", reviewCsv, StringComparison.Ordinal);

            using var summary = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Build", "link-reports", "sitemap-migration.json")));
            Assert.Equal(3, summary.RootElement.GetProperty("legacyUrlCount").GetInt32());
            Assert.Equal(2, summary.RootElement.GetProperty("newUrlCount").GetInt32());
            Assert.Equal(3, summary.RootElement.GetProperty("missingLegacyCount").GetInt32());
            Assert.Equal(1, summary.RootElement.GetProperty("reviewCount").GetInt32());
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
            // best-effort cleanup
        }
    }
}
