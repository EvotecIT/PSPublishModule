using System;
using System.IO;
using PowerForge.Web;
using Xunit;

public class WebLlmsGeneratorTests
{
    [Fact]
    public void Generate_WritesRecommendedLlmsTxtMarkdownLinks()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-markdown-links-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
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
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_AppendsCuratedDiscoveryToIndexAndFullContext()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
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
            Assert.Contains("[Compare libraries](/comparisons/)", llmsFull, StringComparison.Ordinal);
            Assert.Contains("Full-only implementation notes", llmsFull, StringComparison.Ordinal);
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
            Assert.Contains("Example Product documentation site and API reference.", llmsFull, StringComparison.Ordinal);
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
    public void Generate_FallsBackFromUnresolvedMsBuildMetadata()
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

            var result = WebLlmsGenerator.Generate(new WebLlmsOptions
            {
                SiteRoot = root,
                PackageFiles = new[] { projectPath }
            });

            Assert.Equal("unknown", result.Version);
            var llmsTxt = File.ReadAllText(result.LlmsTxtPath);
            var llmsJson = File.ReadAllText(result.LlmsJsonPath);
            Assert.Contains("dotnet add package Example.Indirect", llmsTxt, StringComparison.Ordinal);
            Assert.DoesNotContain("$(", llmsTxt, StringComparison.Ordinal);
            Assert.DoesNotContain("$(", llmsJson, StringComparison.Ordinal);
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
