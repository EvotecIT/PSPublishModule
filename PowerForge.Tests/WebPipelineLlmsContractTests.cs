using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebPipelineLlmsContractTests
{
    [Fact]
    public void Llms_pipeline_site_content_omits_package_installation_metadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-pipeline-site-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "llms",
                      "siteRoot": "_site",
                      "contentKind": "Site",
                      "name": "Example Portal",
                      "overview": "A documentation and product portal."
                    }
                  ]
                }
                """);

            Directory.CreateDirectory(Path.Combine(root, "_site"));
            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.StartsWith("LLMS generated (site)", Assert.Single(result.Steps).Message, StringComparison.Ordinal);
            var llmsTxt = File.ReadAllText(Path.Combine(root, "_site", "llms.txt"));
            Assert.DoesNotContain("## Install", llmsTxt, StringComparison.Ordinal);
            Assert.DoesNotContain("dotnet add package", llmsTxt, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Llms_pipeline_rejects_unknown_content_kind()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-pipeline-kind-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "llms",
                      "siteRoot": "_site",
                      "contentKind": "PortalTypo",
                      "name": "Example Portal"
                    }
                  ]
                }
                """);

            Directory.CreateDirectory(Path.Combine(root, "_site"));
            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.False(result.Success);
            Assert.Contains("Unsupported LLMS content kind", Assert.Single(result.Steps).Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Llms_pipeline_rejects_unknown_api_detail_level()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-pipeline-api-level-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "llms",
                      "siteRoot": "_site",
                      "contentKind": "Site",
                      "name": "Example Portal",
                      "apiLevel": "Everything"
                    }
                  ]
                }
                """);

            Directory.CreateDirectory(Path.Combine(root, "_site"));
            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.False(result.Success);
            Assert.Contains("Expected None, Summary, or Full", Assert.Single(result.Steps).Message, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void LlmsStep_schema_declares_package_manifest_arrays()
    {
        var schemaDocument = JsonNode.Parse(File.ReadAllText(GetRepoPath(
            "Schemas",
            "powerforge.web.pipelinespec.schema.json")))!;
        var properties = schemaDocument["$defs"]!["LlmsStep"]!["properties"]!;

        foreach (var propertyName in new[] { "packageFiles", "package-files" })
        {
            Assert.Equal("array", properties[propertyName]!["type"]!.GetValue<string>());
            Assert.Equal("string", properties[propertyName]!["items"]!["type"]!.GetValue<string>());
        }
    }

    [Fact]
    public void LlmsStep_schema_declares_site_content_kind()
    {
        var schemaDocument = JsonNode.Parse(File.ReadAllText(GetRepoPath(
            "Schemas",
            "powerforge.web.pipelinespec.schema.json")))!;
        var properties = schemaDocument["$defs"]!["LlmsStep"]!["properties"]!;

        foreach (var propertyName in new[] { "contentKind", "content-kind" })
        {
            var values = properties[propertyName]!["enum"]!.AsArray()
                .Select(static value => value!.GetValue<string>())
                .ToArray();
            Assert.Equal(new[] { "Package", "Site" }, values);
        }
    }

    [Fact]
    public void LlmsStep_schema_declares_supported_path_aliases_and_api_index_arrays()
    {
        var schemaDocument = JsonNode.Parse(File.ReadAllText(GetRepoPath(
            "Schemas",
            "powerforge.web.pipelinespec.schema.json")))!;
        var properties = schemaDocument["$defs"]!["LlmsStep"]!["properties"]!;

        foreach (var propertyName in new[] { "siteRoot", "site-root", "apiIndex", "api-index", "apiBase", "api-base", "apiLevel", "api-level" })
            Assert.NotNull(properties[propertyName]);
        foreach (var propertyName in new[] { "apiIndexes", "api-indexes" })
        {
            Assert.Equal("array", properties[propertyName]!["type"]!.GetValue<string>());
            Assert.Equal("string", properties[propertyName]!["items"]!["type"]!.GetValue<string>());
        }
    }

    [Theory]
    [InlineData("packageFiles")]
    [InlineData("package-files")]
    public void Llms_fingerprint_changes_when_package_manifest_changes(string propertyName)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-fingerprint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var manifestPath = Path.Combine(root, "Module.psd1");
            File.WriteAllText(manifestPath, "@{ ModuleVersion = '1.0.0' }");
            using var document = JsonDocument.Parse(
                $$"""
                {
                  "task": "llms",
                  "siteRoot": "_site",
                  "{{propertyName}}": ["Module.psd1"]
                }
                """);
            var method = typeof(WebPipelineRunner).GetMethod(
                "ComputeStepFingerprint",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var first = (string)method!.Invoke(null, new object?[] { root, document.RootElement, null })!;
            File.WriteAllText(manifestPath, "@{ ModuleVersion = '1.0.0'; Description = 'Changed' }");
            var second = (string)method.Invoke(null, new object?[] { root, document.RootElement, null })!;

            Assert.NotEqual(first, second);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("apiIndex")]
    [InlineData("api-index")]
    [InlineData("apiIndexes")]
    [InlineData("api-indexes")]
    public void Llms_fingerprint_changes_when_api_index_changes(string propertyName)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-api-fingerprint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var indexPath = Path.Combine(root, "api", "index.json");
            Directory.CreateDirectory(Path.GetDirectoryName(indexPath)!);
            File.WriteAllText(indexPath, "{\"typeCount\":1}");
            var value = propertyName.EndsWith("es", StringComparison.Ordinal) ? "[\"api/index.json\"]" : "\"api/index.json\"";
            using var document = JsonDocument.Parse(
                $$"""
                {
                  "task": "llms",
                  "siteRoot": "_site",
                  "{{propertyName}}": {{value}}
                }
                """);
            var method = typeof(WebPipelineRunner).GetMethod(
                "ComputeStepFingerprint",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var first = (string)method!.Invoke(null, new object?[] { root, document.RootElement, null })!;
            File.WriteAllText(indexPath, "{\"typeCount\":22}");
            var second = (string)method.Invoke(null, new object?[] { root, document.RootElement, null })!;
            Assert.NotEqual(first, second);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData("Directory.Build.props")]
    [InlineData("Directory.Build.targets")]
    public void Llms_fingerprint_changes_when_implicit_msbuild_metadata_changes(string metadataFileName)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-msbuild-fingerprint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var projectPath = Path.Combine(root, "Example.csproj");
            File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            var metadataPath = Path.Combine(root, metadataFileName);
            File.WriteAllText(metadataPath, "<Project><PropertyGroup><PackageId>Example.One</PackageId></PropertyGroup></Project>");
            using var document = JsonDocument.Parse(
                """
                {
                  "task": "llms",
                  "siteRoot": "_site",
                  "packageFiles": ["Example.csproj"]
                }
                """);
            var method = typeof(WebPipelineRunner).GetMethod(
                "ComputeStepFingerprint",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var first = (string)method!.Invoke(null, new object?[] { root, document.RootElement, null })!;
            File.WriteAllText(metadataPath, "<Project><PropertyGroup><PackageId>Example.Two.Longer</PackageId></PropertyGroup></Project>");
            var second = (string)method.Invoke(null, new object?[] { root, document.RootElement, null })!;
            Assert.NotEqual(first, second);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Llms_fingerprint_changes_when_imported_msbuild_metadata_changes()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-import-fingerprint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "Example.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            File.WriteAllText(Path.Combine(root, "Directory.Build.targets"), "<Project><Import Project=\"Package.targets\" /></Project>");
            var importedPath = Path.Combine(root, "Package.targets");
            File.WriteAllText(importedPath, "<Project><PropertyGroup><PackageId>Example.One</PackageId></PropertyGroup></Project>");
            using var document = JsonDocument.Parse(
                """
                {
                  "task": "llms",
                  "siteRoot": "_site",
                  "packageFiles": ["Example.csproj"]
                }
                """);
            var method = typeof(WebPipelineRunner).GetMethod(
                "ComputeStepFingerprint",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var first = (string)method!.Invoke(null, new object?[] { root, document.RootElement, null })!;
            File.WriteAllText(importedPath, "<Project><PropertyGroup><PackageId>Example.Two.Longer</PackageId></PropertyGroup></Project>");
            var second = (string)method.Invoke(null, new object?[] { root, document.RootElement, null })!;
            Assert.NotEqual(first, second);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Theory]
    [InlineData(".fsproj")]
    [InlineData(".vbproj")]
    public void Llms_fingerprint_tracks_implicit_metadata_for_all_msbuild_project_types(string extension)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-project-type-fingerprint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var projectName = "Example" + extension;
            File.WriteAllText(Path.Combine(root, projectName), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            var metadataPath = Path.Combine(root, "Directory.Build.props");
            File.WriteAllText(metadataPath, "<Project><PropertyGroup><PackageId>Example.One</PackageId></PropertyGroup></Project>");
            using var document = JsonDocument.Parse(
                $$"""
                {
                  "task": "llms",
                  "siteRoot": "_site",
                  "packageFiles": ["{{projectName}}"]
                }
                """);
            var method = typeof(WebPipelineRunner).GetMethod(
                "ComputeStepFingerprint",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var first = (string)method!.Invoke(null, new object?[] { root, document.RootElement, null })!;
            File.WriteAllText(metadataPath, "<Project><PropertyGroup><PackageId>Example.Two.Longer</PackageId></PropertyGroup></Project>");
            var second = (string)method.Invoke(null, new object?[] { root, document.RootElement, null })!;

            Assert.NotEqual(first, second);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Llms_site_fingerprint_ignores_package_only_project_inputs()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-site-package-fingerprint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var obsoleteProject = Path.Combine(root, "Obsolete.csproj");
            File.WriteAllText(obsoleteProject, "<not-valid-msbuild");
            using var document = JsonDocument.Parse(
                """
                {
                  "task": "llms",
                  "siteRoot": "_site",
                  "contentKind": "Site",
                  "name": "Example Portal",
                  "packageFiles": ["Obsolete.csproj"]
                }
                """);
            var method = typeof(WebPipelineRunner).GetMethod(
                "ComputeStepFingerprint",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var first = (string)method!.Invoke(null, new object?[] { root, document.RootElement, null })!;
            File.WriteAllText(obsoleteProject, "still-not-valid-msbuild");
            var second = (string)method.Invoke(null, new object?[] { root, document.RootElement, null })!;

            Assert.Equal(first, second);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Llms_fingerprint_changes_when_optional_import_appears()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-llms-optional-import-fingerprint-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(
                Path.Combine(root, "Example.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><Import Project=\"Optional.targets\" Condition=\"Exists('Optional.targets')\" /></Project>");
            using var document = JsonDocument.Parse(
                """
                {
                  "task": "llms",
                  "siteRoot": "_site",
                  "packageFiles": ["Example.csproj"]
                }
                """);
            var method = typeof(WebPipelineRunner).GetMethod(
                "ComputeStepFingerprint",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.NotNull(method);

            var first = (string)method!.Invoke(null, new object?[] { root, document.RootElement, null })!;
            File.WriteAllText(
                Path.Combine(root, "Optional.targets"),
                "<Project><PropertyGroup><PackageId>Example.Optional</PackageId></PropertyGroup></Project>");
            var second = (string)method.Invoke(null, new object?[] { root, document.RootElement, null })!;

            Assert.NotEqual(first, second);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static string GetRepoPath(params string[] relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && current is not null; i++)
        {
            if (File.Exists(Path.Combine(current.FullName, "PowerForge", "PowerForge.csproj")))
                return Path.Combine([current.FullName, .. relativePath]);
            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Unable to locate repository root.");
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort test cleanup.
        }
    }
}
