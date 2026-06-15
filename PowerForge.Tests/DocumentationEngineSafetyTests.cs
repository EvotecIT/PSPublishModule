using System;
using System.IO;
using System.Runtime.Serialization.Json;
using Xunit;

namespace PowerForge.Tests;

public sealed class DocumentationEngineSafetyTests
{
    [Fact]
    public void Build_RefusesProjectRootDocsPathBeforeDeleting()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var projectRoot = Path.Combine(root.FullName, "Project");
            Directory.CreateDirectory(projectRoot);
            File.WriteAllText(Path.Combine(projectRoot, "Project.sln"), string.Empty);
            var sourceFile = Path.Combine(projectRoot, "Important.cs");
            File.WriteAllText(sourceFile, "do not delete");

            var stagingRoot = Path.Combine(root.FullName, "Staging");
            Directory.CreateDirectory(stagingRoot);
            var manifestPath = Path.Combine(stagingRoot, "TestModule.psd1");
            File.WriteAllText(manifestPath, "@{ ModuleVersion = '1.0.0'; RootModule = 'TestModule.psm1' }");

            var engine = new DocumentationEngine(new ModulePipelineMissingAnalysisServiceTests.ThrowingPowerShellRunner(), new NullLogger());
            var result = engine.Build(
                moduleName: "TestModule",
                stagingPath: stagingRoot,
                moduleManifestPath: manifestPath,
                documentation: new DocumentationConfiguration
                {
                    Path = projectRoot,
                    PathReadme = Path.Combine(projectRoot, "Readme.md")
                },
                buildDocumentation: new BuildDocumentationConfiguration
                {
                    Enable = true
                });

            Assert.False(result.Succeeded);
            Assert.Contains("project root", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(sourceFile));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Build_PreservesDocsAssetsAndPrunesGeneratedMarkdownAfterExtraction()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var stagingRoot = Path.Combine(root.FullName, "Staging");
            var docsPath = Path.Combine(stagingRoot, "Docs");
            var assetsPath = Path.Combine(docsPath, "assets");
            Directory.CreateDirectory(assetsPath);
            File.WriteAllText(Path.Combine(docsPath, "Old-Command.md"), GeneratedCommandMarkdown("Old-Command"));
            File.WriteAllText(Path.Combine(docsPath, "usage.md"), "# Usage guide");
            File.WriteAllText(Path.Combine(docsPath, "guide.md"), "Module Name: Contoso");
            File.WriteAllText(Path.Combine(assetsPath, "logo.png"), "asset");

            var manifestPath = Path.Combine(stagingRoot, "TestModule.psd1");
            Directory.CreateDirectory(stagingRoot);
            File.WriteAllText(manifestPath, "@{ ModuleVersion = '1.0.0'; RootModule = 'TestModule.psm1' }");

            var payload = new DocumentationExtractionPayload
            {
                ModuleName = "TestModule",
                Commands =
                [
                    new DocumentationCommandHelp
                    {
                        Name = "Get-Test",
                        Synopsis = "Gets a test item.",
                        Description = "Gets a test item."
                    }
                ]
            };

            var engine = new DocumentationEngine(new PayloadPowerShellRunner(payload), new NullLogger());
            var result = engine.Build(
                moduleName: "TestModule",
                stagingPath: stagingRoot,
                moduleManifestPath: manifestPath,
                documentation: new DocumentationConfiguration
                {
                    Path = "Docs",
                    PathReadme = Path.Combine("Docs", "Readme.md")
                },
                buildDocumentation: new BuildDocumentationConfiguration
                {
                    Enable = true
                });

            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.True(File.Exists(Path.Combine(docsPath, "Get-Test.md")));
            Assert.True(File.Exists(Path.Combine(docsPath, "Readme.md")));
            Assert.False(File.Exists(Path.Combine(docsPath, "Old-Command.md")));
            Assert.True(File.Exists(Path.Combine(docsPath, "usage.md")));
            Assert.True(File.Exists(Path.Combine(docsPath, "guide.md")));
            Assert.True(File.Exists(Path.Combine(assetsPath, "logo.png")));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Build_DoesNotCleanDocsWhenExtractionFails()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var stagingRoot = Path.Combine(root.FullName, "Staging");
            var docsPath = Path.Combine(stagingRoot, "Docs");
            Directory.CreateDirectory(docsPath);
            var oldFile = Path.Combine(docsPath, "Old-Command.md");
            File.WriteAllText(oldFile, "# Old");

