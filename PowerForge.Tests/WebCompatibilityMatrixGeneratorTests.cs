using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Tests;

public class WebCompatibilityMatrixGeneratorTests
{
    [Fact]
    public void Generate_MergesExplicitAndDiscoveredEntries_AndWritesMarkdown()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-compat-matrix-generator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var csprojPath = Path.Combine(root, "Sample.Library.csproj");
            File.WriteAllText(csprojPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
                    <PackageId>Sample.Library</PackageId>
                    <Version>2.1.0</Version>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
                  </ItemGroup>
                </Project>
                """);

            var psd1Path = Path.Combine(root, "Sample.Module.psd1");
            File.WriteAllText(psd1Path,
                """
                @{
                    RootModule = 'Sample.Module.psm1'
                    ModuleVersion = '1.3.0'
                    PowerShellVersion = '7.2'
                    CompatiblePSEditions = @('Core')
                    RequiredModules = @(
                        'Pester',
                        @{ ModuleName = 'PSReadLine'; ModuleVersion = '2.2.0' }
                    )
                }
                """);

            var outputPath = Path.Combine(root, "compat-matrix.json");
            var markdownPath = Path.Combine(root, "compatibility.md");
            var result = WebCompatibilityMatrixGenerator.Generate(new WebCompatibilityMatrixOptions
            {
                BaseDirectory = root,
                OutputPath = outputPath,
                MarkdownOutputPath = markdownPath,
                Title = "Matrix",
                Entries = new List<WebCompatibilityMatrixEntryInput>
                {
                    new()
                    {
                        Type = "nuget",
                        Id = "Sample.Extensions",
                        Version = "0.9.0-preview.1",
                        TargetFrameworks = new List<string> { "net8.0" },
                        Status = "preview"
                    }
                },
                CsprojFiles = new List<string> { csprojPath },
                Psd1Files = new List<string> { psd1Path }
            });

            Assert.Equal(3, result.EntryCount);
            Assert.Empty(result.Warnings);
            Assert.True(File.Exists(outputPath));
            Assert.True(File.Exists(markdownPath));

            using var json = JsonDocument.Parse(File.ReadAllText(outputPath));
            var entries = json.RootElement.GetProperty("entries");
            Assert.Equal(3, entries.GetArrayLength());

            var nuget = entries.EnumerateArray()
                .First(entry => string.Equals(entry.GetProperty("id").GetString(), "Sample.Library", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("nuget", nuget.GetProperty("type").GetString());
            Assert.Equal("2.1.0", nuget.GetProperty("version").GetString());
            Assert.Contains("net10.0", nuget.GetProperty("targetFrameworks").EnumerateArray().Select(static value => value.GetString()), StringComparer.OrdinalIgnoreCase);

            var module = entries.EnumerateArray()
                .First(entry => string.Equals(entry.GetProperty("id").GetString(), "Sample.Module", StringComparison.OrdinalIgnoreCase));
            Assert.Equal("powershell-module", module.GetProperty("type").GetString());
            Assert.Equal("1.3.0", module.GetProperty("version").GetString());
            Assert.Equal("7.2", module.GetProperty("powerShellVersion").GetString());
            Assert.Contains("Core", module.GetProperty("powerShellEditions").EnumerateArray().Select(static value => value.GetString()), StringComparer.OrdinalIgnoreCase);
            Assert.Contains("PSReadLine (2.2.0)", module.GetProperty("dependencies").EnumerateArray().Select(static value => value.GetString()), StringComparer.OrdinalIgnoreCase);

            var markdown = File.ReadAllText(markdownPath);
            Assert.Contains("| Type | Id | Version |", markdown, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Sample.Library", markdown, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Sample.Module", markdown, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Generate_WhenIncludeDependenciesIsFalse_OmitsDependencies()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-compat-matrix-no-deps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var csprojPath = Path.Combine(root, "Sample.Library.csproj");
            File.WriteAllText(csprojPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <PackageId>Sample.Library</PackageId>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
                  </ItemGroup>
                </Project>
                """);

            var outputPath = Path.Combine(root, "compat-matrix.json");
            var result = WebCompatibilityMatrixGenerator.Generate(new WebCompatibilityMatrixOptions
            {
                BaseDirectory = root,
                OutputPath = outputPath,
                IncludeDependencies = false,
                CsprojFiles = new List<string> { csprojPath }
            });

            Assert.Equal(1, result.EntryCount);
            using var json = JsonDocument.Parse(File.ReadAllText(outputPath));
            var entry = json.RootElement.GetProperty("entries")[0];
            Assert.Equal(0, entry.GetProperty("dependencies").GetArrayLength());
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
