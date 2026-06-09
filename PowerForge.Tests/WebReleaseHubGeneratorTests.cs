using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Tests;

public class WebReleaseHubGeneratorTests
{
    [Fact]
    public void Generate_FromLocalReleasesJson_ClassifiesAssetsAndMarksLatest()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-release-hub-generator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var releasesPath = Path.Combine(root, "releases.json");
            File.WriteAllText(releasesPath,
                """
                [
                  {
                    "tag_name": "v1.2.0",
                    "name": "OfficeIMO 1.2.0",
                    "published_at": "2026-02-20T10:15:00Z",
                    "prerelease": false,
                    "draft": false,
                    "body": "Stable release",
                    "assets": [
                      {
                        "name": "OfficeIMO.Word-v1.2.0-win-x64.zip",
                        "browser_download_url": "https://example.test/OfficeIMO.Word-v1.2.0-win-x64.zip",
                        "size": 1200,
                        "content_type": "application/zip"
                      },
                      {
                        "name": "OfficeIMO.Excel-v1.2.0-linux-arm64.tar.gz",
                        "browser_download_url": "https://example.test/OfficeIMO.Excel-v1.2.0-linux-arm64.tar.gz",
                        "size": 2200,
                        "content_type": "application/gzip"
                      }
                    ]
                  },
                  {
                    "tag_name": "v1.3.0-preview1",
                    "name": "OfficeIMO 1.3.0 Preview",
                    "published_at": "2026-02-25T09:00:00Z",
                    "prerelease": true,
                    "draft": false,
                    "body": "Preview release",
                    "assets": [
                      {
                        "name": "OfficeIMO.Word-v1.3.0-preview1-osx-arm64.zip",
                        "browser_download_url": "https://example.test/OfficeIMO.Word-v1.3.0-preview1-osx-arm64.zip",
                        "size": 1400,
                        "content_type": "application/zip"
                      }
                    ]
                  }
                ]
                """);

            var outputPath = Path.Combine(root, "release-hub.json");
            var result = WebReleaseHubGenerator.Generate(new WebReleaseHubOptions
            {
                Source = WebChangelogSource.File,
                ReleasesPath = releasesPath,
                OutputPath = outputPath,
                Title = "OfficeIMO Releases",
                AssetRules =
                {
                    new WebReleaseHubAssetRuleInput
                    {
                        Product = "officeimo.word",
                        Label = "OfficeIMO.Word",
                        Match = { "OfficeIMO.Word*.zip" },
                        Kind = "zip"
                    },
                    new WebReleaseHubAssetRuleInput
                    {
                        Product = "officeimo.excel",
                        Label = "OfficeIMO.Excel",
                        Match = { "OfficeIMO.Excel*" }
                    }
                }
            });

            Assert.True(File.Exists(outputPath));
            Assert.Equal(2, result.ReleaseCount);
            Assert.Equal(3, result.AssetCount);
            Assert.Empty(result.Warnings);

            using var doc = JsonDocument.Parse(File.ReadAllText(outputPath));
            var json = doc.RootElement;
            Assert.Equal("OfficeIMO Releases", json.GetProperty("title").GetString());
            Assert.Equal("v1.2.0", json.GetProperty("latest").GetProperty("stableTag").GetString());
            Assert.Equal("v1.3.0-preview1", json.GetProperty("latest").GetProperty("prereleaseTag").GetString());

            var products = json.GetProperty("products");
            Assert.Contains(products.EnumerateArray(), entry =>
                string.Equals(entry.GetProperty("id").GetString(), "officeimo.word", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(products.EnumerateArray(), entry =>
                string.Equals(entry.GetProperty("id").GetString(), "officeimo.excel", StringComparison.OrdinalIgnoreCase));

            var releases = json.GetProperty("releases");
            Assert.Equal(2, releases.GetArrayLength());
            Assert.True(releases[0].GetProperty("isLatestPrerelease").GetBoolean());
            Assert.True(releases[1].GetProperty("isLatestStable").GetBoolean());

            var previewAsset = releases[0].GetProperty("assets")[0];
            Assert.Equal("officeimo.word", previewAsset.GetProperty("product").GetString());
            Assert.Equal("preview", previewAsset.GetProperty("channel").GetString());
            Assert.Equal("macos", previewAsset.GetProperty("platform").GetString());
            Assert.Equal("arm64", previewAsset.GetProperty("arch").GetString());
            Assert.Equal("zip", previewAsset.GetProperty("kind").GetString());

            var stableAsset = releases[1].GetProperty("assets")[1];
            Assert.Equal("officeimo.excel", stableAsset.GetProperty("product").GetString());
            Assert.Equal("stable", stableAsset.GetProperty("channel").GetString());
            Assert.Equal("linux", stableAsset.GetProperty("platform").GetString());
            Assert.Equal("arm64", stableAsset.GetProperty("arch").GetString());
            Assert.Equal("tar.gz", stableAsset.GetProperty("kind").GetString());
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
