using System;
using System.IO;
using PowerForge.Web;
using Xunit;

public class WebLlmsGeneratorTests
{
    [Fact]
    public void Generate_SiteContentOmitsInventedPackageMetadataAndKeepsCuratedQuickstart()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-site-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var quickstartPath = Path.Combine(root, "quickstart.txt");
            File.WriteAllText(quickstartPath, "search --query example");

            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                ContentKind = WebLlmsContentKind.Site,
                Name = "Example Portal",
                PackageId = "example.invalid",
                Version = "1.2.3",
                QuickstartPath = quickstartPath
            });

            Assert.Equal(0, result.PackageCount);
            Assert.Empty(result.PackageId);
            Assert.Empty(result.Version);

            var llmsTxt = File.ReadAllText(result.LlmsTxtPath);
            Assert.Contains("# Example Portal", llmsTxt, StringComparison.Ordinal);
            Assert.Contains("> Example Portal website.", llmsTxt, StringComparison.Ordinal);
            Assert.DoesNotContain("API reference", llmsTxt, StringComparison.Ordinal);
            Assert.Contains("search --query example", llmsTxt, StringComparison.Ordinal);
            Assert.DoesNotContain("## Metadata", llmsTxt, StringComparison.Ordinal);
            Assert.DoesNotContain("## Install", llmsTxt, StringComparison.Ordinal);
            Assert.DoesNotContain("## Machine-friendly API data", llmsTxt, StringComparison.Ordinal);
            Assert.DoesNotContain("Slug rule:", llmsTxt, StringComparison.Ordinal);
            Assert.DoesNotContain("example.invalid", llmsTxt, StringComparison.Ordinal);

            var llmsFull = File.ReadAllText(result.LlmsFullPath);
            Assert.DoesNotContain("## Installation", llmsFull, StringComparison.Ordinal);
            Assert.DoesNotContain("- Package:", llmsFull, StringComparison.Ordinal);
            Assert.DoesNotContain("- Version:", llmsFull, StringComparison.Ordinal);
            Assert.DoesNotContain("## API Resources", llmsFull, StringComparison.Ordinal);

            var llmsJson = File.ReadAllText(result.LlmsJsonPath);
            Assert.DoesNotContain("\"package\"", llmsJson, StringComparison.Ordinal);
            Assert.DoesNotContain("\"version\"", llmsJson, StringComparison.Ordinal);
            Assert.DoesNotContain("\"install\"", llmsJson, StringComparison.Ordinal);
            Assert.DoesNotContain("\"api\"", llmsJson, StringComparison.Ordinal);
            Assert.DoesNotContain("\"apiCatalogs\"", llmsJson, StringComparison.Ordinal);
            Assert.DoesNotContain("\"apiTypeCount\"", llmsJson, StringComparison.Ordinal);
            Assert.Contains("\"quickstartLanguage\": \"shell\"", llmsJson, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_RejectsUndefinedContentKind()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-content-kind-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                ContentKind = (WebLlmsContentKind)42
            }));

            Assert.Equal("ContentKind", exception.ParamName);
            Assert.Contains("Expected Package or Site", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_RejectsUndefinedApiDetailLevel()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-api-level-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var exception = Assert.Throws<ArgumentOutOfRangeException>(() => WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                Name = "Example Product",
                PackageId = "Example.Product",
                ApiDetailLevel = (WebApiDetailLevel)42
            }));

            Assert.Equal("ApiDetailLevel", exception.ParamName);
            Assert.Contains("Expected None, Summary, or Full", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_RecognizesCSharpInTextQuickstart()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-csharp-text-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var quickstartPath = Path.Combine(root, "quickstart.txt");
            File.WriteAllText(quickstartPath,
                """
                using Example.Product;

                var client = new ExampleClient();
                """);

            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                Name = "Example Product",
                PackageId = "Example.Product",
                QuickstartPath = quickstartPath
            });

            var llmsJson = File.ReadAllText(result.LlmsJsonPath);
            Assert.Contains("\"quickstartLanguage\": \"csharp\"", llmsJson, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_DotNetPackageWithoutCuratedQuickstartOmitsPlaceholderSection()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-no-placeholder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                Name = "Example Product",
                PackageId = "Example.Product"
            });

            var llmsTxt = File.ReadAllText(result.LlmsTxtPath);
            Assert.Contains("dotnet add package Example.Product", llmsTxt, StringComparison.Ordinal);
            Assert.DoesNotContain("## Quickstart", llmsTxt, StringComparison.Ordinal);
            Assert.DoesNotContain("TODO", llmsTxt, StringComparison.OrdinalIgnoreCase);

            var llmsJson = File.ReadAllText(result.LlmsJsonPath);
            Assert.DoesNotContain("\"quickstart\"", llmsJson, StringComparison.Ordinal);
            Assert.DoesNotContain("\"quickstartLanguage\"", llmsJson, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_ThrowsWhenConfiguredQuickstartDoesNotExist()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-missing-quickstart-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var missingPath = Path.Combine(root, "missing.cs");
            var exception = Assert.Throws<FileNotFoundException>(() => WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                Name = "Example Product",
                PackageId = "Example.Product",
                QuickstartPath = missingPath
            }));

            Assert.Equal(missingPath, exception.FileName);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("Install-Module Example.Product", "powershell")]
    [InlineData("dotnet run", "shell")]
    [InlineData("npm install", "shell")]
    public void Generate_InfersCuratedTextQuickstartLanguage(string content, string language)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-text-language-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var quickstartPath = Path.Combine(root, "quickstart.txt");
            File.WriteAllText(quickstartPath, content);
            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                Name = "Example Product",
                PackageId = "Example.Product",
                QuickstartPath = quickstartPath
            });

            Assert.Contains($"\"quickstartLanguage\": \"{language}\"", File.ReadAllText(result.LlmsJsonPath), StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_RejectsEmptyConfiguredQuickstart()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-empty-quickstart-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var quickstartPath = Path.Combine(root, "quickstart.txt");
            File.WriteAllText(quickstartPath, " \r\n");
            var exception = Assert.Throws<InvalidDataException>(() => WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                Name = "Example Product",
                PackageId = "Example.Product",
                QuickstartPath = quickstartPath
            }));
            Assert.Contains("quickstart file is empty", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_WritesRecommendedLlmsTxtMarkdownLinks()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-markdown-links-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            WriteApiIndex(root, string.Empty, "Example.Product", 1);
            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                Name = "Example Product",
                PackageId = "Example.Product",
                Overview = "Example Product publishes API docs and implementation guidance.",
                ApiBase = "/projects/example/api/"
            });

            var llmsTxt = File.ReadAllText(result.LlmsTxtPath);
            Assert.Contains("# Example Product", llmsTxt, StringComparison.Ordinal);
            Assert.Contains("## Machine-friendly API data", llmsTxt, StringComparison.Ordinal);
            Assert.Contains("- [API index](/projects/example/api/index.json):", llmsTxt, StringComparison.Ordinal);
            Assert.Contains("- [API search](/projects/example/api/search.json):", llmsTxt, StringComparison.Ordinal);
            Assert.Contains("- [API type template](/projects/example/api/types/{slug}.json):", llmsTxt, StringComparison.Ordinal);
            const string slugRule = "lower-case; remove generic arity markers (one or two backticks followed by digits); replace remaining non-alphanumerics with dashes; collapse and trim dashes";
            Assert.Contains($"Slug rule: {slugRule}.", llmsTxt, StringComparison.Ordinal);
            Assert.Contains($"- Slug rule: {slugRule}.", File.ReadAllText(result.LlmsFullPath), StringComparison.Ordinal);
            Assert.Contains($"\"slugRule\": \"{slugRule}\"", File.ReadAllText(result.LlmsJsonPath), StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_SiteNameComesFromHomepageInsteadOfOutputDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-site-title-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "<html><head><title>Example Portal</title></head><body><h1>Ignored Heading</h1></body></html>");
            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                ContentKind = WebLlmsContentKind.Site
            });

            Assert.Equal("Example Portal", result.Name);
            Assert.Contains("# Example Portal", File.ReadAllText(result.LlmsTxtPath), StringComparison.Ordinal);
            Assert.DoesNotContain(Path.GetFileName(root), File.ReadAllText(result.LlmsTxtPath), StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_RejectsMissingAndInvalidConfiguredApiIndexes()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-invalid-api-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var missing = Path.Combine(root, "api", "missing.json");
            Assert.Throws<FileNotFoundException>(() => WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                ContentKind = WebLlmsContentKind.Site,
                Name = "Example Portal",
                ApiIndexPath = missing
            }));

            Directory.CreateDirectory(Path.GetDirectoryName(missing)!);
            File.WriteAllText(missing, "{ invalid");
            Assert.Throws<InvalidDataException>(() => WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                ContentKind = WebLlmsContentKind.Site,
                Name = "Example Portal",
                ApiIndexPath = missing
            }));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_PackageWithoutPresentApiIndexOmitsApiResources()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-package-no-api-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                Name = "Example Product",
                PackageId = "Example.Product"
            });

            Assert.Equal(0, result.ApiCatalogCount);
            var llmsTxt = File.ReadAllText(result.LlmsTxtPath);
            Assert.DoesNotContain("API index", llmsTxt, StringComparison.Ordinal);
            Assert.DoesNotContain("API reference", llmsTxt, StringComparison.Ordinal);
            Assert.DoesNotContain("\"api\"", File.ReadAllText(result.LlmsJsonPath), StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_RejectsExternalMultipleApiIndexesWhoseRoutesCannotBeProven()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-external-api-site-" + Guid.NewGuid().ToString("N"));
        var external = Path.Combine(Path.GetTempPath(), "pf-web-llms-external-api-data-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(external);

        try
        {
            var first = WriteApiIndex(external, "first", "Example.First", 1);
            var second = WriteApiIndex(external, "second", "Example.Second", 1);
            var exception = Assert.Throws<InvalidOperationException>(() => WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                ContentKind = WebLlmsContentKind.Site,
                Name = "Example Portal",
                ApiIndexPaths = new[] { first, second }
            }));
            Assert.Contains("Cannot infer a published API route", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
            TryDeleteDirectory(external);
        }
    }

    [Fact]
    public void Generate_AppendsCuratedDiscoveryToIndexAndFullContext()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            WriteApiIndex(root, string.Empty, "Example.Product", 1);
            var discoveryPath = Path.Combine(root, "discovery.md");
            var extraPath = Path.Combine(root, "extra.md");
            File.WriteAllText(discoveryPath,
                """
                ## Decision guides

                - [Compare libraries](/comparisons/): Dated first-party evidence.
                - [License policy](/licensing/): License and public crawler policy.
                """);
            File.WriteAllText(extraPath, "## Full-only implementation notes");

            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                Name = "Example Product",
                PackageId = "Example.Product",
                DiscoveryContentPath = discoveryPath,
                ExtraContentPath = extraPath
            });

            var llmsTxt = File.ReadAllText(result.LlmsTxtPath);
            var llmsFull = File.ReadAllText(result.LlmsFullPath);
            Assert.Contains("[Compare libraries](/comparisons/)", llmsTxt, StringComparison.Ordinal);
            Assert.Contains("[License policy](/licensing/)", llmsTxt, StringComparison.Ordinal);
            Assert.DoesNotContain("Full-only implementation notes", llmsTxt, StringComparison.Ordinal);
            Assert.True(
                llmsTxt.IndexOf("[License policy](/licensing/)", StringComparison.Ordinal) <
                llmsTxt.IndexOf("Slug rule:", StringComparison.Ordinal));
            Assert.Contains("[Compare libraries](/comparisons/)", llmsFull, StringComparison.Ordinal);
            Assert.Contains("Full-only implementation notes", llmsFull, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_SiteContentPublishesPresentDefaultAndExplicitApiCatalogs()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-site-api-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var defaultDirectory = Path.Combine(root, "api");
            Directory.CreateDirectory(defaultDirectory);
            var defaultIndex = Path.Combine(defaultDirectory, "index.json");
            File.WriteAllText(defaultIndex,
                """
                {
                  "title": "Example API Reference",
                  "typeCount": 3,
                  "types": []
                }
                """);

            var implicitResult = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                ContentKind = WebLlmsContentKind.Site,
                Name = "Example Portal"
            });

            Assert.Equal(1, implicitResult.ApiCatalogCount);
            Assert.Equal(3, implicitResult.ApiTypeCount);
            Assert.Contains("[API index](/api/index.json)", File.ReadAllText(implicitResult.LlmsTxtPath), StringComparison.Ordinal);
            Assert.Contains("\"apiTypeCount\": 3", File.ReadAllText(implicitResult.LlmsJsonPath), StringComparison.Ordinal);

            var explicitIndex = WriteApiIndex(root, "custom", "Example.Custom", 5);
            var explicitResult = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                ContentKind = WebLlmsContentKind.Site,
                Name = "Example Portal",
                ApiIndexPath = explicitIndex,
                ApiBase = "/reference"
            });

            Assert.Equal(1, explicitResult.ApiCatalogCount);
            Assert.Equal(5, explicitResult.ApiTypeCount);
            Assert.Contains("[API index](/reference/index.json)", File.ReadAllText(explicitResult.LlmsTxtPath), StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_SiteContentAggregatesExplicitApiCatalogs()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-site-multi-api-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var firstIndex = WriteApiIndex(root, "first", "Example.First", 2);
            var secondIndex = WriteApiIndex(root, "second", "Example.Second", 4);

            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                ContentKind = WebLlmsContentKind.Site,
                Name = "Example Portal",
                ApiIndexPaths = new[] { firstIndex, secondIndex }
            });

            Assert.Equal(2, result.ApiCatalogCount);
            Assert.Equal(6, result.ApiTypeCount);
            var llmsTxt = File.ReadAllText(result.LlmsTxtPath);
            var llmsJson = File.ReadAllText(result.LlmsJsonPath);
            Assert.Contains("[Example.First API index](/api/first/index.json)", llmsTxt, StringComparison.Ordinal);
            Assert.Contains("[Example.Second API search](/api/second/search.json)", llmsTxt, StringComparison.Ordinal);
            Assert.Contains("\"apiCatalogs\"", llmsJson, StringComparison.Ordinal);
            Assert.DoesNotContain("\"package\"", llmsJson, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_AggregatesMultipleApiCatalogsWithTheirPublishedRoutes()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-multi-api-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var wordIndex = WriteApiIndex(root, "word", "OfficeIMO.Word", 274);
            var excelIndex = WriteApiIndex(root, "excel", "OfficeIMO.Excel", 63);

            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                Name = "OfficeIMO",
                PackageId = "OfficeIMO.Word",
                ApiIndexPaths = new[] { wordIndex, excelIndex }
            });

            Assert.Equal(2, result.ApiCatalogCount);
            Assert.Equal(337, result.ApiTypeCount);

            var llmsTxt = File.ReadAllText(result.LlmsTxtPath);
            Assert.Contains("- API catalogs: 2", llmsTxt, StringComparison.Ordinal);
            Assert.Contains("[OfficeIMO.Word API index](/api/word/index.json)", llmsTxt, StringComparison.Ordinal);
            Assert.Contains("[OfficeIMO.Excel API search](/api/excel/search.json)", llmsTxt, StringComparison.Ordinal);
            Assert.DoesNotContain("[API index](/api/index.json)", llmsTxt, StringComparison.Ordinal);

            var llmsJson = File.ReadAllText(result.LlmsJsonPath);
            Assert.Contains("\"apiCatalogs\"", llmsJson, StringComparison.Ordinal);
            Assert.Contains("\"index\": \"/api/word/index.json\"", llmsJson, StringComparison.Ordinal);
            Assert.Contains("\"type\": \"/api/excel/types/{slug}.json\"", llmsJson, StringComparison.Ordinal);
            Assert.DoesNotContain("\"api\":", llmsJson, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_UsesProjectDescription_WhenOverviewIsNotProvided()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-project-description-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var projectPath = Path.Combine(root, "Example.csproj");
            File.WriteAllText(projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <AssemblyName>ExampleProduct</AssemblyName>
                    <PackageId>Example.Product</PackageId>
                    <Version>1.2.3</Version>
                    <Description>ExampleProduct helps teams publish internal documentation and automation portals.</Description>
                  </PropertyGroup>
                </Project>
                """);

            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                ProjectFile = projectPath
            });

            var llmsFull = File.ReadAllText(result.LlmsFullPath);
            Assert.Contains("ExampleProduct helps teams publish internal documentation and automation portals.", llmsFull, StringComparison.Ordinal);
            Assert.DoesNotContain("QR codes", llmsFull, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_UsesHomepageMetaDescription_WhenProjectDescriptionAndOverviewAreMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-homepage-description-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html lang="en">
                <head>
                  <meta name="description" content="Test product site for Active Directory security, posture, and reporting workflows." />
                  <title>Example Product</title>
                </head>
                <body>
                  <h1>Example Product</h1>
                </body>
                </html>
                """);

            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                Name = "Example Product",
                PackageId = "Example.Product"
            });

            var llmsFull = File.ReadAllText(result.LlmsFullPath);
            Assert.Contains("Test product site for Active Directory security, posture, and reporting workflows.", llmsFull, StringComparison.Ordinal);
            Assert.DoesNotContain("QR codes", llmsFull, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_UsesNeutralFallback_WhenNoOverviewSourcesExist()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-neutral-fallback-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                Name = "Example Product",
                PackageId = "Example.Product"
            });

            var llmsFull = File.ReadAllText(result.LlmsFullPath);
            Assert.Contains("Example Product documentation.", llmsFull, StringComparison.Ordinal);
            Assert.DoesNotContain("API reference", llmsFull, StringComparison.Ordinal);
            Assert.DoesNotContain("QR codes", llmsFull, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("barcodes", llmsFull, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_DescribesMultiPackageSuiteFromProjectAndModuleManifests()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-package-suite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var projectPath = Path.Combine(root, "OfficeIMO.Word.csproj");
            File.WriteAllText(projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <PackageId>OfficeIMO.Word</PackageId>
                    <VersionPrefix>3.0.3</VersionPrefix>
                  </PropertyGroup>
                </Project>
                """);
            var modulePath = Path.Combine(root, "PSWriteOffice.psd1");
            File.WriteAllText(modulePath,
                """
                @{
                    ModuleVersion = '3.0.1'
                    Description = 'PowerShell document automation powered by OfficeIMO.'
                }
                """);
            var quickstartPath = Path.Combine(root, "quickstart.cs");
            File.WriteAllText(quickstartPath,
                """
                using OfficeIMO.Word;

                using var document = WordDocument.Create("Example.docx");
                document.AddParagraph("Hello from OfficeIMO");
                document.Save();
                """);

            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                Name = "OfficeIMO library suite",
                ProjectFile = projectPath,
                PackageFiles = new[] { projectPath, modulePath },
                QuickstartPath = quickstartPath
            });

            Assert.Equal(2, result.PackageCount);
            Assert.Equal("varies by package", result.Version);

            var llmsTxt = File.ReadAllText(result.LlmsTxtPath);
            Assert.Contains("- Packages: 2", llmsTxt, StringComparison.Ordinal);
            Assert.Contains("`dotnet add package OfficeIMO.Word` — source version `3.0.3`", llmsTxt, StringComparison.Ordinal);
            Assert.Contains("`Install-Module PSWriteOffice` — source version `3.0.1`", llmsTxt, StringComparison.Ordinal);
            Assert.Contains("using OfficeIMO.Word;", llmsTxt, StringComparison.Ordinal);
            Assert.DoesNotContain("Version: unknown", llmsTxt, StringComparison.Ordinal);
            Assert.DoesNotContain("dotnet add package OfficeIMO library suite", llmsTxt, StringComparison.Ordinal);
            Assert.DoesNotContain("TODO", llmsTxt, StringComparison.Ordinal);

            var llmsJson = File.ReadAllText(result.LlmsJsonPath);
            Assert.Contains("\"packages\"", llmsJson, StringComparison.Ordinal);
            Assert.Contains("\"id\": \"OfficeIMO.Word\"", llmsJson, StringComparison.Ordinal);
            Assert.Contains("\"install\": \"Install-Module PSWriteOffice\"", llmsJson, StringComparison.Ordinal);
            Assert.DoesNotContain("\"package\": \"OfficeIMO library suite\"", llmsJson, StringComparison.Ordinal);

            var overridden = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                Name = "OfficeIMO library suite",
                ProjectFile = projectPath,
                PackageFiles = new[] { projectPath, modulePath },
                Version = "2026.7.0"
            });
            Assert.Equal("2026.7.0", overridden.Version);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_RejectsMissingConfiguredPackageManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-missing-package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var missingPath = Path.Combine(root, "Missing.Package.csproj");
            var error = Assert.Throws<FileNotFoundException>(() => WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                Name = "Example Suite",
                PackageFiles = new[] { missingPath }
            }));

            Assert.Equal(missingPath, error.FileName);
            Assert.Contains("Configured package manifest not found", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_PreservesPowerShellSurfaceForLegacyProjectFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-powershell-project-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var modulePath = Path.Combine(root, "ExampleModule.psd1");
            File.WriteAllText(modulePath,
                """
                @{
                    ModuleVersion = '2.4.1'
                    Description = 'Example PowerShell automation module.'
                }
                """);

            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                ProjectFile = modulePath
            });

            Assert.Equal(1, result.PackageCount);
            Assert.Equal("ExampleModule", result.PackageId);
            Assert.Equal("2.4.1", result.Version);

            var llmsTxt = File.ReadAllText(result.LlmsTxtPath);
            Assert.Contains("`Install-Module ExampleModule`", llmsTxt, StringComparison.Ordinal);
            Assert.Contains("```powershell", llmsTxt, StringComparison.Ordinal);
            Assert.Contains("Import-Module ExampleModule", llmsTxt, StringComparison.Ordinal);
            Assert.DoesNotContain("dotnet add package", llmsTxt, StringComparison.Ordinal);
            Assert.DoesNotContain("```csharp", llmsTxt, StringComparison.Ordinal);

            var llmsJson = File.ReadAllText(result.LlmsJsonPath);
            Assert.Contains("\"quickstartLanguage\": \"powershell\"", llmsJson, StringComparison.Ordinal);
            Assert.Contains("\"Install-Module ExampleModule\"", llmsJson, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_ReportsUnknownSuiteVersionWhenAnyManifestHasNoVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-unknown-suite-version-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var versionedPath = Path.Combine(root, "Versioned.csproj");
            File.WriteAllText(versionedPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <PackageId>Example.Versioned</PackageId>
                    <Version>1.2.3</Version>
                  </PropertyGroup>
                </Project>
                """);
            var unversionedPath = Path.Combine(root, "Unversioned.csproj");
            File.WriteAllText(unversionedPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <PackageId>Example.Unversioned</PackageId>
                  </PropertyGroup>
                </Project>
                """);

            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                PackageFiles = new[] { versionedPath, unversionedPath }
            });

            Assert.Equal(2, result.PackageCount);
            Assert.Equal("unknown", result.Version);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_UsesDotNetToolInstallationForToolManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-dotnet-tool-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var toolPath = Path.Combine(root, "Example.Tool.csproj");
            File.WriteAllText(toolPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <PackageId>Example.Tool</PackageId>
                    <Version>1.2.3</Version>
                    <PackAsTool>true</PackAsTool>
                    <ToolCommandName>example-tool</ToolCommandName>
                  </PropertyGroup>
                </Project>
                """);

            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                PackageFiles = new[] { toolPath }
            });

            var llmsTxt = File.ReadAllText(result.LlmsTxtPath);
            Assert.Contains("`dotnet tool install --global Example.Tool`", llmsTxt, StringComparison.Ordinal);
            Assert.DoesNotContain("dotnet add package Example.Tool", llmsTxt, StringComparison.Ordinal);
            Assert.Contains("```shell", llmsTxt, StringComparison.Ordinal);
            Assert.Contains("example-tool --help", llmsTxt, StringComparison.Ordinal);
            Assert.DoesNotContain("using Example.Tool", llmsTxt, StringComparison.Ordinal);

            var llmsJson = File.ReadAllText(result.LlmsJsonPath);
            Assert.Contains("\"quickstartLanguage\": \"shell\"", llmsJson, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_UsesEffectiveNuGetPackageVersions()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-package-version-precedence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var packageVersionPath = Path.Combine(root, "PackageVersion.csproj");
            File.WriteAllText(packageVersionPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <PackageId>Example.PackageVersion</PackageId>
                    <PackageVersion>4.2.0</PackageVersion>
                    <Version>1.0.0</Version>
                    <VersionPrefix>2.0.0</VersionPrefix>
                    <VersionSuffix>preview.1</VersionSuffix>
                  </PropertyGroup>
                </Project>
                """);
            var prefixedPath = Path.Combine(root, "Prefixed.csproj");
            File.WriteAllText(prefixedPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <PackageId>Example.Prefixed</PackageId>
                    <VersionPrefix>2.0.0</VersionPrefix>
                    <VersionSuffix>rc.1</VersionSuffix>
                  </PropertyGroup>
                </Project>
                """);

            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                PackageFiles = new[] { packageVersionPath, prefixedPath }
            });

            Assert.Equal("varies by package", result.Version);
            var llmsJson = File.ReadAllText(result.LlmsJsonPath);
            Assert.Contains("\"version\": \"4.2.0\"", llmsJson, StringComparison.Ordinal);
            Assert.Contains("\"version\": \"2.0.0-rc.1\"", llmsJson, StringComparison.Ordinal);
            Assert.DoesNotContain("\"version\": \"1.0.0\"", llmsJson, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_UsesPowerShellQuickstartForPackageManifest()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-powershell-package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var modulePath = Path.Combine(root, "ExampleModule.psd1");
            File.WriteAllText(modulePath,
                """
                @{
                    ModuleVersion = '2.4.1'
                    Description = 'Example PowerShell automation module.'
                }
                """);

            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                PackageFiles = new[] { modulePath }
            });

            var llmsTxt = File.ReadAllText(result.LlmsTxtPath);
            Assert.Contains("```powershell", llmsTxt, StringComparison.Ordinal);
            Assert.Contains("Import-Module ExampleModule", llmsTxt, StringComparison.Ordinal);
            Assert.DoesNotContain("```csharp", llmsTxt, StringComparison.Ordinal);
            Assert.DoesNotContain("using ExampleModule", llmsTxt, StringComparison.Ordinal);

            var llmsJson = File.ReadAllText(result.LlmsJsonPath);
            Assert.Contains("\"quickstartLanguage\": \"powershell\"", llmsJson, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_IncludesPowerShellPrereleaseLabelInEffectiveVersion()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-powershell-prerelease-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var modulePath = Path.Combine(root, "ExampleModule.psd1");
            File.WriteAllText(modulePath,
                """
                @{
                    ModuleVersion = '2.4.1'
                    Description = 'Example PowerShell automation module.'
                    PrivateData = @{
                        PSData = @{
                            Prerelease = 'beta1'
                        }
                    }
                }
                """);

            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                PackageFiles = new[] { modulePath }
            });

            Assert.Equal("2.4.1-beta1", result.Version);
            var llmsTxt = File.ReadAllText(result.LlmsTxtPath);
            var llmsJson = File.ReadAllText(result.LlmsJsonPath);
            Assert.Contains("source version `2.4.1-beta1`", llmsTxt, StringComparison.Ordinal);
            Assert.Contains("\"version\": \"2.4.1-beta1\"", llmsJson, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_ReadsInlinePowerShellManifestMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-powershell-inline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var modulePath = Path.Combine(root, "InlineModule.psd1");
            File.WriteAllText(
                modulePath,
                """
                # Example only: @{ ModuleVersion = '0.1.0'; Description = 'Commented metadata.'; Prerelease = 'ignored' }
                <# Another example:
                @{ ModuleVersion = '0.2.0'; Description = 'Blocked metadata.' }
                #>
                @{ ModuleVersion = '1.7.0'; Description = 'Inline # PowerShell module metadata.'; PrivateData = @{ PSData = @{ Prerelease = 'preview2' } } }
                """);

            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                ProjectFile = modulePath,
                PackageFiles = new[] { modulePath }
            });

            Assert.Equal("1.7.0-preview2", result.Version);
            var llmsTxt = File.ReadAllText(result.LlmsTxtPath);
            Assert.Contains("Inline # PowerShell module metadata.", llmsTxt, StringComparison.Ordinal);
            Assert.Contains("source version `1.7.0-preview2`", llmsTxt, StringComparison.Ordinal);
            Assert.DoesNotContain("Commented metadata", llmsTxt, StringComparison.Ordinal);
            Assert.DoesNotContain("Blocked metadata", llmsTxt, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_RejectsUnresolvedMsBuildPackageMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-msbuild-properties-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var projectPath = Path.Combine(root, "Example.Indirect.csproj");
            File.WriteAllText(projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <PackageId>$(PackageName)</PackageId>
                    <Version>$(VersionPrefix)</Version>
                    <Description>$(PackageDescription)</Description>
                  </PropertyGroup>
                </Project>
                """);

            var exception = Assert.Throws<InvalidDataException>(() => WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                PackageFiles = new[] { projectPath }
            }));
            Assert.Contains("Cannot resolve MSBuild property", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_ResolvesPackageIdentityFromDirectoryBuildProps()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-central-props-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "Directory.Build.props"),
                "<Project><PropertyGroup><SharedPackageId>Example.Central</SharedPackageId><PackageId>$(SharedPackageId)</PackageId></PropertyGroup></Project>");
            var projectPath = Path.Combine(root, "DifferentFileName.csproj");
            File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><Version>1.0.0</Version></PropertyGroup></Project>");

            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                PackageFiles = new[] { projectPath }
            });

            Assert.Contains("dotnet add package Example.Central", File.ReadAllText(result.LlmsTxtPath), StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_SiteIgnoresUnresolvedPackageOnlyProjectMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-site-project-metadata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "<html><head><title>Example Portal</title></head><body></body></html>");
            var projectPath = Path.Combine(root, "Internal.Project.csproj");
            File.WriteAllText(projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <Version>$(GitVersion)</Version>
                    <PackageId>$(PublishedPackageId)</PackageId>
                    <Description>Example portal documentation.</Description>
                  </PropertyGroup>
                </Project>
                """);

            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                ContentKind = WebLlmsContentKind.Site,
                ProjectFile = projectPath
            });

            Assert.Equal("Example Portal", result.Name);
            var llmsTxt = File.ReadAllText(result.LlmsTxtPath);
            Assert.Contains("Example portal documentation.", llmsTxt, StringComparison.Ordinal);
            Assert.DoesNotContain("GitVersion", llmsTxt, StringComparison.Ordinal);
            Assert.DoesNotContain("PublishedPackageId", llmsTxt, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_RejectsPackageMetadataInsideChooseBranches()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-conditional-package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var projectPath = Path.Combine(root, "Conditional.csproj");
            File.WriteAllText(projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Choose>
                    <When Condition="'$(Configuration)' == 'Release'">
                      <PropertyGroup><PackageId>Example.Release</PackageId></PropertyGroup>
                    </When>
                    <Otherwise>
                      <PropertyGroup><PackageId>Example.Debug</PackageId></PropertyGroup>
                    </Otherwise>
                  </Choose>
                </Project>
                """);

            var exception = Assert.Throws<InvalidDataException>(() => WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                PackageFiles = new[] { projectPath }
            }));
            Assert.Contains("Cannot resolve MSBuild property 'PackageId'", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_ExpandsMsBuildPropertiesAtAssignmentTime()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-assignment-order-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "Directory.Build.props"),
                "<Project><PropertyGroup><BaseName>Example.Initial</BaseName><PackageId>$(BaseName)</PackageId></PropertyGroup></Project>");
            var projectPath = Path.Combine(root, "Example.csproj");
            File.WriteAllText(projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><BaseName>Example.Later</BaseName><Version>1.0.0</Version></PropertyGroup></Project>");

            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                PackageFiles = new[] { projectPath }
            });

            var llmsTxt = File.ReadAllText(result.LlmsTxtPath);
            Assert.Contains("dotnet add package Example.Initial", llmsTxt, StringComparison.Ordinal);
            Assert.DoesNotContain("Example.Later", llmsTxt, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_UsesPackageMetadataAssignedInDirectoryBuildTargets()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-late-package-metadata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "Directory.Build.targets"),
                "<Project><PropertyGroup><PackageId>Example.Late</PackageId></PropertyGroup></Project>");
            var projectPath = Path.Combine(root, "Example.csproj");
            File.WriteAllText(projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><PackageId>Example.Early</PackageId></PropertyGroup></Project>");

            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                PackageFiles = new[] { projectPath }
            });
            Assert.Contains("dotnet add package Example.Late", File.ReadAllText(result.LlmsTxtPath), StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_FollowsImportedPostProjectPackageMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-imported-targets-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "Package.targets"),
                "<Project><PropertyGroup><PackageId>Example.Imported</PackageId></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(root, "Directory.Build.targets"),
                "<Project><Import Project=\"Package.targets\" /></Project>");
            var projectPath = Path.Combine(root, "Example.csproj");
            File.WriteAllText(projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><PackageId>Example.Early</PackageId></PropertyGroup></Project>");

            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                PackageFiles = new[] { projectPath }
            });
            Assert.Contains("dotnet add package Example.Imported", File.ReadAllText(result.LlmsTxtPath), StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_AllowsUnrelatedImportBeforeFinalPackageMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-unrelated-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "Unrelated.props"),
                "<Project><PropertyGroup><NoWarn>CS1591</NoWarn></PropertyGroup></Project>");
            var projectPath = Path.Combine(root, "Example.csproj");
            File.WriteAllText(projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="Unrelated.props" />
                  <PropertyGroup><PackageId>Example.Final</PackageId><Version>1.2.3</Version></PropertyGroup>
                </Project>
                """);

            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                PackageFiles = new[] { projectPath }
            });
            var llmsTxt = File.ReadAllText(result.LlmsTxtPath);
            Assert.Contains("dotnet add package Example.Final", llmsTxt, StringComparison.Ordinal);
            Assert.Contains("source version `1.2.3`", llmsTxt, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_AllowsConditionalImportThatCannotAlterPackageMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-unrelated-conditional-import-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "Warnings.props"),
                "<Project><PropertyGroup><NoWarn>CS1591</NoWarn></PropertyGroup></Project>");
            var projectPath = Path.Combine(root, "Example.csproj");
            File.WriteAllText(projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><PackageId>Example.Package</PackageId><Version>1.0.0</Version></PropertyGroup>
                  <Import Project="Warnings.props" Condition="'$(IncludeWarnings)' == 'true'" />
                </Project>
                """);

            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                PackageFiles = new[] { projectPath }
            });
            Assert.Contains("dotnet add package Example.Package", File.ReadAllText(result.LlmsTxtPath), StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_IgnoresUnresolvedOptionalDescriptionAndUnusedVersionFallbacks()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-optional-metadata-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var projectPath = Path.Combine(root, "Example.csproj");
            File.WriteAllText(projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <PackageId>Example.Package</PackageId>
                    <Version>2.3.4</Version>
                    <VersionPrefix Condition="'$(Configuration)' == 'Release'">9.9.9</VersionPrefix>
                    <Description>$(GeneratedDescription)</Description>
                  </PropertyGroup>
                </Project>
                """);

            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                PackageFiles = new[] { projectPath }
            });
            var llmsTxt = File.ReadAllText(result.LlmsTxtPath);
            Assert.Contains("dotnet add package Example.Package", llmsTxt, StringComparison.Ordinal);
            Assert.Contains("source version `2.3.4`", llmsTxt, StringComparison.Ordinal);
            Assert.DoesNotContain("GeneratedDescription", llmsTxt, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_TreatsBlankConfiguredSiteNameAsAbsent()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-blank-site-name-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "<html><head><title>Example Portal</title></head><body></body></html>");
            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                ContentKind = WebLlmsContentKind.Site,
                Name = "   "
            });
            Assert.Equal("Example Portal", result.Name);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_RejectsMissingConfiguredProjectFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-missing-project-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var missing = Path.Combine(root, "Missing.csproj");
            var exception = Assert.Throws<FileNotFoundException>(() => WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                Name = "Example Product",
                PackageId = "Example.Product",
                ProjectFile = missing
            }));
            Assert.Equal(missing, exception.FileName);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_RecordsExplicitVersionForPackageSuite()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-explicit-suite-version-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var projectPath = Path.Combine(root, "Example.Package.csproj");
            File.WriteAllText(projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <PackageId>Example.Package</PackageId>
                    <Version>1.0.0</Version>
                  </PropertyGroup>
                </Project>
                """);

            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                Name = "Example Suite",
                Version = "2026.7.0",
                PackageFiles = new[] { projectPath }
            });

            Assert.Equal("2026.7.0", result.Version);
            Assert.Contains("- Suite version: 2026.7.0", File.ReadAllText(result.LlmsTxtPath), StringComparison.Ordinal);
            Assert.Contains("\"version\": \"2026.7.0\"", File.ReadAllText(result.LlmsJsonPath), StringComparison.Ordinal);
            Assert.Contains("- Suite version: 2026.7.0", File.ReadAllText(result.LlmsFullPath), StringComparison.Ordinal);
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

    private static string WriteApiIndex(string root, string slug, string assemblyName, int typeCount)
    {
        var directory = Path.Combine(root, "api", slug);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "index.json");
        File.WriteAllText(path,
            $$"""
            {
              "title": "{{assemblyName}} API Reference",
              "assembly": {
                "assemblyName": "{{assemblyName}}",
                "assemblyVersion": "1.0.0.0"
              },
              "typeCount": {{typeCount}},
              "types": []
            }
            """);
        return path;
    }
}
