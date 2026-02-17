using PowerForge.Web;

namespace PowerForge.Tests;

public class WebApiDocsSocialMetaTests
{
    [Fact]
    public void Generate_Html_EmitsOpenGraphAndTwitterMeta_ForApiIndex()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-apidocs-social-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "api.xml");
            File.WriteAllText(xmlPath,
                """
                <doc>
                  <assembly><name>Sample.Api</name></assembly>
                  <members></members>
                </doc>
                """);

            var navJsonPath = Path.Combine(root, "site.json");
            File.WriteAllText(navJsonPath,
                """
                {
                  "Name": "Example Site",
                  "BaseUrl": "https://example.test",
                  "Social": {
                    "Image": "/assets/social/share-card.png",
                    "TwitterCard": "summary_large_image"
                  }
                }
                """);

            var outPath = Path.Combine(root, "_site", "api");
            var result = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                Type = ApiDocsType.CSharp,
                XmlPath = xmlPath,
                OutputPath = outPath,
                Title = "Sample API Reference",
                BaseUrl = "/api",
                Format = "html",
                Template = "docs",
                NavJsonPath = navJsonPath
            });

            var htmlIndexPath = Path.Combine(outPath, "index.html");
            Assert.True(File.Exists(htmlIndexPath));
            var html = File.ReadAllText(htmlIndexPath);
            Assert.Contains("property=\"og:title\"", html, StringComparison.Ordinal);
            Assert.Contains("property=\"og:url\" content=\"https://example.test/api\"", html, StringComparison.Ordinal);
            Assert.Contains("property=\"og:image\" content=\"https://example.test/assets/social/share-card.png\"", html, StringComparison.Ordinal);
            Assert.Contains("property=\"og:image:alt\" content=\"Sample API Reference\"", html, StringComparison.Ordinal);
            Assert.Contains("name=\"twitter:card\" content=\"summary_large_image\"", html, StringComparison.Ordinal);
            Assert.Contains("name=\"twitter:image\" content=\"https://example.test/assets/social/share-card.png\"", html, StringComparison.Ordinal);
            Assert.Contains("name=\"twitter:image:alt\" content=\"Sample API Reference\"", html, StringComparison.Ordinal);
            Assert.Contains("<link rel=\"canonical\" href=\"https://example.test/api\"", html, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Generate_Html_AutoGeneratesSocialCard_ForApiIndex()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-apidocs-social-generated-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "api.xml");
            File.WriteAllText(xmlPath,
                """
                <doc>
                  <assembly><name>Sample.Api</name></assembly>
                  <members></members>
                </doc>
                """);

            var navJsonPath = Path.Combine(root, "site.json");
            File.WriteAllText(navJsonPath,
                """
                {
                  "Name": "Example Site",
                  "BaseUrl": "https://example.test",
                  "Social": {
                    "AutoGenerateCards": true,
                    "GeneratedCardsPath": "/assets/social/generated"
                  }
                }
                """);

            var outPath = Path.Combine(root, "_site", "api");
            _ = WebApiDocsGenerator.Generate(new WebApiDocsOptions
            {
                Type = ApiDocsType.CSharp,
                XmlPath = xmlPath,
                OutputPath = outPath,
                Title = "Sample API Reference",
                BaseUrl = "/api",
                Format = "html",
                Template = "docs",
                NavJsonPath = navJsonPath,
                AutoGenerateSocialCards = true,
                SocialCardPath = "/assets/social/generated/api"
            });

            var htmlIndexPath = Path.Combine(outPath, "index.html");
            Assert.True(File.Exists(htmlIndexPath));
            var html = File.ReadAllText(htmlIndexPath);

            const string marker = "property=\"og:image\" content=\"";
            var start = html.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(start >= 0, "Expected og:image meta tag.");

            var valueStart = start + marker.Length;
            var valueEnd = html.IndexOf('"', valueStart);
            Assert.True(valueEnd > valueStart, "Expected og:image content value.");
            var imageUrl = html[valueStart..valueEnd];

            Assert.StartsWith("https://example.test/assets/social/generated/api/", imageUrl, StringComparison.Ordinal);

            var relativePath = imageUrl.Replace("https://example.test/", string.Empty, StringComparison.Ordinal);
            var generatedPath = Path.Combine(root, "_site", relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(generatedPath), $"Generated API social card missing: {generatedPath}");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
