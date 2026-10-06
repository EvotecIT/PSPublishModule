using System;
using System.IO;
using System.Text.Json;
using PowerForge.Web.Cli;
using Xunit;

public partial class WebPipelineRunnerProjectCatalogProductTests
{
    [Fact]
    public void RunPipeline_ProjectCatalog_GeneratesSeparateProductPageAndKeepsProjectPage()
    {
        var root = CreateTestRoot("separate-product-page");

        try
        {
            var catalogPath = WriteCatalog(root,
                """
                {
                  "projects": [
                    {
                      "slug": "officeimo",
                      "name": "OfficeIMO",
                      "kind": "product",
                      "mode": "dedicated-external",
                      "contentMode": "external",
                      "githubRepo": "EvotecIT/OfficeIMO",
                      "description": "Create, convert, and read Office and PDF files.",
                      "externalUrl": "https://officeimo.com/",
                      "aliases": ["/products/officeimo/", "/apps/officeimo/", "/officeimo-library/"],
                      "links": {
                        "source": "https://github.com/EvotecIT/OfficeIMO",
                        "support": "https://github.com/EvotecIT/OfficeIMO/issues",
                        "privacy": "https://officeimo.com/privacy/"
                      },
                      "surfaces": { "docs": true },
                      "brand": { "accent": "#2563EB", "icon": "/assets/products/officeimo/icon.png", "iconWidth": 256, "iconHeight": 256 },
                      "product": {
                        "category": "Documents",
                        "tagline": "One document engine. Every way you work.",
                        "platforms": ["Windows", "macOS", "Linux"],
                        "channels": [
                          { "kind": "winget", "status": "coming-soon", "command": "winget install EvotecIT.OfficeIMO.Studio" },
                          { "kind": "MicrosoftStore", "url": "https://apps.microsoft.com/detail/xp9m6j7hvm2hz0", "platforms": ["Windows"] },
                          { "kind": "nuget", "command": "dotnet add package OfficeIMO.Word", "url": "https://www.nuget.org/packages/OfficeIMO.Word" }
                        ],
                        "media": [
                          { "src": "/assets/products/officeimo/studio.png", "alt": "OfficeIMO Studio workspace", "width": 1440, "height": 960, "role": "hero" }
                        ]
                      }
                    }
                  ]
                }
                """);
            var pipelinePath = WriteProductPagesPipeline(root);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success, result.Steps[0].Message);
            var projectPage = File.ReadAllText(Path.Combine(root, "content", "projects", "officeimo.md"));
            Assert.Contains("layout: project", projectPage, StringComparison.Ordinal);
            Assert.Contains("meta.project_product_path: \"/products/officeimo/\"", projectPage, StringComparison.Ordinal);
            Assert.Contains("/officeimo-library/", projectPage, StringComparison.Ordinal);
            Assert.DoesNotContain("  - \"/products/officeimo/\"", projectPage, StringComparison.Ordinal);
            Assert.DoesNotContain("/apps/officeimo/", projectPage, StringComparison.Ordinal);
            Assert.DoesNotContain("meta.product_presentation:", projectPage, StringComparison.Ordinal);

            var productPage = File.ReadAllText(Path.Combine(root, "content", "products", "officeimo.md"));
            Assert.Contains("layout: product", productPage, StringComparison.Ordinal);
            Assert.Contains("meta.product_page: true", productPage, StringComparison.Ordinal);
            Assert.Contains("meta.product_project_path: \"/projects/officeimo/\"", productPage, StringComparison.Ordinal);
            Assert.Contains("  - \"/apps/officeimo/\"", productPage, StringComparison.Ordinal);
            Assert.DoesNotContain("  - \"/projects/officeimo/\"", productPage, StringComparison.Ordinal);
            Assert.Contains("    - kind: \"microsoftStore\"", productPage, StringComparison.Ordinal);
            Assert.Contains("      label: \"Microsoft Store\"", productPage, StringComparison.Ordinal);
            Assert.Contains("      command: \"winget install EvotecIT.OfficeIMO.Studio\"", productPage, StringComparison.Ordinal);
            Assert.Contains("      status: \"coming-soon\"", productPage, StringComparison.Ordinal);
            Assert.Contains("meta.software.download_url: \"https://apps.microsoft.com/detail/xp9m6j7hvm2hz0\"", productPage, StringComparison.Ordinal);

            using var normalized = JsonDocument.Parse(File.ReadAllText(catalogPath));
            var product = normalized.RootElement.GetProperty("projects")[0].GetProperty("product");
            Assert.Equal("/products/officeimo/", product.GetProperty("path").GetString());
            Assert.Equal("microsoftStore", product.GetProperty("channels")[1].GetProperty("kind").GetString());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectCatalog_PrivateProductOnlyGetsProductPageAndRedirectsOldProjectRoute()
    {
        var root = CreateTestRoot("private-product-page");

        try
        {
            WriteCatalog(root,
                """
                {
                  "projects": [
                    {
                      "slug": "casaray",
                      "name": "CasaRay",
                      "kind": "product",
                      "mode": "dedicated-external",
                      "contentMode": "external",
                      "description": "Home Assistant on Apple devices.",
                      "externalUrl": "https://casaray.dev/",
                      "aliases": ["/products/casaray/", "/apps/casaray/"],
                      "links": {
                        "support": "https://casaray.dev/support/",
                        "privacy": "https://casaray.dev/privacy/",
                        "appStore": "https://apps.apple.com/us/app/casaray/id6778025328"
                      },
                      "surfaces": { "docs": false },
                      "brand": { "accent": "#73C7E6", "icon": "/assets/products/casaray/icon.png", "iconWidth": 1024, "iconHeight": 1024 },
                      "product": {
                        "category": "Smart home",
                        "tagline": "Your home, calm and native.",
                        "platforms": ["iPhone"],
                        "media": [
                          { "src": "/assets/products/casaray/ipad-home.webp", "alt": "CasaRay on iPad", "width": 1200, "height": 1600, "role": "hero" }
                        ]
                      }
                    }
                  ]
                }
                """);
            var staleProjectPage = Path.Combine(root, "content", "projects", "casaray.md");
            Directory.CreateDirectory(Path.GetDirectoryName(staleProjectPage)!);
            File.WriteAllText(staleProjectPage, "---\ntitle: CasaRay\nmeta.generated_by: powerforge.project-catalog\n---\n");
            var pipelinePath = WriteProductPagesPipeline(root, generateSections: true);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success, result.Steps[0].Message);
            Assert.False(File.Exists(staleProjectPage));
            Assert.False(File.Exists(Path.Combine(root, "content", "projects", "casaray.docs.md")));
            var productPage = File.ReadAllText(Path.Combine(root, "content", "products", "casaray.md"));
            Assert.Contains("  - \"/apps/casaray/\"", productPage, StringComparison.Ordinal);
            Assert.Contains("  - \"/projects/casaray/\"", productPage, StringComparison.Ordinal);
            Assert.Contains("  project_page: false", productPage, StringComparison.Ordinal);
            Assert.DoesNotContain("meta.product_project_path", productPage, StringComparison.Ordinal);
            Assert.Contains("meta.software.download_url: \"https://apps.apple.com/us/app/casaray/id6778025328\"", productPage, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectCatalog_RejectsUnknownProductChannel()
    {
        var root = CreateTestRoot("invalid-product-channel");

        try
        {
            WriteCatalog(root,
                """
                {
                  "projects": [
                    {
                      "slug": "authimo",
                      "name": "AuthIMO",
                      "kind": "product",
                      "description": "An authenticator.",
                      "links": { "support": "/contact/", "privacy": "/privacy/", "source": "https://github.com/EvotecIT/AuthIMO" },
                      "brand": { "accent": "#146EF5", "icon": "/assets/products/authimo/icon.svg", "iconWidth": 36, "iconHeight": 36 },
                      "product": {
                        "category": "Identity",
                        "tagline": "MFA codes in one vault.",
                        "platforms": ["Windows"],
                        "channels": [ { "kind": "floppyDisk", "url": "https://example.com/" } ],
                        "media": [ { "src": "/a.png", "alt": "AuthIMO accounts", "width": 10, "height": 10, "role": "hero" } ]
                      }
                    }
                  ]
                }
                """);
            var pipelinePath = WriteProductPagesPipeline(root);
            var catalogPath = Path.Combine(root, "data", "projects", "catalog.json");
            var invalidCatalog = File.ReadAllText(catalogPath);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Contains("validation failed", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            // The same catalog with a known channel kind passes, so the unknown kind caused the failure.
            File.WriteAllText(catalogPath, invalidCatalog.Replace("\"floppyDisk\"", "\"githubReleases\"", StringComparison.Ordinal));
            var validResult = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(validResult.Success, validResult.Steps[0].Message);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectCatalog_ManifestCanClearWebsiteLink()
    {
        var root = CreateTestRoot("manifest-clears-website");

        try
        {
            var catalogPath = WriteCatalog(root,
                """
                {
                  "projects": [
                    {
                      "slug": "chartforgex",
                      "name": "ChartForgeX",
                      "mode": "hub-full",
                      "externalUrl": "https://evotecit.github.io/ChartForgeX/",
                      "links": { "website": "https://evotecit.github.io/ChartForgeX/", "source": "https://github.com/EvotecIT/ChartForgeX" }
                    }
                  ]
                }
                """);
            var manifestPath = Path.Combine(root, "projects-sources", "chartforgex", "WebsiteArtifacts", "project-manifest.json");
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            File.WriteAllText(manifestPath,
                """
                { "slug": "chartforgex", "name": "ChartForgeX", "links": { "source": "https://github.com/EvotecIT/ChartForgeX", "website": null } }
                """);
            var pipelinePath = WriteProductPagesPipeline(root);
            File.WriteAllText(pipelinePath, File.ReadAllText(pipelinePath)
                .Replace("\"importManifests\": false", "\"importManifests\": true, \"sourcesRoot\": \"./projects-sources\"", StringComparison.Ordinal));

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success, result.Steps[0].Message);
            using var normalized = JsonDocument.Parse(File.ReadAllText(catalogPath));
            var project = normalized.RootElement.GetProperty("projects")[0];
            Assert.True(project.GetProperty("externalUrl").ValueKind == JsonValueKind.Null);
            Assert.False(project.GetProperty("links").TryGetProperty("website", out _));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static string WriteProductPagesPipeline(string root, bool generateSections = false)
    {
        var pipelinePath = WritePipeline(root, generateSections);
        File.WriteAllText(pipelinePath, File.ReadAllText(pipelinePath)
            .Replace("\"contentRoot\": \"./content/projects\",", "\"contentRoot\": \"./content/projects\",\n      \"productContentRoot\": \"./content/products\",", StringComparison.Ordinal));
        return pipelinePath;
    }
}
