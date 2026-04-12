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
                      "manifestGeneratedAt": "2026-03-07T09:39:12.7138991+01:00",
                      "manifestCommit": "da741ca2a12e30ec1dff8875182eaa5782534b13",
                      "metrics": {
                        "github": {
                          "language": "PowerShell",
                          "stars": 321,
                          "forks": 12,
                          "openIssues": 4,
                          "lastPushedAt": "2026-02-14T21:19:34.0000000+00:00"
                        },
                        "powerShellGallery": {
                          "totalDownloads": 1234567,
                          "galleryUrl": "https://example.test/gallery/alpha"
                        },
                        "downloads": {
                          "total": 1234567,
                          "powerShellGallery": 1234567,
                          "nuget": 0
                        },
                        "release": {
                          "latestTag": "v1.2.3"
                        }
                      },
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
            var headerPath = Path.Combine(root, "api-header.html");
            var footerPath = Path.Combine(root, "api-footer.html");
            File.WriteAllText(headerPath, "<header>{{PROJECT_DOWNLOADS_LABEL}} {{PROJECT_DOWNLOADS}} Updated: {{PROJECT_LAST_PUSH}}</header>");
            File.WriteAllText(footerPath, "<footer>{{YEAR}}</footer>");

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
                      "headerHtml": "./api-header.html",
                      "footerHtml": "./api-footer.html"
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
            Assert.Contains("PowerShell Gallery downloads", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("1,234,567", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Updated: 2026-02-14", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Manifest commit:", html, StringComparison.OrdinalIgnoreCase);

            var summary = File.ReadAllText(Path.Combine(root, "summary.json"));
            Assert.Contains("\"generated\": 1", summary, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectApiDocs_UsesPowerShellCommandMetadataForFunctionKindsAndAliases()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-apidocs-powershell-metadata-" + Guid.NewGuid().ToString("N"));
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
            Directory.CreateDirectory(powerShellRoot);
            File.WriteAllText(Path.Combine(powerShellRoot, "Alpha-help.xml"), SamplePowerShellHelpXml("Add-HTML", "Adds alpha html."));
            File.WriteAllText(Path.Combine(powerShellRoot, "command-metadata.json"),
                """
                {
                  "commands": [
                    {
                      "name": "Add-HTML",
                      "kind": "Function",
                      "aliases": [ "EmailHTML" ]
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
            var indexPath = Path.Combine(root, "_site", "projects", "alpha", "api", "index.html");
            Assert.True(File.Exists(indexPath));
            var indexHtml = File.ReadAllText(indexPath);
            Assert.Contains("Functions (1)", indexHtml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Cmdlets (1)", indexHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("EmailHTML", indexHtml, StringComparison.Ordinal);
            Assert.Contains("Aliases: EmailHTML", indexHtml, StringComparison.Ordinal);

            var detailHtml = File.ReadAllText(Path.Combine(root, "_site", "projects", "alpha", "api", "add-html", "index.html"));
            Assert.Contains("Aliases", detailHtml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("EmailHTML", detailHtml, StringComparison.Ordinal);
            Assert.Contains("type-header-aliases", detailHtml, StringComparison.Ordinal);
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

    [Fact]
    public void RunPipeline_ProjectApiDocs_ReplacesGenericArtifactDescription()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-apidocs-generic-description-" + Guid.NewGuid().ToString("N"));
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
                      "description": "Zeta website artifacts for the Evotec multi-project hub.",
                      "githubRepo": "EvotecIT/Zeta",
                      "mode": "hub-full",
                      "surfaces": {
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
                <header>
                  <p>{{PROJECT_DESCRIPTION}}</p>
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
            var html = File.ReadAllText(Path.Combine(root, "_site", "projects", "zeta", "api", "index.html"));
            Assert.Contains("Zeta is an open-source PowerShell project with packages, release history, and working documentation.", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("website artifacts for the Evotec multi-project hub", html, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_ProjectApiDocs_UsesLocalizedMenuWhenLanguageBuildSelectsRootNavigation()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-project-apidocs-localized-nav-" + Guid.NewGuid().ToString("N"));
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
            Directory.CreateDirectory(powerShellRoot);
            File.WriteAllText(Path.Combine(powerShellRoot, "Alpha-help.xml"), SamplePowerShellHelpXml("Get-AlphaItem", "Returns alpha items."));

            var navPath = Path.Combine(root, "_site-pl", "data", "site-nav.json");
            Directory.CreateDirectory(Path.GetDirectoryName(navPath)!);
            File.WriteAllText(navPath,
                """
                {
                  "schemaVersion": 2,
                  "format": "powerforge.site-nav",
                  "menus": {
                    "main": [
                      { "href": "/", "text": "Home" },
                      { "href": "/contact/", "text": "Contact" }
                    ],
                    "main-pl": [
                      { "href": "/", "text": "Start" },
                      { "href": "/kontakt/", "text": "Kontakt" }
                    ]
                  },
                  "menuModels": [
                    {
                      "name": "main",
                      "items": [
                        { "href": "/", "text": "Home" },
                        { "href": "/contact/", "text": "Contact" }
                      ]
                    },
                    {
                      "name": "main-pl",
                      "items": [
                        { "href": "/", "text": "Start" },
                        { "href": "/kontakt/", "text": "Kontakt" }
                      ]
                    }
                  ],
                  "surfaces": {
                    "main": {
                      "name": "main",
                      "primaryMenu": "main",
                      "primary": [
                        { "href": "/", "text": "Home" },
                        { "href": "/contact/", "text": "Contact" }
                      ]
                    }
                  }
                }
                """);

            var fragmentsRoot = Path.Combine(root, "fragments");
            Directory.CreateDirectory(fragmentsRoot);
            File.WriteAllText(Path.Combine(fragmentsRoot, "header.html"), "<header><nav>{{NAV_LINKS}}</nav></header>");
            File.WriteAllText(Path.Combine(fragmentsRoot, "footer.html"), "<footer>Footer</footer>");

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "project-apidocs",
                      "catalog": "./data/projects/catalog.json",
                      "apiRoot": "./data/project-api",
                      "siteRoot": "./_site-pl",
                      "nav": "./_site-pl/data/site-nav.json",
                      "language": "pl",
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
            var html = File.ReadAllText(Path.Combine(root, "_site-pl", "projects", "alpha", "api", "index.html"));
            Assert.Contains("href=\"/kontakt/\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(">Kontakt</a>", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("href=\"/contact/\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(">Contact</a>", html, StringComparison.OrdinalIgnoreCase);
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
