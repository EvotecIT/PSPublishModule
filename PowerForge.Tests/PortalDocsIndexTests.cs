using System.Net;
using System.Text;
using System.Text.Json;
using PowerForge;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class PortalDocsIndexTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    [Fact]
    public void WebPortalDocsGenerator_ComposesLocalAndPackageDocs()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-portal-docs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "content", "docs"));
        Directory.CreateDirectory(Path.Combine(root, "data", "private-gallery"));

        try
        {
            File.WriteAllText(Path.Combine(root, "content", "docs", "getting-started.md"),
                """
                ---
                title: Getting Started
                ---

                Internal install and onboarding steps.
                """);

            var gallery = new PrivateGalleryDocument
            {
                Packages =
                {
                    new PrivateGalleryPackage
                    {
                        Name = "Contoso.Tools",
                        LatestVersion = "1.2.3",
                        WebUrl = "https://dev.azure.com/org/project/_artifacts/feed/feed/package/Contoso.Tools",
                        Module = new PrivateGalleryModuleMetadata
                        {
                            Name = "Contoso.Tools",
                            Version = "1.2.3",
                            Documents =
                            {
                                new PrivateGalleryDocumentAsset
                                {
                                    Path = "Contoso.Tools/README.md",
                                    Kind = "readme",
                                    Title = "Contoso Tools",
                                    Content = "# Contoso Package Docs\n\nBundled package guidance."
                                }
                            }
                        }
                    }
                }
            };
            File.WriteAllText(Path.Combine(root, "data", "private-gallery", "feed.json"), JsonSerializer.Serialize(gallery, JsonOptions));

            File.WriteAllText(Path.Combine(root, "portal.sources.json"),
                """
                {
                  "schemaVersion": 1,
                  "format": "powerforge.portal.sources",
                  "sources": [
                    {
                      "id": "portal-local",
                      "kind": "local",
                      "path": "./content",
                      "include": [ "docs/**/*.md" ],
                      "placement": { "surface": "knowledge-base", "navigationGroup": "Documentation" }
                    },
                    {
                      "id": "contoso-package",
                      "kind": "package",
                      "module": "Contoso.Tools",
                      "placement": { "surface": "module", "module": "Contoso.Tools", "navigationGroup": "Bundled module docs" }
                    }
                  ]
                }
                """);

            var result = WebPortalDocsGenerator.Generate(new WebPortalDocsOptions
            {
                BaseDirectory = root,
                SourcesPath = "./portal.sources.json",
                PrivateGalleryPath = "./data/private-gallery/feed.json",
                OutputDirectory = "./data/portal"
            });

            Assert.Equal(2, result.SourceCount);
            Assert.Equal(2, result.DocumentCount);
            Assert.Empty(result.Warnings);

            var docs = JsonSerializer.Deserialize<WebPortalDocsDocument>(File.ReadAllText(result.DocsPath), JsonOptions)!;
            Assert.Equal(2, docs.Documents.Count);
            Assert.Contains(docs.Documents, doc => doc.SourceKind == "local" && doc.Title == "Getting Started" && doc.Kind == "docs");
            Assert.Contains(docs.Documents, doc =>
                doc.SourceKind == "package" &&
                doc.Module == "Contoso.Tools" &&
                doc.Kind == "readme" &&
                doc.Title == "Contoso Package Docs" &&
                doc.Content!.Contains("Bundled package guidance.", StringComparison.Ordinal));

            var search = JsonSerializer.Deserialize<WebPortalDocsSearchDocument>(File.ReadAllText(result.SearchPath), JsonOptions)!;
            Assert.Contains(search.Entries, entry => entry.Title == "Getting Started");
            Assert.Contains(search.Entries, entry => entry.Module == "Contoso.Tools" && entry.Summary == "Bundled package guidance.");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void WebPortalDocsGenerator_HonorsContentPolicyForPackageDocs()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-portal-docs-package-policy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "data", "private-gallery"));

        try
        {
            var gallery = new PrivateGalleryDocument
            {
                Packages =
                {
                    new PrivateGalleryPackage
                    {
                        Name = "Contoso.Tools",
                        Module = new PrivateGalleryModuleMetadata
                        {
                            Name = "Contoso.Tools",
                            Documents =
                            {
                                new PrivateGalleryDocumentAsset
                                {
                                    Path = "README.md",
                                    Kind = "readme",
                                    Content = "1234567890"
                                }
                            }
                        }
                    }
                }
            };
            File.WriteAllText(Path.Combine(root, "data", "private-gallery", "feed.json"), JsonSerializer.Serialize(gallery, JsonOptions));
            File.WriteAllText(Path.Combine(root, "portal.sources.json"),
                """
                {
                  "sources": [
                    { "id": "pkg", "kind": "package", "module": "Contoso.Tools" }
                  ]
                }
                """);

            var withoutContent = WebPortalDocsGenerator.Generate(new WebPortalDocsOptions
            {
                BaseDirectory = root,
                SourcesPath = "./portal.sources.json",
                PrivateGalleryPath = "./data/private-gallery/feed.json",
                OutputDirectory = "./data/portal-off",
                IncludeContent = false
            });
            var withoutDocs = JsonSerializer.Deserialize<WebPortalDocsDocument>(File.ReadAllText(withoutContent.DocsPath), JsonOptions)!;
            Assert.Null(Assert.Single(withoutDocs.Documents).Content);

            var limited = WebPortalDocsGenerator.Generate(new WebPortalDocsOptions
            {
                BaseDirectory = root,
                SourcesPath = "./portal.sources.json",
                PrivateGalleryPath = "./data/private-gallery/feed.json",
                OutputDirectory = "./data/portal-limited",
                MaxContentBytes = 4
            });
            var limitedDocs = JsonSerializer.Deserialize<WebPortalDocsDocument>(File.ReadAllText(limited.DocsPath), JsonOptions)!;
            Assert.Equal("1234", Assert.Single(limitedDocs.Documents).Content);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void WebPortalDocsGenerator_FetchesGitHubRepositoryDocs()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-portal-docs-gh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "portal.sources.json"),
                """
                {
                  "defaults": { "branch": "main" },
                  "sources": [
                    {
                      "id": "contoso-repo",
                      "kind": "github",
                      "owner": "EvotecIT",
                      "repo": "Contoso.Tools",
                      "module": "Contoso.Tools",
                      "include": [ "README.md", "Docs/**/*.md" ],
                      "relationshipDefaults": { "module": "Contoso.Tools", "tags": [ "Internal" ] }
                    }
                  ]
                }
                """);

            var handler = new StubHttpMessageHandler(request =>
            {
                if (request.RequestUri!.AbsoluteUri.Contains("/git/trees/main", StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            """
                            {
                              "tree": [
                                { "path": "README.md", "type": "blob" },
                                { "path": "Docs/Operators.md", "type": "blob" },
                                { "path": "src/ignored.cs", "type": "blob" }
                              ]
                            }
                            """,
                            Encoding.UTF8,
                            "application/json")
                    };
                }

                var body = request.RequestUri.AbsolutePath.EndsWith("/README.md", StringComparison.OrdinalIgnoreCase)
                    ? "# Contoso Tools\n\nRepository maintained docs."
                    : "# Operators\n\nOperator-facing guidance.";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "text/markdown")
                };
            });

            var result = WebPortalDocsGenerator.Generate(new WebPortalDocsOptions
            {
                BaseDirectory = root,
                SourcesPath = "./portal.sources.json",
                OutputDirectory = "./data/portal"
            }, handler);

            Assert.Equal(1, result.SourceCount);
            Assert.Equal(2, result.DocumentCount);
            Assert.Empty(result.Warnings);

            var docs = JsonSerializer.Deserialize<WebPortalDocsDocument>(File.ReadAllText(result.DocsPath), JsonOptions)!;
            Assert.Contains(docs.Documents, doc => doc.Title == "Contoso Tools" && doc.RawUrl!.Contains("raw.githubusercontent.com", StringComparison.Ordinal));
            Assert.Contains(docs.Documents, doc => doc.Title == "Operators" && doc.Module == "Contoso.Tools" && doc.Tags.Contains("Internal"));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void WebPortalDocsGenerator_FetchesAzureDevOpsRepositoryDocs()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-portal-docs-azdo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "portal.sources.json"),
                """
                {
                  "sources": [
                    {
                      "id": "contoso-azdo",
                      "kind": "azure-devops",
                      "organization": "evotecpl",
                      "project": "PowerShellGallery",
                      "repository": "Contoso.Tools",
                      "branch": "main",
                      "authentication": "pat",
                      "include": [ "README.md", "Docs/**/*.md" ],
                      "relationshipDefaults": { "module": "Contoso.Tools", "tags": [ "AzureDevOps" ] }
                    }
                  ]
                }
                """);

            var handler = new StubHttpMessageHandler(request =>
            {
                Assert.Equal("Basic OnRva2Vu", request.Headers.Authorization?.ToString());

                if (request.RequestUri!.AbsoluteUri.Contains("recursionLevel=Full", StringComparison.Ordinal))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            """
                            {
                              "value": [
                                { "path": "/README.md", "gitObjectType": "blob" },
                                { "path": "/Docs/Runbook.md", "gitObjectType": "blob" },
                                { "path": "/Docs/Nested", "gitObjectType": "tree", "isFolder": true }
                              ]
                            }
                            """,
                            Encoding.UTF8,
                            "application/json")
                    };
                }

                var content = request.RequestUri.AbsoluteUri.Contains("README.md", StringComparison.Ordinal)
                    ? "# Contoso Tools\n\nAzure DevOps maintained docs."
                    : "# Runbook\n\nOperations runbook.";
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(JsonSerializer.Serialize(new { content }), Encoding.UTF8, "application/json")
                };
            });

            var result = WebPortalDocsGenerator.Generate(new WebPortalDocsOptions
            {
                BaseDirectory = root,
                SourcesPath = "./portal.sources.json",
                OutputDirectory = "./data/portal",
                Token = "token"
            }, handler);

            Assert.Equal(1, result.SourceCount);
            Assert.Equal(2, result.DocumentCount);
            Assert.Empty(result.Warnings);

            var docs = JsonSerializer.Deserialize<WebPortalDocsDocument>(File.ReadAllText(result.DocsPath), JsonOptions)!;
            Assert.Contains(docs.Documents, doc => doc.Title == "Contoso Tools" && doc.SourceKind == "azure-devops" && doc.Module == "Contoso.Tools");
            Assert.Contains(docs.Documents, doc => doc.Title == "Runbook" && doc.Tags.Contains("AzureDevOps"));
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
            // Ignore cleanup failures in tests.
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_handler(request));
    }
}
