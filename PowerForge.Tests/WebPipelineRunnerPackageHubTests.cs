using System;
using System.IO;
using System.Text.Json;
using PowerForge.Web.Cli;
using Xunit;

public class WebPipelineRunnerPackageHubTests
{
    [Fact]
    public void RunPipeline_PackageHub_GeneratesLibraryAndModuleMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-package-hub-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var srcDir = Path.Combine(root, "src");
            Directory.CreateDirectory(srcDir);
            var projectPath = Path.Combine(srcDir, "Contoso.Core.csproj");
            File.WriteAllText(projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
                    <PackageId>Contoso.Core</PackageId>
                    <Version>1.2.3</Version>
                    <Description>Core library</Description>
                    <RepositoryUrl>https://github.com/contoso/core</RepositoryUrl>
                  </PropertyGroup>
                  <ItemGroup>
                    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
                  </ItemGroup>
                </Project>
                """);

            var moduleDir = Path.Combine(root, "module");
            Directory.CreateDirectory(moduleDir);
            var manifestPath = Path.Combine(moduleDir, "Contoso.Tools.psd1");
            File.WriteAllText(manifestPath,
                """
                @{
                  RootModule = 'Contoso.Tools.psm1'
                  ModuleVersion = '2.1.0'
                  Author = 'Contoso'
                  Description = 'Tooling module'
                  PowerShellVersion = '7.2'
                  CompatiblePSEditions = @('Desktop','Core')
                  FunctionsToExport = @('Get-ContosoTool')
                  CmdletsToExport = @('Set-ContosoTool')
                  RequiredModules = @(
                    @{ ModuleName = 'Pester'; RequiredVersion = '5.5.0' }
                    'PSFramework'
                  )
                }
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "package-hub",
                      "title": "Contoso Hub",
                      "projectFiles": [ "./src/Contoso.Core.csproj" ],
                      "moduleFiles": [ "./module/Contoso.Tools.psd1" ],
                      "out": "./data/package-hub.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("Package hub 1 libraries, 1 modules", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            var outputPath = Path.Combine(root, "data", "package-hub.json");
            Assert.True(File.Exists(outputPath));

            using var doc = JsonDocument.Parse(File.ReadAllText(outputPath));
            var rootJson = doc.RootElement;
            Assert.Equal("Contoso Hub", rootJson.GetProperty("title").GetString());

            var libraries = rootJson.GetProperty("libraries");
            Assert.Equal(1, libraries.GetArrayLength());
            var firstLibrary = libraries[0];
            Assert.Equal("Contoso.Core", firstLibrary.GetProperty("packageId").GetString());
            Assert.Equal("1.2.3", firstLibrary.GetProperty("version").GetString());

            var modules = rootJson.GetProperty("modules");
            Assert.Equal(1, modules.GetArrayLength());
            var firstModule = modules[0];
            Assert.Equal("2.1.0", firstModule.GetProperty("version").GetString());
            Assert.Equal("7.2", firstModule.GetProperty("powerShellVersion").GetString());
            Assert.Contains(firstModule.GetProperty("exportedCommands").EnumerateArray(), command =>
                string.Equals(command.GetString(), "Get-ContosoTool", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(firstModule.GetProperty("exportedCommands").EnumerateArray(), command =>
                string.Equals(command.GetString(), "Set-ContosoTool", StringComparison.OrdinalIgnoreCase));

            var requiredModules = firstModule.GetProperty("requiredModules");
            Assert.Equal(2, requiredModules.GetArrayLength());
            Assert.Contains(requiredModules.EnumerateArray(), dependency =>
                string.Equals(dependency.GetProperty("name").GetString(), "Pester", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(dependency.GetProperty("version").GetString(), "5.5.0", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(requiredModules.EnumerateArray(), dependency =>
                string.Equals(dependency.GetProperty("name").GetString(), "PSFramework", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_PackageHub_FailsWithoutInputs()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-package-hub-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "package-hub",
                      "out": "./data/package-hub.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("requires at least one project or module input", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
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
