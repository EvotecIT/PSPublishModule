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
            Assert.All(artifactPaths, path => Assert.True(File.Exists(path)));
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
