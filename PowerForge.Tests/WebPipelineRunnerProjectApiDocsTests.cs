using System;
using System.IO;
using PowerForge.Web.Cli;
using Xunit;

namespace PowerForge.Tests;

public class WebPipelineRunnerProjectApiDocsTests
{
    [Fact]
    public void RunPipeline_ProjectApiDocs_GeneratesProjectScopedPowerShellApi()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-apidocs-powershell-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            WriteCatalog(root,
                """
                {
                  "projects": [
                    {
                      "slug": "alpha",
                      "name": "Alpha",
                      "mode": "hub-full",
                      "surfaces": {
                        "apiPowerShell": true
                      }
                    }
                  ]
                }
                """);

            var powerShellRoot = Path.Combine(root, "data", "project-api", "alpha", "powershell");
            Directory.CreateDirectory(Path.Combine(powerShellRoot, "examples"));
            File.WriteAllText(Path.Combine(powerShellRoot, "Alpha-help.xml"), SamplePowerShellHelpXml("Get-AlphaItem", "Returns alpha items."));
            File.WriteAllText(Path.Combine(powerShellRoot, "examples", "Get-AlphaItem.ps1"), "Get-AlphaItem -Name 'Demo'");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "project-apidocs",
                      "catalog": "./data/projects/catalog.json",
                      "apiRoot": "./data/project-api",
                      "siteRoot": "./_site",
                      "summaryPath": "./summary.json",
                      "template": "docs",
                      "format": "html"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            var indexPath = Path.Combine(root, "_site", "projects", "alpha", "api", "index.html");
            Assert.True(File.Exists(indexPath));
            var html = File.ReadAllText(indexPath);
            Assert.Contains("Alpha API Reference", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Get-AlphaItem", html, StringComparison.OrdinalIgnoreCase);

            var summary = File.ReadAllText(Path.Combine(root, "summary.json"));
            Assert.Contains("\"generated\": 1", summary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectApiDocs_GeneratesProjectScopedDotNetApi()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-apidocs-dotnet-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            WriteCatalog(root,
                """
                {
                  "projects": [
                    {
                      "slug": "beta",
                      "name": "Beta",
                      "mode": "hub-full",
                      "surfaces": {
                        "apiDotNet": true
                      }
                    }
                  ]
                }
                """);

            var dotNetRoot = Path.Combine(root, "data", "project-api", "beta", "dotnet");
            Directory.CreateDirectory(dotNetRoot);
            var assemblyPath = typeof(WebPipelineRunnerProjectApiDocsTests).Assembly.Location;
            File.Copy(assemblyPath, Path.Combine(dotNetRoot, "PowerForge.Tests.dll"));
            File.WriteAllText(Path.Combine(dotNetRoot, "PowerForge.Tests.xml"),
                $"""
                <doc>
                  <assembly><name>PowerForge.Tests</name></assembly>
                  <members>
                    <member name="T:{typeof(WebPipelineRunnerProjectApiDocsTests).FullName}">
                      <summary>Project API docs test type.</summary>
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
                      "task": "project-apidocs",
                      "catalog": "./data/projects/catalog.json",
                      "apiRoot": "./data/project-api",
                      "siteRoot": "./_site",
                      "summaryPath": "./summary.json",
                      "template": "docs",
                      "format": "html",
                      "preferredMode": "dotnet"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            var indexPath = Path.Combine(root, "_site", "projects", "beta", "api", "index.html");
            Assert.True(File.Exists(indexPath));
            var html = File.ReadAllText(indexPath);
            Assert.Contains("Beta API Reference", html, StringComparison.OrdinalIgnoreCase);

            var summary = File.ReadAllText(Path.Combine(root, "summary.json"));
            Assert.Contains("\"generated\": 1", summary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectApiDocs_FailsWhenPlaceholderContentDetected()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-apidocs-placeholder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            WriteCatalog(root,
                """
                {
                  "projects": [
                    {
                      "slug": "gamma",
                      "name": "Gamma",
                      "mode": "hub-full",
                      "surfaces": {
                        "apiPowerShell": true
                      }
                    }
                  ]
                }
                """);

            var powerShellRoot = Path.Combine(root, "data", "project-api", "gamma", "powershell");
            Directory.CreateDirectory(powerShellRoot);
            File.WriteAllText(Path.Combine(powerShellRoot, "Gamma-help.xml"),
                SamplePowerShellHelpXml("Get-GammaItem", "{{ Fill in the Synopsis }}"));

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "project-apidocs",
                      "catalog": "./data/projects/catalog.json",
                      "apiRoot": "./data/project-api",
                      "siteRoot": "./_site",
                      "template": "docs",
                      "format": "html",
                      "failOnPlaceholderContent": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("placeholder content detected", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectApiDocs_CleanRemovesStaleOutputWhenSourceMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-apidocs-clean-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            WriteCatalog(root,
                """
                {
                  "projects": [
                    {
                      "slug": "delta",
                      "name": "Delta",
                      "mode": "hub-full",
                      "surfaces": {
                        "apiPowerShell": true
                      }
                    }
                  ]
                }
                """);

            var staleOutputPath = Path.Combine(root, "_site", "projects", "delta", "api");
            Directory.CreateDirectory(staleOutputPath);
            File.WriteAllText(Path.Combine(staleOutputPath, "index.html"), "<html>stale</html>");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "project-apidocs",
                      "catalog": "./data/projects/catalog.json",
                      "apiRoot": "./data/project-api",
                      "siteRoot": "./_site",
                      "template": "docs",
                      "format": "html",
                      "clean": true,
                      "failOnMissingSource": false
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            Assert.False(Directory.Exists(staleOutputPath));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectApiDocs_IncludesThemeCssHrefFromSiteContract()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-apidocs-theme-css-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            WriteCatalog(root,
                """
                {
                  "projects": [
                    {
                      "slug": "epsilon",
                      "name": "Epsilon",
                      "mode": "hub-full",
                      "surfaces": {
                        "apiPowerShell": true
                      }
                    }
                  ]
                }
                """);

            File.WriteAllText(Path.Combine(root, "site.json"),
                """
                {
                  "name": "Test Site",
                  "defaultTheme": "test-theme"
                }
                """);

            var themeRoot = Path.Combine(root, "themes", "test-theme");
            Directory.CreateDirectory(themeRoot);
            File.WriteAllText(Path.Combine(themeRoot, "theme.manifest.json"),
                """
                {
                  "name": "test-theme",
                  "schemaVersion": 2,
                  "contractVersion": 2,
                  "featureContracts": {
                    "apiDocs": {
                      "cssHrefs": ["/themes/test-theme/assets/app.css"]
                    }
                  }
                }
                """);

            var powerShellRoot = Path.Combine(root, "data", "project-api", "epsilon", "powershell");
            Directory.CreateDirectory(powerShellRoot);
            File.WriteAllText(Path.Combine(powerShellRoot, "Epsilon-help.xml"), SamplePowerShellHelpXml("Get-EpsilonItem", "Returns epsilon items."));

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "project-apidocs",
                      "catalog": "./data/projects/catalog.json",
                      "apiRoot": "./data/project-api",
                      "siteRoot": "./_site",
                      "template": "docs",
                      "format": "html"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            var indexPath = Path.Combine(root, "_site", "projects", "epsilon", "api", "index.html");
            Assert.True(File.Exists(indexPath));
            var html = File.ReadAllText(indexPath);
            Assert.Contains("/themes/test-theme/assets/app.css", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectApiDocs_ReplacesProjectTemplateTokensInCustomHeader()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-apidocs-template-tokens-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            WriteCatalog(root,
                """
                {
                  "projects": [
                    {
                      "slug": "zeta",
                      "name": "Zeta",
                      "description": "Zeta project description.",
                      "githubRepo": "EvotecIT/Zeta",
                      "mode": "hub-full",
                      "surfaces": {
                        "docs": true,
                        "apiPowerShell": true
                      }
                    }
                  ]
                }
                """);

            var fragmentsRoot = Path.Combine(root, "fragments");
            Directory.CreateDirectory(fragmentsRoot);
            File.WriteAllText(Path.Combine(fragmentsRoot, "header.html"),
                """
                <header class="test-header">
                  <h1>{{PROJECT_NAME}}</h1>
                  <p>{{PROJECT_DESCRIPTION}}</p>
                  <a href="{{PROJECT_OVERVIEW_URL}}">Overview</a>
                  <a href="{{PROJECT_DOCS_URL}}">Docs</a>
                  <a href="{{PROJECT_SOURCE_URL}}">Source</a>
                </header>
                """);
            File.WriteAllText(Path.Combine(fragmentsRoot, "footer.html"), "<footer>Footer</footer>");

            var powerShellRoot = Path.Combine(root, "data", "project-api", "zeta", "powershell");
            Directory.CreateDirectory(powerShellRoot);
            File.WriteAllText(Path.Combine(powerShellRoot, "Zeta-help.xml"), SamplePowerShellHelpXml("Get-ZetaItem", "Returns zeta items."));

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "project-apidocs",
                      "catalog": "./data/projects/catalog.json",
                      "apiRoot": "./data/project-api",
                      "siteRoot": "./_site",
                      "template": "docs",
                      "format": "html",
                      "headerHtml": "./fragments/header.html",
                      "footerHtml": "./fragments/footer.html"
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);

            Assert.True(result.Success);
            var indexPath = Path.Combine(root, "_site", "projects", "zeta", "api", "index.html");
            var html = File.ReadAllText(indexPath);
            Assert.Contains("Zeta project description.", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("href=\"/projects/zeta/\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("href=\"/projects/zeta/docs/\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("href=\"https://github.com/EvotecIT/Zeta\"", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static void WriteCatalog(string root, string content)
    {
        var catalogPath = Path.Combine(root, "data", "projects", "catalog.json");
        Directory.CreateDirectory(Path.GetDirectoryName(catalogPath)!);
        File.WriteAllText(catalogPath, content);
    }

    private static string SamplePowerShellHelpXml(string commandName, string summary)
    {
        return
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <helpItems schema="maml" xmlns="http://msh" xmlns:maml="http://schemas.microsoft.com/maml/2004/10" xmlns:command="http://schemas.microsoft.com/maml/dev/command/2004/10" xmlns:dev="http://schemas.microsoft.com/maml/dev/2004/10">
              <command:command>
                <command:details>
                  <command:name>{commandName}</command:name>
                  <maml:description>
                    <maml:para>{summary}</maml:para>
                  </maml:description>
                </command:details>
                <maml:description>
                  <maml:para>{summary}</maml:para>
                </maml:description>
                <command:syntax>
                  <command:syntaxItem>
                    <command:name>{commandName}</command:name>
                  </command:syntaxItem>
                </command:syntax>
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
}
