using System;
using System.IO;
using System.Text.Json;
using PowerForge.Web.Cli;
using Xunit;

public partial class WebPipelineRunnerProjectCatalogProductTests
{
    [Theory]
    [InlineData("//untrusted.example/install")]
    [InlineData("/\\untrusted.example/install")]
    [InlineData("/\t/untrusted.example/install")]
    [InlineData("/\n/untrusted.example/install")]
    [InlineData("/\r/untrusted.example/install")]
    public void RunPipeline_ProjectCatalog_RejectsBrowserNormalizedExternalProductAction(string actionUrl)
    {
        var root = CreateTestRoot("protocol-relative-action");

        try
        {
            var serializedActionUrl = JsonSerializer.Serialize(actionUrl);
            WriteCatalog(root,
                $$"""
                {
                  "projects": [
                    {
                      "slug": "unsafe-action",
                      "name": "Unsafe Action",
                      "kind": "product",
                      "mode": "hub-full",
                      "description": "A product with an invalid protocol-relative action.",
                      "links": {
                        "support": "/projects/unsafe-action/#support",
                        "privacy": "/projects/unsafe-action/#privacy"
                      },
                      "brand": {
                        "accent": "#123456",
                        "icon": "/assets/products/unsafe/icon.png",
                        "iconWidth": 256,
                        "iconHeight": 256
                      },
                      "product": {
                        "category": "Utilities",
                        "tagline": "Reject unsafe action targets.",
                        "platforms": ["Web"],
                        "primaryAction": {
                          "label": "Install",
                          "url": {{serializedActionUrl}}
                        },
                        "media": [
                          {
                            "src": "/assets/products/unsafe/home.png",
                            "alt": "Unsafe Action home screen",
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

            Assert.False(result.Success);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("validation failed", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }
}
