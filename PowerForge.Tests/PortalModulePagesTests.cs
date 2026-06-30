using System.Text.Json;
using PowerForge;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class PortalModulePagesTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    [Fact]
    public void WebPrivateGalleryPageGenerator_ComposesPackageAndPortalDocs()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-portal-pages-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "data", "private-gallery"));
        Directory.CreateDirectory(Path.Combine(root, "data", "portal"));

        try
        {
            WriteSampleGallery(Path.Combine(root, "data", "private-gallery", "feed.json"));
            WriteSamplePortalDocs(Path.Combine(root, "data", "portal", "docs.json"));

            var result = WebPrivateGalleryPageGenerator.Generate(new WebPrivateGalleryPageOptions
            {
                BaseDirectory = root,
                PrivateGalleryPath = "./data/private-gallery/feed.json",
                PortalDocsPath = "./data/portal/docs.json",
                OutputDirectory = "./content/generated/modules",
                ProfileName = "EvotecPowerShellGallery",
                RepositoryName = "PowerShellGalleryFeed"
            });

            Assert.Equal(1, result.ModulePageCount);
            Assert.Equal(1, result.DocumentPageCount);
            Assert.Empty(result.Warnings);

            var modulePage = Path.Combine(root, "content", "generated", "modules", "contoso-tools", "index.md");
            Assert.True(File.Exists(modulePage));
            var moduleMarkdown = File.ReadAllText(modulePage);
            Assert.Contains("Install-ManagedModule -ProfileName 'EvotecPowerShellGallery' -Name 'Contoso.Tools'", moduleMarkdown);
            Assert.Contains("| `Get-ContosoTool` | Function | Gets a tool. |", moduleMarkdown);
            Assert.Contains("[Operator Guide](../contoso-tools-operator-guide/)", moduleMarkdown);
            Assert.Contains("| Pester | 5.7.1 |", moduleMarkdown);

            var docPage = Path.Combine(root, "content", "generated", "modules", "contoso-tools-operator-guide", "index.md");
            Assert.True(File.Exists(docPage));
            var docMarkdown = File.ReadAllText(docPage);
            Assert.Contains("Module: [Contoso.Tools](../contoso-tools/)", docMarkdown);
            Assert.Contains("Repository-owned operator guidance.", docMarkdown);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_PortalModulePages_GeneratesContentPages()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-portal-pages-pipeline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "data", "private-gallery"));
        Directory.CreateDirectory(Path.Combine(root, "data", "portal"));

        try
        {
            WriteSampleGallery(Path.Combine(root, "data", "private-gallery", "feed.json"));
            WriteSamplePortalDocs(Path.Combine(root, "data", "portal", "docs.json"));
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "portal-module-pages",
                      "privateGallery": "./data/private-gallery/feed.json",
                      "portalDocs": "./data/portal/docs.json",
                      "profileName": "EvotecPowerShellGallery",
                      "out": "./content/generated/modules"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("modules=1", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(root, "content", "generated", "modules", "contoso-tools", "index.md")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_PortalModulePages_UsesSurfaceSpecificLayouts()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-portal-pages-layouts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "data", "private-gallery"));
        Directory.CreateDirectory(Path.Combine(root, "data", "portal"));

        try
        {
            WriteSampleGallery(Path.Combine(root, "data", "private-gallery", "feed.json"));
            WriteSamplePortalDocs(Path.Combine(root, "data", "portal", "docs.json"));
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "portal-module-pages",
                      "privateGallery": "./data/private-gallery/feed.json",
                      "portalDocs": "./data/portal/docs.json",
                      "profileName": "EvotecPowerShellGallery",
                      "repositoryName": "PublishedGalleryName",
                      "layout": "page",
                      "indexLayout": "module-catalog",
                      "moduleLayout": "module-detail",
                      "documentLayout": "module-document",
                      "out": "./content/generated/modules"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            var outDir = Path.Combine(root, "content", "generated", "modules");
            var indexMarkdown = File.ReadAllText(Path.Combine(outDir, "index.md"));
            var moduleMarkdown = File.ReadAllText(Path.Combine(outDir, "contoso-tools", "index.md"));
            var docMarkdown = File.ReadAllText(Path.Combine(outDir, "contoso-tools-operator-guide", "index.md"));

            Assert.Contains("layout: module-catalog", indexMarkdown);
            Assert.Contains("meta.pageKind: \"module-catalog\"", indexMarkdown);
            Assert.Contains("meta.moduleCount: \"1\"", indexMarkdown);
            Assert.Contains("meta.repositoryName: \"PublishedGalleryName\"", indexMarkdown);
            Assert.Contains("layout: module-detail", moduleMarkdown);
            Assert.Contains("meta.pageKind: \"module-detail\"", moduleMarkdown);
            Assert.Contains("meta.commandCount: \"1\"", moduleMarkdown);
            Assert.Contains("meta.portalDocCount: \"1\"", moduleMarkdown);
            Assert.Contains("layout: module-document", docMarkdown);
            Assert.Contains("meta.pageKind: \"module-document\"", docMarkdown);
            Assert.Contains("meta.navigationGroup: \"Repository docs\"", docMarkdown);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void WebPrivateGalleryPageGenerator_DisambiguatesSlugCollisions()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-portal-pages-collisions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "data", "private-gallery"));
        Directory.CreateDirectory(Path.Combine(root, "data", "portal"));

        try
        {
            var gallery = new PrivateGalleryDocument
            {
                Feed = new PrivateGalleryFeed { Name = "CompanyFeed", RepositoryName = "CompanyGallery" },
                Packages =
                {
                    CreatePackage("Contoso.Tools"),
                    CreatePackage("Contoso-Tools")
                }
            };
            File.WriteAllText(Path.Combine(root, "data", "private-gallery", "feed.json"), JsonSerializer.Serialize(gallery, JsonOptions));

            var docs = new WebPortalDocsDocument
            {
                Documents =
                {
                    CreatePortalDoc("readme-one", "README", "Docs/README.md", "Contoso.Tools", "One"),
                    CreatePortalDoc("readme-two", "README", "More/README.md", "Contoso.Tools", "Two")
                }
            };
            File.WriteAllText(Path.Combine(root, "data", "portal", "docs.json"), JsonSerializer.Serialize(docs, JsonOptions));

            var result = WebPrivateGalleryPageGenerator.Generate(new WebPrivateGalleryPageOptions
            {
                BaseDirectory = root,
                PrivateGalleryPath = "./data/private-gallery/feed.json",
                PortalDocsPath = "./data/portal/docs.json",
                OutputDirectory = "./content/generated/modules"
            });

            Assert.Equal(2, result.ModulePageCount);
            Assert.Equal(2, result.DocumentPageCount);
            var generated = Directory.GetFiles(Path.Combine(root, "content", "generated", "modules"), "index.md", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(Path.Combine(root, "content", "generated", "modules"), Path.GetDirectoryName(path)!))
                .ToArray();

            Assert.Equal(generated.Length, generated.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Contains("contoso-tools", generated);
            Assert.Contains(generated, path => path.StartsWith("contoso-tools-", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(5, generated.Length);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_PortalModulePages_RejectsEmptyOutputBeforeDeleting()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-portal-pages-empty-output-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "data", "private-gallery"));
        File.WriteAllText(Path.Combine(root, "keep.txt"), "do not delete");
        WriteSampleGallery(Path.Combine(root, "data", "private-gallery", "feed.json"));

        try
        {
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "portal-module-pages",
                      "privateGallery": "./data/private-gallery/feed.json",
                      "out": ""
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Contains("requires out/outputDirectory", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(root, "keep.txt")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static void WriteSampleGallery(string path)
    {
        var gallery = new PrivateGalleryDocument
        {
            Title = "Contoso Gallery",
            Feed = new PrivateGalleryFeed
            {
                Name = "CompanyFeed",
                RepositoryName = "CompanyGallery"
            },
            Packages =
            {
                new PrivateGalleryPackage
                {
                    Id = "Contoso.Tools",
                    Name = "Contoso.Tools",
                    LatestVersion = "1.2.3",
                    Description = "Contoso operator tools.",
                    Versions =
                    {
                        new PrivateGalleryPackageVersion
                        {
                            Id = "1.2.3",
                            Version = "1.2.3",
                            IsLatest = true,
                            IsListed = true,
                            PublishedAtUtc = new DateTimeOffset(2026, 5, 24, 8, 0, 0, TimeSpan.Zero)
                        }
                    },
                    Module = new PrivateGalleryModuleMetadata
                    {
                        Name = "Contoso.Tools",
                        Version = "1.2.3",
                        Description = "Contoso operator tools.",
                        Author = "Contoso",
                        PowerShellVersion = "5.1",
                        Commands =
                        {
                            new PrivateGalleryCommandMetadata
                            {
                                Name = "Get-ContosoTool",
                                Kind = "Function",
                                Synopsis = "Gets a tool."
                            }
                        },
                        Documents =
                        {
                            new PrivateGalleryDocumentAsset
                            {
                                Path = "README.md",
                                Kind = "readme",
                                Title = "README",
                                Size = 42
                            }
                        },
                        RequiredModules =
                        {
                            new PrivateGalleryDependency
                            {
                                Name = "Pester",
                                VersionRange = "5.7.1"
                            }
                        }
                    }
                }
            }
        };
        File.WriteAllText(path, JsonSerializer.Serialize(gallery, JsonOptions));
    }

    private static void WriteSamplePortalDocs(string path)
    {
        var docs = new WebPortalDocsDocument
        {
            Documents =
            {
                new WebPortalDocEntry
                {
                    Id = "contoso-operator-guide",
                    Title = "Operator Guide",
                    Kind = "docs",
                    SourceId = "contoso-repo",
                    SourceKind = "github",
                    Path = "Docs/OperatorGuide.md",
                    Module = "Contoso.Tools",
                    Surface = "module",
                    NavigationGroup = "Repository docs",
                    Summary = "Operator-facing module guidance.",
                    Content = "# Operator Guide\n\nRepository-owned operator guidance.",
                    Url = "https://github.com/contoso/tools/blob/main/Docs/OperatorGuide.md"
                }
            }
        };
        docs.Summary.DocumentCount = 1;
        File.WriteAllText(path, JsonSerializer.Serialize(docs, JsonOptions));
    }

    private static PrivateGalleryPackage CreatePackage(string name)
    {
        return new PrivateGalleryPackage
        {
            Id = name,
            Name = name,
            LatestVersion = "1.0.0",
            Module = new PrivateGalleryModuleMetadata
            {
                Name = name,
                Version = "1.0.0"
            }
        };
    }

    private static WebPortalDocEntry CreatePortalDoc(string id, string title, string path, string module, string body)
    {
        return new WebPortalDocEntry
        {
            Id = id,
            Title = title,
            Kind = "docs",
            SourceId = "repo",
            SourceKind = "github",
            Path = path,
            Module = module,
            Content = "# " + title + "\n\n" + body
        };
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
