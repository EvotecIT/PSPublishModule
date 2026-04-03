using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using PowerForge.Web.Cli;
using Xunit;

public class WebPipelineRunnerApiDocsPreflightTests
{
    [Fact]
    public void RunPipeline_ApiDocsPreflight_FailsOnSourceMappingsWithoutSourceConfig()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apidocs-preflight-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "test.xml");
            File.WriteAllText(xmlPath, BuildMinimalXml());
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apidocs",
                      "type": "CSharp",
                      "xml": "./test.xml",
                      "out": "./_site/api",
                      "format": "json",
                      "failOnWarnings": true,
                      "sourceUrlMappings": [
                        {
                          "pathPrefix": "PowerForge.Web",
                          "urlPattern": "https://example.invalid/blob/main/{path}#L{line}"
                        }
                      ]
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("[PFWEB.APIDOCS.SOURCE]", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("sourceUrlMappings are configured but both sourceRoot and sourceUrl are empty", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ApiDocsPreflight_AllowsSuppressedSourceWarnings()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apidocs-preflight-suppress-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "test.xml");
            File.WriteAllText(xmlPath, BuildMinimalXml());
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apidocs",
                      "type": "CSharp",
                      "xml": "./test.xml",
                      "out": "./_site/api",
                      "format": "json",
                      "failOnWarnings": true,
                      "suppressWarnings": [ "PFWEB.APIDOCS.SOURCE" ],
                      "sourceUrlMappings": [
                        {
                          "pathPrefix": "PowerForge.Web",
                          "urlPattern": "https://example.invalid/blob/main/{path}#L{line}"
                        }
                      ]
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            var indexJson = Path.Combine(root, "_site", "api", "index.json");
            Assert.True(File.Exists(indexJson), "Expected apidocs output to be generated when preflight warning is suppressed.");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ApiDocsPreflight_FailsWhenNavSurfaceConfiguredWithoutNavFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apidocs-preflight-nav-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "test.xml");
            File.WriteAllText(xmlPath, BuildMinimalXml());
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apidocs",
                      "type": "CSharp",
                      "xml": "./test.xml",
                      "out": "./_site/api",
                      "format": "json",
                      "failOnWarnings": true,
                      "navSurface": "apidocs"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("[PFWEB.APIDOCS.NAV]", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("navSurface is set but nav/navJson is empty", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ApiDocsPreflight_FailsWhenRelatedContentManifestIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apidocs-preflight-related-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "test.xml");
            File.WriteAllText(xmlPath, BuildMinimalXml());
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apidocs",
                      "type": "CSharp",
                      "xml": "./test.xml",
                      "out": "./_site/api",
                      "format": "json",
                      "failOnWarnings": true,
                      "relatedContentManifests": [ "./missing-related-content.json" ]
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("[PFWEB.APIDOCS.RELATED]", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("manifest was not found", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ApiDocsPreflight_FailsWhenSourceRootLooksOneLevelAboveGitHubRepo()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apidocs-preflight-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "TestRepo"));
            var xmlPath = Path.Combine(root, "test.xml");
            File.WriteAllText(xmlPath, BuildMinimalXml());
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apidocs",
                      "type": "CSharp",
                      "xml": "./test.xml",
                      "out": "./_site/api",
                      "format": "json",
                      "failOnWarnings": true,
                      "sourceRoot": ".",
                      "sourceUrl": "https://github.com/EvotecIT/TestRepo/blob/main/{path}#L{line}"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("[PFWEB.APIDOCS.SOURCE]", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("looks one level above repo 'TestRepo'", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ApiDocsPreflight_AllowsGitRepoRootWhenFolderNameDiffersFromRepoName()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apidocs-preflight-worktree-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, ".git"));
            Directory.CreateDirectory(Path.Combine(root, "TestRepo"));
            var xmlPath = Path.Combine(root, "test.xml");
            File.WriteAllText(xmlPath, BuildMinimalXml());
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apidocs",
                      "type": "CSharp",
                      "xml": "./test.xml",
                      "out": "./_site/api",
                      "format": "json",
                      "failOnWarnings": true,
                      "sourceRoot": ".",
                      "sourceUrl": "https://github.com/EvotecIT/TestRepo/blob/main/{path}#L{line}"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.True(File.Exists(Path.Combine(root, "_site", "api", "index.json")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ApiDocs_RespectsMemberXrefKindsAndMaxPerType()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apidocs-member-xref-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "test.xml");
            File.WriteAllText(xmlPath, BuildMinimalXmlWithMembers());
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apidocs",
                      "type": "CSharp",
                      "xml": "./test.xml",
                      "out": "./_site/api",
                      "format": "both",
                      "memberXrefKinds": [ "methods", "properties" ],
                      "memberXrefMaxPerType": 1
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);

            var xrefPath = Path.Combine(root, "_site", "api", "xrefmap.json");
            Assert.True(File.Exists(xrefPath));
            var json = File.ReadAllText(xrefPath);
            Assert.Contains("M:TestNamespace.TestType.Run(System.String)", json, StringComparison.Ordinal);
            Assert.DoesNotContain("P:TestNamespace.TestType.Name", json, StringComparison.Ordinal);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ApiDocs_FailsWhenLegacyAliasModeIsInvalid()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apidocs-legacy-mode-invalid-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "test.xml");
            File.WriteAllText(xmlPath, BuildMinimalXml());
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apidocs",
                      "type": "CSharp",
                      "xml": "./test.xml",
                      "out": "./_site/api",
                      "format": "json",
                      "legacyAliasMode": "legacy"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("legacy-alias-mode", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ApiDocs_InvalidLegacyAliasModeMessageIncludesResolvedContext()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apidocs-legacy-mode-context-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "test.xml");
            File.WriteAllText(xmlPath, BuildMinimalXml());
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apidocs",
                      "type": "CSharp",
                      "xml": "./test.xml",
                      "out": "./_site/api",
                      "format": "json",
                      "legacyAliasMode": "legacy"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("apidocs failed", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("type=CSharp", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("xml='", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("test.xml", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("out='", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("legacy-alias-mode", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ApiDocs_FailsWhenGeneratedOnlyPowerShellExamplesExceedThreshold()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apidocs-powershell-generated-threshold-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "Sample.Module-help.xml");
            File.WriteAllText(helpPath, BuildMinimalPowerShellHelpWithoutExamples());
            File.WriteAllText(Path.Combine(root, "Sample.Module.psd1"),
                """
                @{
                    CmdletsToExport = @()
                    FunctionsToExport = @('Invoke-SampleFunction')
                    AliasesToExport = @()
                    RootModule = 'Sample.Module.psm1'
                }
                """);
            File.WriteAllText(Path.Combine(root, "Sample.Module.psm1"), "function Invoke-SampleFunction { param([string]$Name) }");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apidocs",
                      "type": "PowerShell",
                      "help": "./Sample.Module-help.xml",
                      "out": "./_site/api",
                      "format": "json",
                      "failOnCoverage": true,
                      "maxPowerShellGeneratedFallbackOnlyExampleCount": 0
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("PowerShell generated-only fallback example count", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("exceeds allowed 0", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ApiDocs_FailsWhenAuthoredPowerShellExampleCoverageFallsBelowThreshold()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apidocs-powershell-authored-threshold-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "Sample.Module-help.xml");
            File.WriteAllText(helpPath, BuildMinimalPowerShellHelpWithoutExamples());
            File.WriteAllText(Path.Combine(root, "Sample.Module.psd1"),
                """
                @{
                    CmdletsToExport = @()
                    FunctionsToExport = @('Invoke-SampleFunction')
                    AliasesToExport = @()
                    RootModule = 'Sample.Module.psm1'
                }
                """);
            File.WriteAllText(Path.Combine(root, "Sample.Module.psm1"), "function Invoke-SampleFunction { param([string]$Name) }");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apidocs",
                      "type": "PowerShell",
                      "help": "./Sample.Module-help.xml",
                      "out": "./_site/api",
                      "format": "json",
                      "failOnCoverage": true,
                      "minPowerShellAuthoredHelpCodeExamplesPercent": 100
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("PowerShell authored-help code examples coverage", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("below required 100%", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ApiDocs_FailsWhenImportedPlaybackMediaIsMissingPosterArt()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apidocs-powershell-playback-poster-threshold-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "Sample.Module-help.xml");
            File.WriteAllText(helpPath, BuildMinimalPowerShellHelpForValidation());
            File.WriteAllText(Path.Combine(root, "Sample.Module.psd1"),
                """
                @{
                    CmdletsToExport = @()
                    FunctionsToExport = @('Invoke-SampleFunction')
                    AliasesToExport = @()
                    RootModule = 'Sample.Module.psm1'
                }
                """);
            File.WriteAllText(Path.Combine(root, "Sample.Module.psm1"), "function Invoke-SampleFunction { param([string]$Name) \"Ran $Name\" }");

            var examplesDir = Path.Combine(root, "Examples");
            Directory.CreateDirectory(examplesDir);
            File.WriteAllText(Path.Combine(examplesDir, "Invoke-SampleFunction.ps1"), "Invoke-SampleFunction -Name 'Alpha'");
            File.WriteAllText(Path.Combine(examplesDir, "Invoke-SampleFunction.cast"), "dummy cast");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apidocs",
                      "type": "PowerShell",
                      "help": "./Sample.Module-help.xml",
                      "psExamplesPath": "./Examples",
                      "out": "./_site/api",
                      "format": "json",
                      "failOnCoverage": true,
                      "maxPowerShellImportedScriptPlaybackMediaWithoutPosterCount": 0
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("PowerShell imported-script playback media without poster count", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("exceeds allowed 0", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ApiDocs_FailsWhenQuickStartTypesLackCuratedRelatedContent()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apidocs-related-threshold-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xmlPath = Path.Combine(root, "test.xml");
            File.WriteAllText(xmlPath,
                """
                <doc>
                  <assembly><name>RelatedCoveragePipeline</name></assembly>
                  <members>
                    <member name="T:MyNamespace.Alpha">
                      <summary>Alpha docs.</summary>
                    </member>
                    <member name="T:MyNamespace.Beta">
                      <summary>Beta docs.</summary>
                    </member>
                  </members>
                </doc>
                """);

            var manifestPath = Path.Combine(root, "related-content.json");
            File.WriteAllText(manifestPath,
                """
                {
                  "entries": [
                    {
                      "title": "Alpha guide",
                      "url": "/docs/alpha/",
                      "kind": "guide",
                      "targets": [ "T:MyNamespace.Alpha" ]
                    }
                  ]
                }
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apidocs",
                      "type": "CSharp",
                      "xml": "./test.xml",
                      "out": "./_site/api",
                      "format": "json",
                      "relatedContentManifest": "./related-content.json",
                      "quickStartTypes": "Alpha,Beta",
                      "failOnCoverage": true,
                      "minQuickStartRelatedContentPercent": 100,
                      "maxQuickStartMissingRelatedContentCount": 0
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("Quick start curated related-content coverage", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("below required 100%", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ApiDocsBatch_InfersSuiteEntriesAcrossInputs()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apidocs-suite-batch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "testimox.xml"),
                """
                <doc>
                  <assembly><name>TestimoX</name></assembly>
                  <members>
                    <member name="T:SuiteNamespace.TestimoXType">
                      <summary>TestimoX type.</summary>
                    </member>
                  </members>
                </doc>
                """);
            File.WriteAllText(Path.Combine(root, "adplayground.xml"),
                """
                <doc>
                  <assembly><name>ADPlayground</name></assembly>
                  <members>
                    <member name="T:SuiteNamespace.ADPlaygroundType">
                      <summary>ADPlayground type.</summary>
                    </member>
                  </members>
                </doc>
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apidocs",
                      "template": "docs",
                      "format": "both",
                      "suiteTitle": "Project APIs",
                      "suiteHomeUrl": "/projects/",
                      "inputs": [
                        {
                          "id": "testimox",
                          "title": "TestimoX API",
                          "type": "CSharp",
                          "xml": "./testimox.xml",
                          "out": "./_site/projects/testimox/api",
                          "baseUrl": "/projects/testimox/api"
                        },
                        {
                          "id": "adplayground",
                          "title": "ADPlayground API",
                          "type": "CSharp",
                          "xml": "./adplayground.xml",
                          "out": "./_site/projects/adplayground/api",
                          "baseUrl": "/projects/adplayground/api"
                        }
                      ]
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);

            var indexJsonPath = Path.Combine(root, "_site", "projects", "testimox", "api", "index.json");
            using var indexJson = JsonDocument.Parse(File.ReadAllText(indexJsonPath));
            var suite = indexJson.RootElement.GetProperty("suite");
            Assert.Equal("Project APIs", suite.GetProperty("title").GetString());
            Assert.Equal("testimox", suite.GetProperty("currentId").GetString());
            Assert.Equal(2, suite.GetProperty("entries").GetArrayLength());

            var indexHtmlPath = Path.Combine(root, "_site", "projects", "testimox", "api", "index.html");
            var indexHtml = File.ReadAllText(indexHtmlPath);
            Assert.Contains("api-suite-switcher", indexHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("/projects/adplayground/api", indexHtml, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectApiDocs_WritesSuiteManifestAndInjectsSwitchers()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-apidocs-suite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var sourcesRoot = Path.Combine(root, "projects-sources");
            WriteProjectApiPowerShellSource(sourcesRoot, "testimox", "TestimoX.Module-help.xml", "TestimoX.Module.psd1", "TestimoX.Module.psm1");
            WriteProjectApiPowerShellSource(sourcesRoot, "adplayground", "ADPlayground.Module-help.xml", "ADPlayground.Module.psd1", "ADPlayground.Module.psm1");

            var manifestsRoot = Path.Combine(root, "manifests");
            Directory.CreateDirectory(manifestsRoot);
            File.WriteAllText(Path.Combine(manifestsRoot, "testimox-related.json"),
                """
                {
                  "entries": [
                    {
                      "title": "TestimoX onboarding guide",
                      "url": "/projects/testimox/docs/onboarding/",
                      "summary": "Walk through the main validation workflow.",
                      "kind": "guide",
                      "targets": [ "Invoke-TestimoXAction" ]
                    }
                  ]
                }
                """);
            File.WriteAllText(Path.Combine(manifestsRoot, "suite-narrative.json"),
                """
                {
                  "summary": "Use this suite portal to choose the right API and follow the core onboarding flow.",
                  "sections": [
                    {
                      "title": "Start with the main automation path",
                      "summary": "Begin with the project most users touch first, then branch into supporting APIs.",
                      "items": [
                        {
                          "title": "Open the TestimoX quick start",
                          "url": "/projects/testimox/docs/quick-start/",
                          "summary": "Learn the main validation workflow before exploring command reference.",
                          "kind": "workflow",
                          "audience": "New maintainers",
                          "estimatedTime": "10 min",
                          "projects": [ "testimox" ]
                        },
                        {
                          "title": "Review the ADPlayground lab guide",
                          "url": "/projects/adplayground/docs/labs/",
                          "summary": "Use the playground API after the core automation flow is familiar.",
                          "kind": "lab",
                          "estimatedTime": "15 min",
                          "projects": [ "adplayground" ]
                        }
                      ]
                    }
                  ]
                }
                """);

            var catalogPath = Path.Combine(root, "catalog.json");
            File.WriteAllText(catalogPath,
                """
                {
                  "projects": [
                    {
                      "slug": "testimox",
                      "name": "TestimoX",
                      "description": "Validation automation APIs.",
                      "hubPath": "/projects/testimox/",
                      "surfaces": {
                        "apiPowerShell": true
                      },
                      "apiDocs": {
                        "relatedContentManifest": "./manifests/testimox-related.json"
                      }
                    },
                    {
                      "slug": "adplayground",
                      "name": "ADPlayground",
                      "description": "Directory lab APIs.",
                      "hubPath": "/projects/adplayground/",
                      "surfaces": {
                        "apiPowerShell": true
                      }
                    }
                  ]
                }
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "project-apidocs",
                      "catalog": "./catalog.json",
                      "sourcesRoot": "./projects-sources",
                      "outRoot": "./_site/projects",
                      "template": "docs",
                      "format": "both",
                      "suiteTitle": "Project APIs",
                      "suiteNarrativeManifest": "./manifests/suite-narrative.json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("suite=api-suite.json", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("suite-search=api-suite-search.json", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("suite-xref=api-suite-xrefmap.json", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("suite-coverage=api-suite-coverage.json", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("suite-landing=api-suite/", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            var suiteManifestPath = Path.Combine(root, "_site", "projects", "api-suite.json");
            Assert.True(File.Exists(suiteManifestPath));
            using var suiteManifest = JsonDocument.Parse(File.ReadAllText(suiteManifestPath));
            Assert.Equal("Project APIs", suiteManifest.RootElement.GetProperty("title").GetString());
            Assert.Equal("/projects/api-suite/", suiteManifest.RootElement.GetProperty("homeUrl").GetString());
            Assert.Equal(2, suiteManifest.RootElement.GetProperty("entries").GetArrayLength());
            Assert.Equal("./api-suite/index.html", suiteManifest.RootElement.GetProperty("landingPath").GetString());
            Assert.Equal("/projects/api-suite/", suiteManifest.RootElement.GetProperty("landingUrl").GetString());
            Assert.Equal("./api-suite-search.json", suiteManifest.RootElement.GetProperty("artifacts").GetProperty("searchPath").GetString());
            Assert.Equal("./api-suite-xrefmap.json", suiteManifest.RootElement.GetProperty("artifacts").GetProperty("xrefMapPath").GetString());
            Assert.Equal("./api-suite-coverage.json", suiteManifest.RootElement.GetProperty("artifacts").GetProperty("coveragePath").GetString());
            Assert.Equal("./api-suite-related-content.json", suiteManifest.RootElement.GetProperty("artifacts").GetProperty("relatedContentPath").GetString());
            Assert.Equal("./api-suite-narrative.json", suiteManifest.RootElement.GetProperty("artifacts").GetProperty("narrativePath").GetString());

            var suiteLandingIndexPath = Path.Combine(root, "_site", "projects", "api-suite", "index.html");
            Assert.True(File.Exists(suiteLandingIndexPath));
            var suiteLandingHtml = File.ReadAllText(suiteLandingIndexPath);
            Assert.Contains("api-suite-search", suiteLandingHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("api-suite-search-filter", suiteLandingHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("api-suite-narrative", suiteLandingHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Start Here", suiteLandingHtml, StringComparison.Ordinal);
            Assert.Contains("api-suite-coverage-summary", suiteLandingHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("api-suite-related-content", suiteLandingHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("../api-suite-search.json", suiteLandingHtml, StringComparison.Ordinal);
            Assert.Contains("../api-suite-narrative.json", suiteLandingHtml, StringComparison.Ordinal);
            Assert.Contains("../api-suite-coverage.json", suiteLandingHtml, StringComparison.Ordinal);
            Assert.Contains("../api-suite-related-content.json", suiteLandingHtml, StringComparison.Ordinal);
            Assert.Contains("/projects/testimox/api/", suiteLandingHtml, StringComparison.Ordinal);

            var suiteSearchPath = Path.Combine(root, "_site", "projects", "api-suite-search.json");
            Assert.True(File.Exists(suiteSearchPath));
            using var suiteSearch = JsonDocument.Parse(File.ReadAllText(suiteSearchPath));
            Assert.Equal(2, suiteSearch.RootElement.GetProperty("itemCount").GetInt32());
            Assert.Equal(2, suiteSearch.RootElement.GetProperty("items").GetArrayLength());
            using var projectIndexJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "_site", "projects", "testimox", "api", "index.json")));
            Assert.Equal("../../api-suite-search.json", projectIndexJson.RootElement.GetProperty("suite").GetProperty("searchUrl").GetString());
            Assert.Equal("/projects/api-suite/", projectIndexJson.RootElement.GetProperty("suite").GetProperty("homeUrl").GetString());

            var suiteXrefPath = Path.Combine(root, "_site", "projects", "api-suite-xrefmap.json");
            Assert.True(File.Exists(suiteXrefPath));
            using var suiteXref = JsonDocument.Parse(File.ReadAllText(suiteXrefPath));
            Assert.True(suiteXref.RootElement.GetProperty("referenceCount").GetInt32() >= 2);

            var suiteCoveragePath = Path.Combine(root, "_site", "projects", "api-suite-coverage.json");
            Assert.True(File.Exists(suiteCoveragePath));
            using var suiteCoverage = JsonDocument.Parse(File.ReadAllText(suiteCoveragePath));
            Assert.Equal(2, suiteCoverage.RootElement.GetProperty("projectCount").GetInt32());
            Assert.Equal(2, suiteCoverage.RootElement.GetProperty("types").GetProperty("count").GetInt32());
            Assert.Equal(2, suiteCoverage.RootElement.GetProperty("powershell").GetProperty("commandCount").GetInt32());

            var suiteNarrativePath = Path.Combine(root, "_site", "projects", "api-suite-narrative.json");
            Assert.True(File.Exists(suiteNarrativePath));
            using var suiteNarrative = JsonDocument.Parse(File.ReadAllText(suiteNarrativePath));
            Assert.Equal(1, suiteNarrative.RootElement.GetProperty("sectionCount").GetInt32());
            Assert.Equal(2, suiteNarrative.RootElement.GetProperty("itemCount").GetInt32());
            Assert.Equal(
                "Use this suite portal to choose the right API and follow the core onboarding flow.",
                suiteNarrative.RootElement.GetProperty("summary").GetString());
            Assert.True(suiteNarrative.RootElement.GetProperty("content").GetProperty("summaryPresent").GetBoolean());
            Assert.Equal(100d, suiteNarrative.RootElement.GetProperty("coverage").GetProperty("suiteEntries").GetProperty("percent").GetDouble());
            Assert.Equal(0, suiteNarrative.RootElement.GetProperty("coverage").GetProperty("suiteEntries").GetProperty("uncoveredCount").GetInt32());
            var narrativeItem = suiteNarrative.RootElement.GetProperty("sections")[0].GetProperty("items")[0];
            Assert.Equal("workflow", narrativeItem.GetProperty("kind").GetString());
            Assert.Equal("New maintainers", narrativeItem.GetProperty("audience").GetString());
            Assert.Contains(
                "TestimoX",
                narrativeItem.GetProperty("suiteEntryLabels").EnumerateArray().Select(static item => item.GetString()).Where(static item => !string.IsNullOrWhiteSpace(item)));

            var suiteRelatedContentPath = Path.Combine(root, "_site", "projects", "api-suite-related-content.json");
            Assert.True(File.Exists(suiteRelatedContentPath));
            using var suiteRelatedContent = JsonDocument.Parse(File.ReadAllText(suiteRelatedContentPath));
            Assert.Equal(1, suiteRelatedContent.RootElement.GetProperty("itemCount").GetInt32());
            var relatedItem = suiteRelatedContent.RootElement.GetProperty("items")[0];
            Assert.Equal("testimox", relatedItem.GetProperty("suiteEntryId").GetString());
            Assert.Equal("TestimoX", relatedItem.GetProperty("suiteEntryLabel").GetString());
            Assert.Equal("/projects/testimox/docs/onboarding/", relatedItem.GetProperty("url").GetString());

            var apiIndexPath = Path.Combine(root, "_site", "projects", "testimox", "api", "index.html");
            var html = File.ReadAllText(apiIndexPath);
            Assert.Contains("api-suite-switcher", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("/projects/adplayground/api/", html, StringComparison.OrdinalIgnoreCase);

            Assert.Equal("../../api-suite-related-content.json", projectIndexJson.RootElement.GetProperty("suite").GetProperty("relatedContentUrl").GetString());
            Assert.Equal("../../api-suite-narrative.json", projectIndexJson.RootElement.GetProperty("suite").GetProperty("narrativeUrl").GetString());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectApiDocs_DoesNotAppendSuiteLandingTwice_WhenSuiteHomeAlreadyTargetsSuiteRoute()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-apidocs-suite-home-route-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var sourcesRoot = Path.Combine(root, "projects-sources");
            WriteProjectApiPowerShellSource(sourcesRoot, "testimox", "TestimoX.Module-help.xml", "TestimoX.Module.psd1", "TestimoX.Module.psm1");
            WriteProjectApiPowerShellSource(sourcesRoot, "adplayground", "ADPlayground.Module-help.xml", "ADPlayground.Module.psd1", "ADPlayground.Module.psm1");

            var catalogPath = Path.Combine(root, "catalog.json");
            File.WriteAllText(catalogPath,
                """
                {
                  "projects": [
                    {
                      "slug": "testimox",
                      "name": "TestimoX",
                      "hubPath": "/projects/testimox/",
                      "surfaces": {
                        "apiPowerShell": true
                      }
                    },
                    {
                      "slug": "adplayground",
                      "name": "ADPlayground",
                      "hubPath": "/projects/adplayground/",
                      "surfaces": {
                        "apiPowerShell": true
                      }
                    }
                  ]
                }
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "project-apidocs",
                      "catalog": "./catalog.json",
                      "sourcesRoot": "./projects-sources",
                      "outRoot": "./_site/projects",
                      "template": "docs",
                      "format": "json",
                      "suiteTitle": "Project APIs",
                      "suiteHomeUrl": "/projects/api-suite/"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);

            var suiteManifestPath = Path.Combine(root, "_site", "projects", "api-suite.json");
            using var suiteManifest = JsonDocument.Parse(File.ReadAllText(suiteManifestPath));
            Assert.Equal("/projects/api-suite/", suiteManifest.RootElement.GetProperty("homeUrl").GetString());
            Assert.Equal("/projects/api-suite/", suiteManifest.RootElement.GetProperty("landingUrl").GetString());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectApiDocs_PreservesAbsoluteSuiteLandingUrl()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-apidocs-suite-absolute-landing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var sourcesRoot = Path.Combine(root, "projects-sources");
            WriteProjectApiPowerShellSource(sourcesRoot, "testimox", "TestimoX.Module-help.xml", "TestimoX.Module.psd1", "TestimoX.Module.psm1");
            WriteProjectApiPowerShellSource(sourcesRoot, "adplayground", "ADPlayground.Module-help.xml", "ADPlayground.Module.psd1", "ADPlayground.Module.psm1");

            var catalogPath = Path.Combine(root, "catalog.json");
            File.WriteAllText(catalogPath,
                """
                {
                  "projects": [
                    {
                      "slug": "testimox",
                      "name": "TestimoX",
                      "hubPath": "/projects/testimox/",
                      "surfaces": {
                        "apiPowerShell": true
                      }
                    },
                    {
                      "slug": "adplayground",
                      "name": "ADPlayground",
                      "hubPath": "/projects/adplayground/",
                      "surfaces": {
                        "apiPowerShell": true
                      }
                    }
                  ]
                }
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "project-apidocs",
                      "catalog": "./catalog.json",
                      "sourcesRoot": "./projects-sources",
                      "outRoot": "./_site/projects",
                      "template": "docs",
                      "format": "json",
                      "suiteTitle": "Project APIs",
                      "suiteLandingUrl": "https://docs.example.com/projects/"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);

            var suiteManifestPath = Path.Combine(root, "_site", "projects", "api-suite.json");
            using var suiteManifest = JsonDocument.Parse(File.ReadAllText(suiteManifestPath));
            Assert.Equal("https://docs.example.com/projects/api-suite/", suiteManifest.RootElement.GetProperty("landingUrl").GetString());
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectApiDocs_WritesSuiteCoverageFromCatalogApiDocsOverrides()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-apidocs-suite-overrides-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var sourcesRoot = Path.Combine(root, "projects-sources");
            WriteProjectApiPowerShellSource(
                sourcesRoot,
                "testimox",
                "TestimoX.Module-help.xml",
                "TestimoX.Module.psd1",
                "TestimoX.Module.psm1",
                "Invoke-TestimoXAction");
            WriteProjectApiPowerShellSource(
                sourcesRoot,
                "adplayground",
                "ADPlayground.Module-help.xml",
                "ADPlayground.Module.psd1",
                "ADPlayground.Module.psm1",
                "Invoke-ADPlaygroundAction");

            var manifestsRoot = Path.Combine(root, "manifests");
            Directory.CreateDirectory(manifestsRoot);
            File.WriteAllText(Path.Combine(manifestsRoot, "testimox-related.json"),
                """
                {
                  "entries": [
                    {
                      "title": "TestimoX quick start",
                      "url": "/projects/testimox/docs/quick-start/",
                      "kind": "guide",
                      "targets": [ "Invoke-TestimoXAction" ]
                    }
                  ]
                }
                """);

            var catalogPath = Path.Combine(root, "catalog.json");
            File.WriteAllText(catalogPath,
                """
                {
                  "projects": [
                    {
                      "slug": "testimox",
                      "name": "TestimoX",
                      "description": "Validation automation APIs.",
                      "hubPath": "/projects/testimox/",
                      "surfaces": {
                        "apiPowerShell": true
                      },
                      "apiDocs": {
                        "quickStartTypes": "Invoke-TestimoXAction",
                        "relatedContentManifest": "./manifests/testimox-related.json"
                      }
                    },
                    {
                      "slug": "adplayground",
                      "name": "ADPlayground",
                      "description": "Directory lab APIs.",
                      "hubPath": "/projects/adplayground/",
                      "surfaces": {
                        "apiPowerShell": true
                      },
                      "apiDocs": {
                        "quickStartTypes": "Invoke-ADPlaygroundAction"
                      }
                    }
                  ]
                }
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "project-apidocs",
                      "catalog": "./catalog.json",
                      "sourcesRoot": "./projects-sources",
                      "outRoot": "./_site/projects",
                      "template": "docs",
                      "format": "json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);

            var suiteCoveragePath = Path.Combine(root, "_site", "projects", "api-suite-coverage.json");
            Assert.True(File.Exists(suiteCoveragePath));
            using var suiteCoverage = JsonDocument.Parse(File.ReadAllText(suiteCoveragePath));
            var types = suiteCoverage.RootElement.GetProperty("types");
            Assert.Equal(2, types.GetProperty("quickStartRelatedContent").GetProperty("total").GetInt32());
            Assert.Equal(1, types.GetProperty("quickStartRelatedContent").GetProperty("covered").GetInt32());
            Assert.Equal(50d, types.GetProperty("quickStartRelatedContent").GetProperty("percent").GetDouble());
            Assert.Equal(1, types.GetProperty("quickStartMissingRelatedContent").GetProperty("count").GetInt32());
            var projects = suiteCoverage.RootElement.GetProperty("projects");
            Assert.Equal("./testimox/api/coverage.json", projects[0].GetProperty("coveragePath").GetString());
            Assert.Equal("./adplayground/api/coverage.json", projects[1].GetProperty("coveragePath").GetString());
            Assert.Contains(
                "Invoke-ADPlaygroundAction",
                types.GetProperty("quickStartMissingRelatedContent").GetProperty("types").EnumerateArray().Select(static item => item.GetString()).Where(static item => !string.IsNullOrWhiteSpace(item)));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectApiDocs_ReportsSuiteStarterRecommendationsWhenGuidanceIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-apidocs-suite-recommendations-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var sourcesRoot = Path.Combine(root, "projects-sources");
            WriteProjectApiPowerShellSource(sourcesRoot, "testimox", "TestimoX.Module-help.xml", "TestimoX.Module.psd1", "TestimoX.Module.psm1");
            WriteProjectApiPowerShellSource(sourcesRoot, "adplayground", "ADPlayground.Module-help.xml", "ADPlayground.Module.psd1", "ADPlayground.Module.psm1");

            var catalogPath = Path.Combine(root, "catalog.json");
            File.WriteAllText(catalogPath,
                """
                {
                  "projects": [
                    {
                      "slug": "testimox",
                      "name": "TestimoX",
                      "hubPath": "/projects/testimox/",
                      "surfaces": {
                        "apiPowerShell": true
                      }
                    },
                    {
                      "slug": "adplayground",
                      "name": "ADPlayground",
                      "hubPath": "/projects/adplayground/",
                      "surfaces": {
                        "apiPowerShell": true
                      }
                    }
                  ]
                }
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "project-apidocs",
                      "catalog": "./catalog.json",
                      "sourcesRoot": "./projects-sources",
                      "outRoot": "./_site/projects",
                      "format": "json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("suite-guidance-recommendations=3", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectApiDocs_ReportsStarterTemplateRecommendationsWhenSuiteScaffoldIsStillUntouched()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-apidocs-suite-starter-empty-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "data", "projects"));
            Directory.CreateDirectory(Path.Combine(root, "content", "docs", "projects"));
            File.WriteAllText(Path.Combine(root, "data", "projects", "catalog.json"), """{ "projects": [] }""");
            File.WriteAllText(Path.Combine(root, "data", "projects", "catalog.project-template.json"), """{ "slug": "sample-project" }""");
            File.WriteAllText(Path.Combine(root, "data", "projects", "sample-project-api-guides.json"), """{ "entries": [] }""");
            File.WriteAllText(Path.Combine(root, "content", "docs", "projects", "api-guide-template.md"), "# Sample");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "project-apidocs",
                      "catalog": "./data/projects/catalog.json",
                      "sourcesRoot": "./projects-sources",
                      "outRoot": "./_site/projects",
                      "format": "json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("generated=0", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("suite-guidance-recommendations=1", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectApiDocs_ReportsStarterTemplateRecommendationsWhenSampleProjectPlaceholdersLeakIntoCatalog()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-apidocs-suite-starter-sample-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var sourcesRoot = Path.Combine(root, "projects-sources");
            WriteProjectApiPowerShellSource(sourcesRoot, "sample-project", "Sample.Module-help.xml", "Sample.Module.psd1", "Sample.Module.psm1");
            WriteProjectApiPowerShellSource(sourcesRoot, "real-project", "Real.Module-help.xml", "Real.Module.psd1", "Real.Module.psm1");

            Directory.CreateDirectory(Path.Combine(root, "data", "projects"));
            Directory.CreateDirectory(Path.Combine(root, "content", "docs", "projects"));
            File.WriteAllText(Path.Combine(root, "data", "projects", "catalog.project-template.json"), """{ "slug": "sample-project" }""");
            File.WriteAllText(Path.Combine(root, "data", "projects", "sample-project-api-guides.json"), """{ "entries": [] }""");
            File.WriteAllText(Path.Combine(root, "content", "docs", "projects", "api-guide-template.md"), "# Sample");
            File.WriteAllText(Path.Combine(root, "data", "projects", "catalog.json"),
                """
                {
                  "projects": [
                    {
                      "slug": "sample-project",
                      "name": "Sample Project",
                      "hubPath": "/projects/sample-project/",
                      "surfaces": {
                        "apiPowerShell": true
                      },
                      "apiDocs": {
                        "quickStartTypes": "Invoke-SampleProjectAction",
                        "relatedContentManifest": "./data/projects/sample-project-api-guides.json"
                      }
                    },
                    {
                      "slug": "real-project",
                      "name": "Real Project",
                      "hubPath": "/projects/real-project/",
                      "surfaces": {
                        "apiPowerShell": true
                      },
                      "apiDocs": {
                        "quickStartTypes": "Invoke-RealProjectAction"
                      }
                    }
                  ]
                }
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "project-apidocs",
                      "catalog": "./data/projects/catalog.json",
                      "sourcesRoot": "./projects-sources",
                      "outRoot": "./_site/projects",
                      "format": "json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("suite-guidance-recommendations=4", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectApiDocs_FailsWhenSuiteCoverageThresholdsAreViolated()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-apidocs-suite-thresholds-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var sourcesRoot = Path.Combine(root, "projects-sources");
            WriteProjectApiPowerShellSource(sourcesRoot, "testimox", "TestimoX.Module-help.xml", "TestimoX.Module.psd1", "TestimoX.Module.psm1");
            WriteProjectApiPowerShellSource(sourcesRoot, "adplayground", "ADPlayground.Module-help.xml", "ADPlayground.Module.psd1", "ADPlayground.Module.psm1");

            var catalogPath = Path.Combine(root, "catalog.json");
            File.WriteAllText(catalogPath,
                """
                {
                  "projects": [
                    {
                      "slug": "testimox",
                      "name": "TestimoX",
                      "hubPath": "/projects/testimox/",
                      "surfaces": {
                        "apiPowerShell": true
                      },
                      "apiDocs": {
                        "quickStartTypes": "Invoke-SampleFunction"
                      }
                    },
                    {
                      "slug": "adplayground",
                      "name": "ADPlayground",
                      "hubPath": "/projects/adplayground/",
                      "surfaces": {
                        "apiPowerShell": true
                      },
                      "apiDocs": {
                        "quickStartTypes": "Invoke-SampleFunction"
                      }
                    }
                  ]
                }
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "project-apidocs",
                      "catalog": "./catalog.json",
                      "sourcesRoot": "./projects-sources",
                      "outRoot": "./_site/projects",
                      "format": "json",
                      "suiteFailOnCoverage": true,
                      "suiteCoverage": {
                        "maxQuickStartMissingRelatedContentCount": 1
                      }
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("Quick start types missing curated related content count", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("exceeds allowed 1", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectApiDocs_FailsWhenSuiteNarrativeThresholdsAreViolated()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-apidocs-suite-narrative-thresholds-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var sourcesRoot = Path.Combine(root, "projects-sources");
            WriteProjectApiPowerShellSource(sourcesRoot, "testimox", "TestimoX.Module-help.xml", "TestimoX.Module.psd1", "TestimoX.Module.psm1");
            WriteProjectApiPowerShellSource(sourcesRoot, "adplayground", "ADPlayground.Module-help.xml", "ADPlayground.Module.psd1", "ADPlayground.Module.psm1");

            var manifestsRoot = Path.Combine(root, "manifests");
            Directory.CreateDirectory(manifestsRoot);
            File.WriteAllText(Path.Combine(manifestsRoot, "suite-narrative.json"),
                """
                {
                  "sections": [
                    {
                      "title": "Only one project is covered",
                      "items": [
                        {
                          "title": "TestimoX quick start",
                          "url": "/projects/testimox/docs/quick-start/",
                          "kind": "workflow",
                          "projects": [ "testimox" ]
                        }
                      ]
                    }
                  ]
                }
                """);

            var catalogPath = Path.Combine(root, "catalog.json");
            File.WriteAllText(catalogPath,
                """
                {
                  "projects": [
                    {
                      "slug": "testimox",
                      "name": "TestimoX",
                      "hubPath": "/projects/testimox/",
                      "surfaces": {
                        "apiPowerShell": true
                      }
                    },
                    {
                      "slug": "adplayground",
                      "name": "ADPlayground",
                      "hubPath": "/projects/adplayground/",
                      "surfaces": {
                        "apiPowerShell": true
                      }
                    }
                  ]
                }
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "project-apidocs",
                      "catalog": "./catalog.json",
                      "sourcesRoot": "./projects-sources",
                      "outRoot": "./_site/projects",
                      "format": "json",
                      "suiteNarrativeManifest": "./manifests/suite-narrative.json",
                      "suiteFailOnNarrative": true,
                      "suiteNarrative": {
                        "requireSummary": true,
                        "minSuiteEntryCoveragePercent": 100,
                        "maxUncoveredSuiteEntryCount": 0
                      }
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("Suite narrative summary is required", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ApiDocs_ReportsPowerShellExampleMediaManifestInStepMessage()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apidocs-powershell-media-manifest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "Sample.Module-help.xml");
            File.WriteAllText(helpPath, BuildMinimalPowerShellHelpForValidation());
            File.WriteAllText(Path.Combine(root, "Sample.Module.psd1"),
                """
                @{
                    CmdletsToExport = @()
                    FunctionsToExport = @('Invoke-SampleFunction')
                    AliasesToExport = @()
                    RootModule = 'Sample.Module.psm1'
                }
                """);
            File.WriteAllText(Path.Combine(root, "Sample.Module.psm1"), "function Invoke-SampleFunction { param([string]$Name) \"Ran $Name\" }");

            var examplesDir = Path.Combine(root, "Examples");
            Directory.CreateDirectory(examplesDir);
            File.WriteAllText(Path.Combine(examplesDir, "Invoke-SampleFunction.ps1"), "Invoke-SampleFunction -Name 'Alpha'");
            File.WriteAllText(Path.Combine(examplesDir, "Invoke-SampleFunction.cast"), "dummy cast");
            File.WriteAllBytes(Path.Combine(examplesDir, "Invoke-SampleFunction.png"), new byte[] { 1, 2, 3, 4 });

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apidocs",
                      "type": "PowerShell",
                      "help": "./Sample.Module-help.xml",
                      "psExamplesPath": "./Examples",
                      "out": "./_site/api",
                      "format": "json"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
            Assert.Contains("ps-example-media: powershell-example-media-manifest.json", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(root, "_site", "api", "powershell-example-media-manifest.json")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ApiDocs_FailsWhenImportedPlaybackMediaAssetsLookStale()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apidocs-powershell-playback-stale-threshold-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "Sample.Module-help.xml");
            File.WriteAllText(helpPath, BuildMinimalPowerShellHelpForValidation());
            File.WriteAllText(Path.Combine(root, "Sample.Module.psd1"),
                """
                @{
                    CmdletsToExport = @()
                    FunctionsToExport = @('Invoke-SampleFunction')
                    AliasesToExport = @()
                    RootModule = 'Sample.Module.psm1'
                }
                """);
            File.WriteAllText(Path.Combine(root, "Sample.Module.psm1"), "function Invoke-SampleFunction { param([string]$Name) \"Ran $Name\" }");

            var examplesDir = Path.Combine(root, "Examples");
            Directory.CreateDirectory(examplesDir);
            var examplePath = Path.Combine(examplesDir, "Invoke-SampleFunction.ps1");
            var castPath = Path.Combine(examplesDir, "Invoke-SampleFunction.cast");
            File.WriteAllText(examplePath, "Invoke-SampleFunction -Name 'Alpha'");
            File.WriteAllText(castPath, "dummy cast");

            var now = DateTime.UtcNow;
            File.SetLastWriteTimeUtc(castPath, now.AddMinutes(-20));
            File.SetLastWriteTimeUtc(examplePath, now.AddMinutes(-5));

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apidocs",
                      "type": "PowerShell",
                      "help": "./Sample.Module-help.xml",
                      "psExamplesPath": "./Examples",
                      "out": "./_site/api",
                      "format": "json",
                      "failOnCoverage": true,
                      "maxPowerShellImportedScriptPlaybackMediaStaleAssetCount": 0
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("PowerShell imported-script playback media with stale assets count", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("exceeds allowed 0", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ApiDocsPreflight_AllowsSourceRootParentWhenSourceUrlUsesPathNoRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apidocs-preflight-pathnoroot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "TestRepo"));
            var xmlPath = Path.Combine(root, "test.xml");
            File.WriteAllText(xmlPath, BuildMinimalXml());
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apidocs",
                      "type": "CSharp",
                      "xml": "./test.xml",
                      "out": "./_site/api",
                      "format": "json",
                      "failOnWarnings": true,
                      "sourceRoot": ".",
                      "sourceUrl": "https://github.com/EvotecIT/TestRepo/blob/main/{pathNoRoot}#L{line}"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ApiDocs_FailsWhenPowerShellExampleValidationFindsInvalidScripts()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apidocs-powershell-validate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "Sample.Module-help.xml");
            File.WriteAllText(helpPath, BuildMinimalPowerShellHelpForValidation());
            File.WriteAllText(Path.Combine(root, "Sample.Module.psd1"),
                """
                @{
                    CmdletsToExport = @()
                    FunctionsToExport = @('Invoke-SampleFunction')
                    AliasesToExport = @()
                    RootModule = 'Sample.Module.psm1'
                }
                """);
            File.WriteAllText(Path.Combine(root, "Sample.Module.psm1"), "function Invoke-SampleFunction { param([string]$Name) }");

            var examplesDir = Path.Combine(root, "Examples");
            Directory.CreateDirectory(examplesDir);
            File.WriteAllText(Path.Combine(examplesDir, "BrokenExample.ps1"), "Invoke-SampleFunction -Name (");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apidocs",
                      "type": "PowerShell",
                      "help": "./Sample.Module-help.xml",
                      "out": "./_site/api",
                      "format": "json",
                      "validatePowerShellExamples": true,
                      "failOnPowerShellExampleValidation": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("PowerShell example validation found 1 invalid script", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            var reportPath = Path.Combine(root, "_site", "api", "powershell-example-validation.json");
            Assert.True(File.Exists(reportPath));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ApiDocs_FailOnPowerShellExampleValidationImpliesValidation()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apidocs-powershell-validate-implicit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "Sample.Module-help.xml");
            File.WriteAllText(helpPath, BuildMinimalPowerShellHelpForValidation());
            File.WriteAllText(Path.Combine(root, "Sample.Module.psd1"),
                """
                @{
                    CmdletsToExport = @()
                    FunctionsToExport = @('Invoke-SampleFunction')
                    AliasesToExport = @()
                    RootModule = 'Sample.Module.psm1'
                }
                """);
            File.WriteAllText(Path.Combine(root, "Sample.Module.psm1"), "function Invoke-SampleFunction { param([string]$Name) }");

            var examplesDir = Path.Combine(root, "Examples");
            Directory.CreateDirectory(examplesDir);
            File.WriteAllText(Path.Combine(examplesDir, "BrokenExample.ps1"), "Invoke-SampleFunction -Name (");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apidocs",
                      "type": "PowerShell",
                      "help": "./Sample.Module-help.xml",
                      "out": "./_site/api",
                      "format": "json",
                      "failOnPowerShellExampleValidation": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("PowerShell example validation found 1 invalid script", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(root, "_site", "api", "powershell-example-validation.json")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ApiDocs_FailsWhenPowerShellExampleExecutionFindsFailingScripts()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apidocs-powershell-execute-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "Sample.Module-help.xml");
            File.WriteAllText(helpPath, BuildMinimalPowerShellHelpForValidation());
            File.WriteAllText(Path.Combine(root, "Sample.Module.psd1"),
                """
                @{
                    CmdletsToExport = @()
                    FunctionsToExport = @('Invoke-SampleFunction')
                    AliasesToExport = @()
                    RootModule = 'Sample.Module.psm1'
                }
                """);
            File.WriteAllText(Path.Combine(root, "Sample.Module.psm1"),
                """
                function Invoke-SampleFunction { param([string]$Name) "Executed $Name" }
                """);

            var examplesDir = Path.Combine(root, "Examples");
            Directory.CreateDirectory(examplesDir);
            File.WriteAllText(Path.Combine(examplesDir, "Invoke-SampleFunction.Good.ps1"), "Invoke-SampleFunction -Name 'Alpha'");
            File.WriteAllText(Path.Combine(examplesDir, "Invoke-SampleFunction.Fail.ps1"), "Invoke-SampleFunction -Name 'Beta'; throw 'boom'");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apidocs",
                      "type": "PowerShell",
                      "help": "./Sample.Module-help.xml",
                      "out": "./_site/api",
                      "format": "json",
                      "validatePowerShellExamples": true,
                      "executePowerShellExamples": true,
                      "failOnPowerShellExampleExecution": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("PowerShell example execution failed for 1 script", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);

            var reportPath = Path.Combine(root, "_site", "api", "powershell-example-validation.json");
            Assert.True(File.Exists(reportPath));
            using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
            Assert.True(report.RootElement.GetProperty("executionRequested").GetBoolean());
            Assert.Equal(1, report.RootElement.GetProperty("failedExecutionFileCount").GetInt32());
            var artifactPaths = report.RootElement.GetProperty("files")
                .EnumerateArray()
                .Select(file => file.TryGetProperty("executionArtifactPath", out var path) ? path.GetString() : null)
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .ToArray();
            Assert.True(artifactPaths.Length >= 1);
            var reportDirectory = Path.GetDirectoryName(reportPath)!;
            Assert.All(artifactPaths, path =>
            {
                Assert.False(Path.IsPathRooted(path));
                Assert.True(File.Exists(Path.GetFullPath(Path.Combine(reportDirectory, path!))));
            });
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ApiDocs_FailOnPowerShellExampleExecutionImpliesExecution()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apidocs-powershell-execute-implicit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "Sample.Module-help.xml");
            File.WriteAllText(helpPath, BuildMinimalPowerShellHelpForValidation());
            File.WriteAllText(Path.Combine(root, "Sample.Module.psd1"),
                """
                @{
                    CmdletsToExport = @()
                    FunctionsToExport = @('Invoke-SampleFunction')
                    AliasesToExport = @()
                    RootModule = 'Sample.Module.psm1'
                }
                """);
            File.WriteAllText(Path.Combine(root, "Sample.Module.psm1"),
                """
                function Invoke-SampleFunction { param([string]$Name) "Executed $Name" }
                """);

            var examplesDir = Path.Combine(root, "Examples");
            Directory.CreateDirectory(examplesDir);
            File.WriteAllText(Path.Combine(examplesDir, "Invoke-SampleFunction.Fail.ps1"), "Invoke-SampleFunction -Name 'Beta'; throw 'boom'");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apidocs",
                      "type": "PowerShell",
                      "help": "./Sample.Module-help.xml",
                      "out": "./_site/api",
                      "format": "json",
                      "failOnPowerShellExampleExecution": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("PowerShell example execution failed for 1 script", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(root, "_site", "api", "powershell-example-validation.json")));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ApiDocs_FailsWhenPercentCoverageThresholdExceedsOneHundred()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apidocs-threshold-percent-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpPath = Path.Combine(root, "Sample.Module-help.xml");
            File.WriteAllText(helpPath, BuildMinimalPowerShellHelpForValidation());
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apidocs",
                      "type": "PowerShell",
                      "help": "./Sample.Module-help.xml",
                      "out": "./_site/api",
                      "format": "json",
                      "maxPowerShellGeneratedFallbackOnlyExamplePercent": 150
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("must be less than or equal to 100", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ApiDocs_EmitsGitFreshnessMetadataWhenEnabled()
    {
        if (!IsGitAvailable())
            return;

        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-apidocs-freshness-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var helpDirectory = Path.Combine(root, "en-US");
            Directory.CreateDirectory(helpDirectory);
            var helpPath = Path.Combine(helpDirectory, "Sample.Module-help.xml");
            var aboutPath = Path.Combine(root, "about_SampleTopic.help.txt");

            RunGit(root, "init");
            RunGit(root, "config", "user.email", "tests@example.invalid");
            RunGit(root, "config", "user.name", "PowerForge Tests");

            File.WriteAllText(aboutPath,
                """
                # about_SampleTopic

                Topic body.
                """);
            RunGit(root, "add", "about_SampleTopic.help.txt");
            var olderCommitDate = DateTimeOffset.UtcNow.AddDays(-35).ToString("yyyy-MM-ddTHH:mm:ssK");
            RunGit(root,
                new Dictionary<string, string>
                {
                    ["GIT_AUTHOR_DATE"] = olderCommitDate,
                    ["GIT_COMMITTER_DATE"] = olderCommitDate
                },
                "commit", "-m", "Add about topic");

            File.WriteAllText(helpPath,
                """
                <?xml version="1.0" encoding="utf-8"?>
                <helpItems schema="maml" xmlns="http://msh" xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10" xmlns:dev="http://schemas.microsoft.com/maml/dev/2004/10">
                  <command:command>
                    <command:details>
                      <command:name>Get-SampleCmdlet</command:name>
                      <maml:description><maml:para>Gets data.</maml:para></maml:description>
                    </command:details>
                    <maml:description>
                      <maml:para>For more details, see about_SampleTopic.</maml:para>
                    </maml:description>
                    <command:syntax><command:syntaxItem><command:name>Get-SampleCmdlet</command:name></command:syntaxItem></command:syntax>
                  </command:command>
                </helpItems>
                """);
            RunGit(root, "add", "en-US/Sample.Module-help.xml");
            var recentCommitDate = DateTimeOffset.UtcNow.AddDays(-2).ToString("yyyy-MM-ddTHH:mm:ssK");
            RunGit(root,
                new Dictionary<string, string>
                {
                    ["GIT_AUTHOR_DATE"] = recentCommitDate,
                    ["GIT_COMMITTER_DATE"] = recentCommitDate
                },
                "commit", "-m", "Add help xml");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "apidocs",
                      "type": "PowerShell",
                      "help": ".",
                      "out": "./_site/api",
                      "format": "both",
                      "template": "docs",
                      "generateGitFreshness": true,
                      "gitFreshnessNewDays": 14,
                      "gitFreshnessUpdatedDays": 90
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success);

            using var commandJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "_site", "api", "types", "get-samplecmdlet.json")));
            using var aboutJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "_site", "api", "types", "about-sampletopic.json")));
            Assert.Equal("new", commandJson.RootElement.GetProperty("freshness").GetProperty("status").GetString());
            Assert.Equal("updated", aboutJson.RootElement.GetProperty("freshness").GetProperty("status").GetString());
            Assert.DoesNotContain(root, commandJson.RootElement.GetProperty("freshness").GetProperty("sourcePath").GetString(), StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(root, aboutJson.RootElement.GetProperty("freshness").GetProperty("sourcePath").GetString(), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static string BuildMinimalXml()
    {
        return
            """
            <doc>
              <assembly>
                <name>TestAssembly</name>
              </assembly>
              <members>
                <member name="T:TestNamespace.TestType">
                  <summary>Type summary.</summary>
                </member>
              </members>
            </doc>
            """;
    }

    private static string BuildMinimalXmlWithMembers()
    {
        return
            """
            <doc>
              <assembly>
                <name>TestAssembly</name>
              </assembly>
              <members>
                <member name="T:TestNamespace.TestType">
                  <summary>Type summary.</summary>
                </member>
                <member name="M:TestNamespace.TestType.Run(System.String)">
                  <summary>Run summary.</summary>
                  <param name="name">Name.</param>
                </member>
                <member name="P:TestNamespace.TestType.Name">
                  <summary>Name summary.</summary>
                </member>
              </members>
            </doc>
            """;
    }

    private static string BuildMinimalPowerShellHelpWithoutExamples()
    {
        return
            """
            <?xml version="1.0" encoding="utf-8"?>
            <helpItems schema="maml" xmlns="http://msh" xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10" xmlns:dev="http://schemas.microsoft.com/maml/dev/2004/10">
              <command:command>
                <command:details>
                  <command:name>Invoke-SampleFunction</command:name>
                  <command:commandType>Function</command:commandType>
                  <maml:description><maml:para>Invokes data.</maml:para></maml:description>
                </command:details>
                <command:syntax>
                  <command:syntaxItem>
                    <command:name>Invoke-SampleFunction</command:name>
                    <command:parameter required="true">
                      <maml:name>Name</maml:name>
                      <command:parameterValue required="true">string</command:parameterValue>
                    </command:parameter>
                  </command:syntaxItem>
                </command:syntax>
                <command:parameters>
                  <command:parameter required="true">
                    <maml:name>Name</maml:name>
                    <maml:description><maml:para>Name value.</maml:para></maml:description>
                    <command:parameterValue required="true">string</command:parameterValue>
                  </command:parameter>
                </command:parameters>
              </command:command>
            </helpItems>
            """;
    }

    private static string BuildMinimalPowerShellHelpWithoutExamples(string commandName)
        => BuildMinimalPowerShellHelpWithoutExamples()
            .Replace("Invoke-SampleFunction", commandName, StringComparison.Ordinal);

    private static string BuildMinimalPowerShellHelpForValidation()
    {
        return
            """
            <?xml version="1.0" encoding="utf-8"?>
            <helpItems schema="maml" xmlns="http://msh" xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10" xmlns:dev="http://schemas.microsoft.com/maml/dev/2004/10">
              <command:command>
                <command:details>
                  <command:name>Invoke-SampleFunction</command:name>
                  <command:commandType>Function</command:commandType>
                  <maml:description><maml:para>Invokes data.</maml:para></maml:description>
                </command:details>
                <command:syntax>
                  <command:syntaxItem>
                    <command:name>Invoke-SampleFunction</command:name>
                    <command:parameter required="true">
                      <maml:name>Name</maml:name>
                      <command:parameterValue required="true">string</command:parameterValue>
                    </command:parameter>
                  </command:syntaxItem>
                </command:syntax>
                <command:parameters>
                  <command:parameter required="true">
                    <maml:name>Name</maml:name>
                    <maml:description><maml:para>Name value.</maml:para></maml:description>
                    <command:parameterValue required="true">string</command:parameterValue>
                  </command:parameter>
                </command:parameters>
              </command:command>
            </helpItems>
            """;
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

    private static void WriteProjectApiPowerShellSource(
        string sourcesRoot,
        string slug,
        string helpFileName,
        string manifestFileName,
        string moduleFileName)
        => WriteProjectApiPowerShellSource(sourcesRoot, slug, helpFileName, manifestFileName, moduleFileName, "Invoke-SampleFunction");

    private static void WriteProjectApiPowerShellSource(
        string sourcesRoot,
        string slug,
        string helpFileName,
        string manifestFileName,
        string moduleFileName,
        string commandName)
    {
        var apiRoot = Path.Combine(sourcesRoot, slug, "WebsiteArtifacts", "apidocs", "powershell");
        Directory.CreateDirectory(apiRoot);
        File.WriteAllText(Path.Combine(apiRoot, helpFileName), BuildMinimalPowerShellHelpWithoutExamples(commandName));
        File.WriteAllText(Path.Combine(apiRoot, manifestFileName),
            $$"""
            @{
                CmdletsToExport = @()
                FunctionsToExport = @('{{commandName}}')
                AliasesToExport = @()
                RootModule = 'Sample.Module.psm1'
            }
            """);
        File.WriteAllText(Path.Combine(apiRoot, moduleFileName), $"function {commandName} {{ param([string]$Name) $Name }}");
    }

    private static bool IsGitAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("--version");
            using var process = Process.Start(psi);
            if (process is null)
                return false;

            process.WaitForExit(2000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void RunGit(string workingDirectory, params string[] args)
    {
        RunGit(workingDirectory, null, args);
    }

    private static void RunGit(string workingDirectory, IReadOnlyDictionary<string, string>? environment, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        if (environment is not null)
        {
            foreach (var pair in environment)
                psi.Environment[pair.Key] = pair.Value;
        }

        using var process = Process.Start(psi);
        if (process is null)
            throw new InvalidOperationException("Failed to start git.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(10000);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(" ", args)} failed: {stderr}{Environment.NewLine}{stdout}");
    }
}