            var manifestPath = Path.Combine(stagingRoot, "TestModule.psd1");
            File.WriteAllText(manifestPath, "@{ ModuleVersion = '1.0.0'; RootModule = 'TestModule.psm1' }");

            var engine = new DocumentationEngine(new ModulePipelineMissingAnalysisServiceTests.ThrowingPowerShellRunner(), new NullLogger());
            var result = engine.Build(
                moduleName: "TestModule",
                stagingPath: stagingRoot,
                moduleManifestPath: manifestPath,
                documentation: new DocumentationConfiguration
                {
                    Path = "Docs",
                    PathReadme = Path.Combine("Docs", "Readme.md")
                },
                buildDocumentation: new BuildDocumentationConfiguration
                {
                    Enable = true
                });

            Assert.False(result.Succeeded);
            Assert.True(File.Exists(oldFile));
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void ExtractHelpPayload_ReadsSimpleDictionaryParameterSetRequired()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var manifestPath = Path.Combine(root.FullName, "TestModule.psd1");
            File.WriteAllText(manifestPath, "@{ ModuleVersion = '1.0.0'; RootModule = 'TestModule.psm1' }");

            const string json = """
{
  "moduleName": "TestModule",
  "commands": [
    {
      "name": "New-Thing",
      "parameters": [
        {
          "name": "Path",
          "parameterSetRequired": {
            "__AllParameterSets": true,
            "OptionalSet": false
          }
        }
      ]
    }
  ]
}
""";

            var engine = new DocumentationEngine(new RawJsonPowerShellRunner(json), new NullLogger());

            var payload = engine.ExtractHelpPayload(root.FullName, manifestPath);

            var parameter = Assert.Single(Assert.Single(payload.Commands).Parameters);
            Assert.True(parameter.ParameterSetRequired["__AllParameterSets"]);
            Assert.False(parameter.ParameterSetRequired["OptionalSet"]);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    private sealed class PayloadPowerShellRunner : IPowerShellRunner
    {
        private readonly DocumentationExtractionPayload _payload;

        public PayloadPowerShellRunner(DocumentationExtractionPayload payload)
        {
            _payload = payload;
        }

        public PowerShellRunResult Run(PowerShellRunRequest request)
        {
            var jsonPath = request.Arguments[2];
            using var stream = File.Create(jsonPath);
            var serializer = new DataContractJsonSerializer(typeof(DocumentationExtractionPayload));
            serializer.WriteObject(stream, _payload);
            return new PowerShellRunResult(0, string.Empty, string.Empty, "pwsh");
        }
    }

    private sealed class RawJsonPowerShellRunner : IPowerShellRunner
    {
        private readonly string _json;

        public RawJsonPowerShellRunner(string json)
        {
            _json = json;
        }

        public PowerShellRunResult Run(PowerShellRunRequest request)
        {
            var jsonPath = request.Arguments[2];
            Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);
            File.WriteAllText(jsonPath, _json);
            return new PowerShellRunResult(0, string.Empty, string.Empty, "pwsh");
        }
    }

    private static string GeneratedCommandMarkdown(string commandName)
        => string.Join(Environment.NewLine, new[]
        {
            "---",
            "external help file: TestModule-help.xml",
            "Module Name: TestModule",
            "schema: 2.0.0",
            "---",
            $"# {commandName}"
        });
}
