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
            Assert.Contains("meta.product:", page, StringComparison.Ordinal);
            Assert.Contains("width: 1200", page, StringComparison.Ordinal);
            Assert.Contains("height: 1600", page, StringComparison.Ordinal);
            Assert.Contains("meta.software.application_category: \"UtilitiesApplication\"", page, StringComparison.Ordinal);
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
            var pipelinePath = WritePipeline(root);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            var page = File.ReadAllText(Path.Combine(root, "content", "projects", "authimo.md"));
            Assert.Contains("role: \"hero\"", page, StringComparison.Ordinal);
            Assert.Contains("label: \"View source\"", page, StringComparison.Ordinal);
            Assert.Contains("url: \"https://github.com/EvotecIT/AuthIMO\"", page, StringComparison.Ordinal);
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

    private static string WritePipeline(string root)
    {
        var pipelinePath = Path.Combine(root, "pipeline.json");
        File.WriteAllText(pipelinePath,
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
            """);
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
