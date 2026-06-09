using System.Text.Json;
using PowerForge.Web.Cli;

public class WebPipelineRunnerCompatMatrixTests
{
    [Fact]
    public void RunPipeline_CompatMatrix_WritesJsonAndMarkdown()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-compat-matrix-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "Sample.Library.csproj"),
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
                    <PackageId>Sample.Library</PackageId>
                    <Version>1.0.0</Version>
                  </PropertyGroup>
                </Project>
                """);

            File.WriteAllText(Path.Combine(root, "Sample.Module.psd1"),
                """
                @{
                    RootModule = 'Sample.Module.psm1'
                    ModuleVersion = '2.0.0'
                    PowerShellVersion = '7.4'
                    CompatiblePSEditions = @('Core')
                }
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "compat-matrix",
                      "title": "Compatibility",
                      "csprojFiles": [ "./Sample.Library.csproj" ],
                      "psd1Files": [ "./Sample.Module.psd1" ],
                      "entries": [
                        { "type": "nuget", "id": "Sample.Extensions", "version": "0.1.0-preview", "targetFrameworks": [ "net8.0" ] }
                      ],
                      "out": "./_temp/compat-matrix.json",
                      "markdownOut": "./_temp/compatibility.md"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("Compatibility matrix", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            var jsonPath = Path.Combine(root, "_temp", "compat-matrix.json");
            var markdownPath = Path.Combine(root, "_temp", "compatibility.md");
            Assert.True(File.Exists(jsonPath));
            Assert.True(File.Exists(markdownPath));

            using var json = JsonDocument.Parse(File.ReadAllText(jsonPath));
            var entries = json.RootElement.GetProperty("entries");
            Assert.Equal(3, entries.GetArrayLength());
            Assert.Contains(entries.EnumerateArray(), entry =>
                string.Equals(entry.GetProperty("id").GetString(), "Sample.Library", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(entries.EnumerateArray(), entry =>
                string.Equals(entry.GetProperty("id").GetString(), "Sample.Module", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_CompatMatrix_FailsOnWarningsWhenConfigured()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-compat-matrix-warn-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "compat-matrix",
                      "csprojFiles": [ "./missing.csproj" ],
                      "out": "./_temp/compat-matrix.json",
                      "failOnWarnings": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("not found", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
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
            // ignore cleanup failures in tests
        }
    }
}
