using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebPipelineLlmsContractTests
{
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
