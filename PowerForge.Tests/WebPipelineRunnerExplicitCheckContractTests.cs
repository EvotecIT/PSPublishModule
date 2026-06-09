using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public class WebPipelineRunnerExplicitCheckContractTests
{
    [Fact]
    public void RunPipeline_Audit_RequireExplicitChecks_FailsWhenMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-audit-explicit-checks-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            CreateSiteOutputFixture(root);
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "audit",
                      "siteRoot": "./_site",
                      "requireExplicitChecks": true,
                      "checkSeoMeta": false
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("requireExplicitChecks", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("checkNetworkHints", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_Audit_RequireExplicitChecks_PassesWhenExplicit()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-audit-explicit-checks-ok-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            CreateSiteOutputFixture(root);
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "audit",
                      "siteRoot": "./_site",
                      "requireExplicitChecks": true,
                      "checkSeoMeta": false,
                      "checkNetworkHints": true,
                      "checkRenderBlockingResources": true,
                      "checkHeadingOrder": true,
                      "checkLinkPurposeConsistency": true,
                      "checkMediaEmbeds": true
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
    public void RunPipeline_Audit_RequireExplicitChecks_AliasWorks()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-audit-explicit-checks-alias-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            CreateSiteOutputFixture(root);
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "audit",
                      "siteRoot": "./_site",
                      "require-explicit-checks": true,
                      "checkSeoMeta": false,
                      "checkNetworkHints": true,
                      "checkRenderBlockingResources": true,
                      "checkHeadingOrder": true,
                      "checkLinkPurposeConsistency": true,
                      "checkMediaEmbeds": true
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
    public void RunPipeline_Doctor_RequireExplicitChecks_FailsWhenMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-doctor-explicit-checks-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            CreateDoctorFixture(root);
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "doctor",
                      "config": "./site.json",
                      "siteRoot": "./_site",
                      "noBuild": true,
                      "noVerify": true,
                      "requireExplicitChecks": true,
                      "checkSeoMeta": false
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("requireExplicitChecks", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("checkNetworkHints", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_Doctor_RequireExplicitChecks_PassesWhenExplicit()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-doctor-explicit-checks-ok-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            CreateDoctorFixture(root);
            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "doctor",
                      "config": "./site.json",
                      "siteRoot": "./_site",
                      "noBuild": true,
                      "noVerify": true,
                      "requireExplicitChecks": true,
                      "checkSeoMeta": false,
                      "checkNetworkHints": true,
                      "checkRenderBlockingResources": true,
                      "checkHeadingOrder": true,
                      "checkLinkPurposeConsistency": true,
                      "checkMediaEmbeds": true
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

    private static void CreateSiteOutputFixture(string root)
    {
        var siteRoot = Path.Combine(root, "_site");
        Directory.CreateDirectory(siteRoot);
        File.WriteAllText(Path.Combine(siteRoot, "index.html"),
            """
            <!doctype html>
            <html>
            <head>
              <title>Audit Explicit Check Contract</title>
            </head>
            <body>
              <h1>Home</h1>
            </body>
            </html>
            """);
        File.WriteAllText(Path.Combine(siteRoot, "404.html"),
            """
            <!doctype html>
            <html>
            <head>
              <title>Not Found</title>
            </head>
            <body>
              <h1>404</h1>
            </body>
            </html>
            """);
    }

    private static void CreateDoctorFixture(string root)
    {
        CreateSiteOutputFixture(root);

        var pagesPath = Path.Combine(root, "content", "pages");
        Directory.CreateDirectory(pagesPath);
        File.WriteAllText(Path.Combine(pagesPath, "index.md"),
            """
            ---
            title: Home
            slug: index
            ---

            Home
            """);

        var themeRoot = Path.Combine(root, "themes", "doctor-contract-theme");
        Directory.CreateDirectory(Path.Combine(themeRoot, "layouts"));
        File.WriteAllText(Path.Combine(themeRoot, "layouts", "home.html"),
            """
            <!doctype html>
            <html><body>{{ content }}</body></html>
            """);
        File.WriteAllText(Path.Combine(themeRoot, "theme.json"),
            """
            {
              "name": "doctor-contract-theme",
              "engine": "scriban",
              "defaultLayout": "home"
            }
            """);

        File.WriteAllText(Path.Combine(root, "site.json"),
            """
            {
              "Name": "Doctor Contract Test",
              "BaseUrl": "https://example.test",
              "ContentRoot": "content",
              "DefaultTheme": "doctor-contract-theme",
              "ThemesRoot": "themes",
              "Collections": [
                { "Name": "pages", "Input": "content/pages", "Output": "/" }
              ]
            }
            """);
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
