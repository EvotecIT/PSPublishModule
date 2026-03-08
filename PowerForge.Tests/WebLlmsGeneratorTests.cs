using System;
using System.IO;
using PowerForge.Web;
using Xunit;

public class WebLlmsGeneratorTests
{
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
