using System;
using System.IO;
using System.Text.Json;
using PowerForge.Web.Cli;
using Xunit;

public class WebPipelineRunnerProjectCatalogProductTests
{
    [Fact]
    public void RunPipeline_ProjectCatalog_GeneratesDedicatedProductProfileWithTypedMedia()
    {
        var root = CreateTestRoot("dedicated-product");

        try
        {
            var catalogPath = WriteCatalog(root,
                """
                {
                  "projects": [
                    {
                      "slug": "casaray",
                      "name": "CasaRay",
                      "kind": "product",
                      "mode": "dedicated-external",
                      "contentMode": "external",
                      "hubPath": "/projects/casaray/",
                      "githubRepo": "EvotecIT/CasaRay",
                      "description": "A private Apple Home companion.",
                      "externalUrl": "https://casaray.dev/",
                      "version": "1.1.0",
                      "aliases": ["/products/casaray/", "/apps/casaray/"],
                      "links": {
                        "source": "https://github.com/EvotecIT/CasaRay",
                        "website": "https://casaray.dev/",
                        "appStore": "https://apps.apple.com/us/app/casaray/id6778025328",
                        "support": "https://casaray.dev/support/",
                        "privacy": "https://casaray.dev/privacy/"
                      },
                      "brand": {
                        "accent": "#635BFF",
                        "icon": "/assets/products/casaray/icon.png",
                        "iconWidth": 1024,
                        "iconHeight": 1024,
                        "socialImage": "/assets/products/casaray/social.png",
                        "socialImageWidth": 1536,
                        "socialImageHeight": 1024
                      },
                      "product": {
                        "layout": "product",
                        "category": "Smart home",
                        "tagline": "A calm, private control surface for Apple Home.",
                        "applicationCategory": "UtilitiesApplication",
                        "platforms": ["iPhone", "iPad", "Mac", "iPhone"],
                        "availability": "available",
                        "highlights": [
                          { "title": "Private by design", "text": "Your home data stays on your devices." }
                        ],
                        "media": [
                          {
                            "src": "/assets/products/casaray/ipad-home.png",
                            "alt": "CasaRay dashboard on iPad",
                            "caption": "A complete home overview.",
                            "width": 1200,
                            "height": 1600,
                            "role": "hero",
                            "frame": "tablet",
                            "fit": "contain"
                          },
                          {
                            "src": "/assets/products/casaray/iphone-controls.png",
                            "alt": "CasaRay controls on iPhone",
                            "width": 720,
                            "height": 1564,
                            "frame": "phone"
                          }
                        ]
                      }
                    }
                  ]
                }
                """);
            var pipelinePath = WritePipeline(root);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.True(result.Steps[0].Success);
            var page = File.ReadAllText(Path.Combine(root, "content", "projects", "casaray.md"));
            Assert.Contains("layout: product", page, StringComparison.Ordinal);
            Assert.Contains("meta.project_kind: \"product\"", page, StringComparison.Ordinal);
            Assert.Contains("meta.product_presentation:", page, StringComparison.Ordinal);
            Assert.DoesNotContain("meta.product:", page, StringComparison.Ordinal);
            Assert.Contains("width: 1200", page, StringComparison.Ordinal);
            Assert.Contains("height: 1600", page, StringComparison.Ordinal);
            Assert.Contains("meta.software.application_category: \"UtilitiesApplication\"", page, StringComparison.Ordinal);
            Assert.Contains("meta.software.download_url: \"https://apps.apple.com/us/app/casaray/id6778025328\"", page, StringComparison.Ordinal);
            Assert.Contains("meta.social_card_image: \"/assets/products/casaray/social.png\"", page, StringComparison.Ordinal);
            Assert.Contains("meta.social_image_width: 1536", page, StringComparison.Ordinal);
            Assert.Contains("meta.social_image_height: 1024", page, StringComparison.Ordinal);

            using var normalized = JsonDocument.Parse(File.ReadAllText(catalogPath));
            var product = normalized.RootElement.GetProperty("projects")[0].GetProperty("product");
            Assert.Equal("Available now", product.GetProperty("availabilityLabel").GetString());
            Assert.Equal(3, product.GetProperty("platforms").GetArrayLength());
            Assert.Equal("gallery", product.GetProperty("media")[1].GetProperty("role").GetString());
            Assert.Equal("contain", product.GetProperty("media")[1].GetProperty("fit").GetString());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectCatalog_DerivesHubProductActionFromSource()
    {
        var root = CreateTestRoot("hub-product");

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
                      "mode": "hub-full",
                      "githubRepo": "EvotecIT/AuthIMO",
                      "description": "A Windows-first authenticator.",
                      "links": {
                        "source": "https://github.com/EvotecIT/AuthIMO",
                        "support": "/projects/authimo/#support",
                        "privacy": "/projects/authimo/#privacy"
                      },
                      "surfaces": {
                        "docs": true,
                        "apiDotNet": true,
                        "examples": true
                      },
                      "brand": {
                        "accent": "#1976D2",
                        "icon": "/assets/products/authimo/icon.png",
                        "iconWidth": 256,
                        "iconHeight": 256
                      },
                      "product": {
                        "category": "Security",
                        "tagline": "Your accounts, codes, and recovery details in one place.",
                        "platforms": ["Windows"],
                        "availability": "beta",
                        "media": [
                          {
                            "src": "/assets/products/authimo/vault.png",
                            "alt": "AuthIMO account vault",
                            "width": 1778,
                            "height": 1389,
                            "frame": "desktop"
                          }
                        ]
                      }
                    }
                  ]
                }
                """);
            var pipelinePath = WritePipeline(root, generateSections: true);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            var page = File.ReadAllText(Path.Combine(root, "content", "projects", "authimo.md"));
            Assert.Contains("layout: project", page, StringComparison.Ordinal);
            Assert.Contains("role: \"hero\"", page, StringComparison.Ordinal);
            Assert.Contains("label: \"View source\"", page, StringComparison.Ordinal);
            Assert.Contains("url: \"https://github.com/EvotecIT/AuthIMO\"", page, StringComparison.Ordinal);
            Assert.DoesNotContain("meta.software.download_url", page, StringComparison.Ordinal);

            foreach (var section in new[] { "docs", "api", "examples" })
            {
                var sectionPage = File.ReadAllText(Path.Combine(root, "content", "projects", $"authimo.{section}.md"));
                Assert.Contains($"meta.project_section: \"{section}\"", sectionPage, StringComparison.Ordinal);
                Assert.Contains("meta.project_link_source: \"https://github.com/EvotecIT/AuthIMO\"", sectionPage, StringComparison.Ordinal);
                Assert.DoesNotContain("meta.product_presentation:", sectionPage, StringComparison.Ordinal);
                Assert.DoesNotContain("meta.software.", sectionPage, StringComparison.Ordinal);
            }
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectCatalog_DerivesPrimaryActionFromWebsiteWithoutPublishingItAsDownload()
    {
        var root = CreateTestRoot("website-product");

        try
        {
            WriteCatalog(root,
                """
                {
                  "projects": [
                    {
                      "slug": "website-product",
                      "name": "Website Product",
                      "kind": "product",
                      "mode": "hub-full",
                      "description": "A product with a website-only call to action.",
                      "links": {
                        "website": "https://product.example/",
                        "support": "https://product.example/support/",
                        "privacy": "https://product.example/privacy/"
                      },
                      "brand": {
                        "accent": "#123456",
                        "icon": "/assets/products/website/icon.png",
                        "iconWidth": 256,
                        "iconHeight": 256
                      },
                      "product": {
                        "category": "Utilities",
                        "tagline": "A website-first product.",
                        "platforms": ["Web"],
                        "media": [
                          {
                            "src": "/assets/products/website/home.png",
                            "alt": "Website Product home screen",
                            "width": 1200,
                            "height": 800,
                            "role": "hero",
                            "frame": "wide",
                            "fit": "contain"
                          }
                        ]
                      }
                    }
                  ]
                }
                """);
            var pipelinePath = WritePipeline(root);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success, result.Steps[0].Message);
            var page = File.ReadAllText(Path.Combine(root, "content", "projects", "website-product.md"));
            Assert.Contains("label: \"Visit product website\"", page, StringComparison.Ordinal);
            Assert.Contains("url: \"https://product.example/\"", page, StringComparison.Ordinal);
            Assert.DoesNotContain("meta.software.download_url", page, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectCatalog_DerivesPrimaryActionFromDownloadsBeforeSource()
    {
        var root = CreateTestRoot("downloads-product");

        try
        {
            WriteCatalog(root,
                """
                {
                  "projects": [
                    {
                      "slug": "downloads-product",
                      "name": "Downloads Product",
                      "kind": "product",
                      "mode": "hub-full",
                      "githubRepo": "EvotecIT/DownloadsProduct",
                      "description": "A product distributed from a downloads page.",
                      "links": {
                        "downloads": "https://product.example/downloads/",
                        "source": "https://github.com/EvotecIT/DownloadsProduct",
                        "support": "https://product.example/support/",
                        "privacy": "https://product.example/privacy/"
                      },
                      "brand": {
                        "accent": "#123456",
                        "icon": "/assets/products/downloads/icon.png",
                        "iconWidth": 256,
                        "iconHeight": 256
                      },
                      "product": {
                        "category": "Utilities",
                        "tagline": "Download the product directly.",
                        "platforms": ["Windows"],
                        "media": [
                          {
                            "src": "/assets/products/downloads/home.png",
                            "alt": "Downloads Product home screen",
                            "width": 1200,
                            "height": 800,
                            "role": "hero",
                            "frame": "wide",
                            "fit": "contain"
                          }
                        ]
                      }
                    }
                  ]
                }
                """);
            var pipelinePath = WritePipeline(root);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            var page = File.ReadAllText(Path.Combine(root, "content", "projects", "downloads-product.md"));
            Assert.Contains("label: \"Download\"", page, StringComparison.Ordinal);
            Assert.Contains("url: \"https://product.example/downloads/\"", page, StringComparison.Ordinal);
            Assert.DoesNotContain("label: \"View source\"", page, StringComparison.Ordinal);
            Assert.Contains("meta.software.download_url: \"https://product.example/downloads/\"", page, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectCatalog_MergesPartialManifestProductPresentationOverCuratedCatalog()
    {
        var root = CreateTestRoot("partial-manifest-product");

        try
        {
            var catalogPath = WriteCatalog(root,
                """
                {
                  "projects": [
                    {
                      "slug": "curated-product",
                      "name": "Curated Product",
                      "kind": "product",
                      "mode": "hub-full",
                      "githubRepo": "EvotecIT/CuratedProduct",
                      "description": "A curated product profile.",
                      "links": {
                        "source": "https://github.com/EvotecIT/CuratedProduct",
                        "support": "/projects/curated-product/#support",
                        "privacy": "/projects/curated-product/#privacy"
                      },
                      "brand": {
                        "accent": "#123456",
                        "icon": "/assets/products/curated/icon.png",
                        "iconWidth": 256,
                        "iconHeight": 256,
                        "socialImage": "/assets/products/curated/social.png",
                        "socialImageWidth": 1200,
                        "socialImageHeight": 630
                      },
                      "product": {
                        "layout": "product",
                        "category": "Original category",
                        "tagline": "Curated tagline.",
                        "applicationCategory": "UtilitiesApplication",
                        "platforms": ["Windows", "macOS"],
                        "availability": "available",
                        "primaryAction": {
                          "label": "View source",
                          "url": "https://github.com/EvotecIT/CuratedProduct"
                        },
                        "highlights": [
                          { "title": "Curated", "text": "Retain this highlight." }
                        ],
                        "media": [
                          {
                            "src": "/assets/products/curated/home.png",
                            "alt": "Curated Product home screen",
                            "width": 1200,
                            "height": 800,
                            "role": "hero",
                            "frame": "wide",
                            "fit": "contain"
                          }
                        ]
                      }
                    }
                  ]
                }
                """);

            var manifestRoot = Path.Combine(root, "projects-sources", "curated-product", "WebsiteArtifacts");
            Directory.CreateDirectory(manifestRoot);
            File.WriteAllText(Path.Combine(manifestRoot, "project-manifest.json"),
                """
                {
                  "slug": "curated-product",
                  "brand": {
                    "accent": "#654321"
                  },
                  "product": {
                    "category": "Manifest category",
                    "primaryAction": {
                      "label": "Install now"
                    }
                  }
                }
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "project-catalog",
                      "catalog": "./data/projects/catalog.json",
                      "sourcesRoot": "./projects-sources",
                      "contentRoot": "./content/projects",
                      "summaryPath": "./summary.json",
                      "importManifests": true,
                      "allowCreateProjects": false,
                      "applyCuration": false,
                      "mergeTelemetry": false,
                      "mergeReleaseTelemetry": false,
                      "generatePages": true,
                      "generateSections": false,
                      "forceOverwriteExisting": true,
                      "validate": true,
                      "failOnWarnings": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            using var normalized = JsonDocument.Parse(File.ReadAllText(catalogPath));
            var project = normalized.RootElement.GetProperty("projects")[0];
            var brand = project.GetProperty("brand");
            var product = project.GetProperty("product");
            Assert.Equal("#654321", brand.GetProperty("accent").GetString());
            Assert.Equal("/assets/products/curated/icon.png", brand.GetProperty("icon").GetString());
            Assert.Equal(256, brand.GetProperty("iconWidth").GetInt32());
            Assert.Equal("/assets/products/curated/social.png", brand.GetProperty("socialImage").GetString());
            Assert.Equal("Manifest category", product.GetProperty("category").GetString());
            Assert.Equal("Curated tagline.", product.GetProperty("tagline").GetString());
            Assert.Equal(2, product.GetProperty("platforms").GetArrayLength());
            Assert.Single(product.GetProperty("media").EnumerateArray());
            Assert.Single(product.GetProperty("highlights").EnumerateArray());
            Assert.Equal("Install now", product.GetProperty("primaryAction").GetProperty("label").GetString());
            Assert.Equal("https://github.com/EvotecIT/CuratedProduct", product.GetProperty("primaryAction").GetProperty("url").GetString());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectCatalog_RejectsUnsupportedExplicitMediaRole()
    {
        var root = CreateTestRoot("invalid-role");

        try
        {
            WriteCatalog(root,
                """
                {
                  "projects": [
                    {
                      "slug": "invalid-role",
                      "name": "Invalid Role",
                      "kind": "product",
                      "mode": "hub-full",
                      "description": "A deliberately invalid media-role fixture.",
                      "links": {
                        "source": "https://github.com/EvotecIT/InvalidRole",
                        "support": "/projects/invalid-role/#support",
                        "privacy": "/projects/invalid-role/#privacy"
                      },
                      "brand": {
                        "accent": "#123456",
                        "icon": "/assets/products/invalid-role/icon.png",
                        "iconWidth": 256,
                        "iconHeight": 256
                      },
                      "product": {
                        "category": "Utilities",
                        "tagline": "An invalid product fixture.",
                        "platforms": ["Windows"],
                        "media": [
                          {
                            "src": "/assets/products/invalid-role/banner.png",
                            "alt": "Invalid banner role fixture",
                            "width": 1200,
                            "height": 630,
                            "role": "banner",
                            "frame": "wide",
                            "fit": "contain"
                          }
                        ]
                      }
                    }
                  ]
                }
                """);
            var pipelinePath = WritePipeline(root);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("validation failed", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectCatalog_RejectsProductMediaWithoutIntrinsicDimensions()
    {
        var root = CreateTestRoot("invalid-media");

        try
        {
            WriteCatalog(root,
                """
                {
                  "projects": [
                    {
                      "slug": "broken-product",
                      "name": "Broken Product",
                      "kind": "product",
                      "mode": "hub-full",
                      "githubRepo": "EvotecIT/BrokenProduct",
                      "links": {
                        "source": "https://github.com/EvotecIT/BrokenProduct",
                        "support": "/projects/broken-product/#support",
                        "privacy": "/projects/broken-product/#privacy"
                      },
                      "brand": {
                        "accent": "#123456",
                        "icon": "/assets/products/broken/icon.png",
                        "iconWidth": 256,
                        "iconHeight": 256
                      },
                      "product": {
                        "category": "Utilities",
                        "tagline": "A deliberately invalid product fixture.",
                        "platforms": ["Windows"],
                        "primaryAction": { "label": "View source", "url": "https://github.com/EvotecIT/BrokenProduct" },
                        "media": [
                          {
                            "src": "/assets/products/broken/screenshot.png",
                            "alt": "Broken product screenshot",
                            "width": 0,
                            "height": 900,
                            "role": "hero",
                            "frame": "desktop",
                            "fit": "contain"
                          }
                        ]
                      }
                    }
                  ]
                }
                """);
            var pipelinePath = WritePipeline(root);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("validation failed", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static string CreateTestRoot(string name)
    {
        var root = Path.Combine(Path.GetTempPath(), $"pf-web-pipeline-product-{name}-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static string WriteCatalog(string root, string json)
    {
        var catalogPath = Path.Combine(root, "data", "projects", "catalog.json");
        Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
        File.WriteAllText(catalogPath, json);
        return catalogPath;
    }

    private static string WritePipeline(string root, bool generateSections = false)
    {
        var pipelinePath = Path.Combine(root, "pipeline.json");
        var pipeline =
            """
            {
              "steps": [
                {
                  "task": "project-catalog",
                  "catalog": "./data/projects/catalog.json",
                  "contentRoot": "./content/projects",
                  "publishPath": "./static/data/projects/catalog.json",
                  "summaryPath": "./summary.json",
                  "importManifests": false,
                  "applyCuration": false,
                  "mergeTelemetry": false,
                  "mergeReleaseTelemetry": false,
                  "generatePages": true,
                  "generateSections": false,
                  "forceOverwriteExisting": true,
                  "validate": true,
                  "failOnWarnings": true
                }
              ]
            }
            """;
        if (generateSections)
            pipeline = pipeline.Replace("\"generateSections\": false", "\"generateSections\": true", StringComparison.Ordinal);
        File.WriteAllText(pipelinePath, pipeline);
        return pipelinePath;
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
            // Ignore cleanup failures in tests.
        }
    }
}
