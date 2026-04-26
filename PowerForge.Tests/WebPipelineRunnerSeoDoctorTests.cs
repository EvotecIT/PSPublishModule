using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public class WebPipelineRunnerSeoDoctorTests
{
    [Fact]
    public void RunPipeline_SeoDoctor_CanGenerateBaselineAndFailOnNewIssues()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-seo-doctor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var siteRoot = Path.Combine(root, "_site");
            Directory.CreateDirectory(siteRoot);
            Directory.CreateDirectory(Path.Combine(siteRoot, "about"));

            File.WriteAllText(Path.Combine(siteRoot, "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>PowerForge SEO Doctor Home Page</title>
                  <meta name="description" content="This page is intentionally long enough to pass basic SEO doctor title and description checks in tests." />
                </head>
                <body>
                  <h1>Home</h1>
                  <a href="/about/">About</a>
                  <img src="/images/logo.png" alt="Logo" />
                </body>
                </html>
                """);

            File.WriteAllText(Path.Combine(siteRoot, "about", "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>PowerForge SEO Doctor About Page</title>
                  <meta name="description" content="This about page links back to home and should not create orphan warnings in the initial baseline." />
                </head>
                <body>
                  <h1>About</h1>
                  <a href="/">Home</a>
                </body>
                </html>
                """);

            var baselinePath = Path.Combine(root, ".powerforge", "seo-baseline.json");
            var pipelineBaseline = Path.Combine(root, "pipeline-baseline.json");
            File.WriteAllText(pipelineBaseline,
                """
                {
                  "steps": [
                    {
                      "task": "seo-doctor",
                      "siteRoot": "./_site",
                      "baseline": "./.powerforge/seo-baseline.json",
                      "baselineGenerate": true,
                      "reportPath": "./_reports/seo-doctor.json",
                      "summaryPath": "./_reports/seo-doctor.md",
                      "backlogSummaryPath": "./_reports/seo-backlog-summary.json",
                      "pageMetricsPath": "./_reports/seo-page-metrics.csv",
                      "issuesCsvPath": "./_reports/seo-issues.csv"
                    }
                  ]
                }
                """);

            var firstResult = WebPipelineRunner.RunPipeline(pipelineBaseline, logger: null);
            Assert.True(firstResult.Success);
            Assert.Single(firstResult.Steps);
            Assert.True(firstResult.Steps[0].Success);
            Assert.True(File.Exists(baselinePath));
            Assert.True(File.Exists(Path.Combine(root, "_reports", "seo-doctor.json")));
            Assert.True(File.Exists(Path.Combine(root, "_reports", "seo-doctor.md")));
            Assert.True(File.Exists(Path.Combine(root, "_reports", "seo-backlog-summary.json")));
            Assert.True(File.Exists(Path.Combine(root, "_reports", "seo-page-metrics.csv")));
            Assert.True(File.Exists(Path.Combine(root, "_reports", "seo-issues.csv")));

            Directory.CreateDirectory(Path.Combine(siteRoot, "orphan"));
            File.WriteAllText(Path.Combine(siteRoot, "orphan", "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>Orphan page for fail-on-new test</title>
                </head>
                <body>
                  <h1>Orphan</h1>
                </body>
                </html>
                """);

            var pipelineFailOnNew = Path.Combine(root, "pipeline-fail-on-new.json");
            File.WriteAllText(pipelineFailOnNew,
                """
                {
                  "steps": [
                    {
                      "task": "seo-doctor",
                      "siteRoot": "./_site",
                      "baseline": "./.powerforge/seo-baseline.json",
                      "failOnNew": true
                    }
                  ]
                }
                """);

            var secondResult = WebPipelineRunner.RunPipeline(pipelineFailOnNew, logger: null);
            Assert.False(secondResult.Success);
            Assert.Single(secondResult.Steps);
            Assert.False(secondResult.Steps[0].Success);
            Assert.Contains("new issues", secondResult.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_SeoDoctor_RequireCanonical_WithFailOnWarnings_Fails()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-seo-doctor-require-canonical-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var siteRoot = Path.Combine(root, "_site");
            Directory.CreateDirectory(siteRoot);
            File.WriteAllText(Path.Combine(siteRoot, "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>SEO Doctor Require Canonical</title>
                  <meta name="description" content="A page without canonical to verify requireCanonical behavior in pipeline tests." />
                </head>
                <body>
                  <h1>Home</h1>
                </body>
                </html>
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "seo-doctor",
                      "siteRoot": "./_site",
                      "checkCanonical": true,
                      "requireCanonical": true,
                      "checkHreflang": false,
                      "checkStructuredData": false,
                      "failOnWarnings": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("warnings", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_SeoDoctor_LocalizedBreakage_WithFailOnWarnings_Fails()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-seo-doctor-localized-breakage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var siteRoot = Path.Combine(root, "_site");
            Directory.CreateDirectory(Path.Combine(siteRoot, "kontakt"));
            Directory.CreateDirectory(Path.Combine(siteRoot, "en", "contact"));

            File.WriteAllText(Path.Combine(siteRoot, "kontakt", "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>Kontakt</title>
                  <link rel="canonical" href="https://pl.example.test/pl/kontakt/" />
                  <link rel="alternate" hreflang="pl" href="https://pl.example.test/pl/kontakt/" />
                  <link rel="alternate" hreflang="en" href="https://en.example.test/en/contact/" />
                </head>
                <body>
                  <h1>Kontakt</h1>
                </body>
                </html>
                """);

            File.WriteAllText(Path.Combine(siteRoot, "en", "contact", "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>Contact</title>
                  <link rel="canonical" href="https://en.example.test/en/contact/" />
                  <link rel="alternate" hreflang="en" href="https://en.example.test/en/contact/" />
                </head>
                <body>
                  <h1>Contact</h1>
                </body>
                </html>
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "seo-doctor",
                      "siteRoot": "./_site",
                      "checkTitleLength": false,
                      "checkDescriptionLength": false,
                      "checkH1": false,
                      "checkImageAlt": false,
                      "checkDuplicateTitles": false,
                      "checkOrphanPages": false,
                      "checkStructuredData": false,
                      "failOnWarnings": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.False(result.Success);
            Assert.Single(result.Steps);
            Assert.False(result.Steps[0].Success);
            Assert.Contains("warnings", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_SeoDoctor_ReferenceSiteRoots_AllowsCrossRootHreflangValidation()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-seo-doctor-reference-roots-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xyzRoot = Path.Combine(root, "_site-xyz");
            var plRoot = Path.Combine(root, "_site-pl");
            Directory.CreateDirectory(xyzRoot);
            Directory.CreateDirectory(plRoot);

            File.WriteAllText(Path.Combine(xyzRoot, "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>Home</title>
                  <link rel="canonical" href="https://evotec.xyz/" />
                  <link rel="alternate" hreflang="en" href="https://evotec.xyz/" />
                  <link rel="alternate" hreflang="pl" href="https://evotec.pl/" />
                  <link rel="alternate" hreflang="x-default" href="https://evotec.xyz/" />
                </head>
                <body><h1>Home</h1></body>
                </html>
                """);

            File.WriteAllText(Path.Combine(plRoot, "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>Start</title>
                  <link rel="canonical" href="https://evotec.pl/" />
                  <link rel="alternate" hreflang="pl" href="https://evotec.pl/" />
                  <link rel="alternate" hreflang="en" href="https://evotec.xyz/" />
                  <link rel="alternate" hreflang="x-default" href="https://evotec.xyz/" />
                </head>
                <body><h1>Start</h1></body>
                </html>
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "seo-doctor",
                      "siteRoot": "./_site-xyz",
                      "referenceSiteRoots": ["./_site-pl"],
                      "checkTitleLength": false,
                      "checkDescriptionLength": false,
                      "checkH1": false,
                      "checkImageAlt": false,
                      "checkDuplicateTitles": false,
                      "checkOrphanPages": false,
                      "checkStructuredData": false,
                      "failOnWarnings": true
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
    public void RunPipeline_SeoDoctor_SameSiteRootLookup_AllowsLocalizedTargetsOutsideIncludedSubset()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-seo-doctor-same-root-subset-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var xyzRoot = Path.Combine(root, "_site-xyz");
            var plRoot = Path.Combine(root, "_site-pl");
            Directory.CreateDirectory(Path.Combine(xyzRoot, "projects", "pswritehtml"));
            Directory.CreateDirectory(Path.Combine(plRoot, "projects", "pswritehtml"));
            Directory.CreateDirectory(Path.Combine(xyzRoot, "fr", "projects", "pswritehtml"));
            Directory.CreateDirectory(Path.Combine(xyzRoot, "de", "projects", "pswritehtml"));
            Directory.CreateDirectory(Path.Combine(xyzRoot, "es", "projects", "pswritehtml"));

            File.WriteAllText(Path.Combine(xyzRoot, "projects", "pswritehtml", "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>PSWriteHTML</title>
                  <link rel="canonical" href="https://evotec.xyz/projects/pswritehtml" />
                  <link rel="alternate" hreflang="en" href="https://evotec.xyz/projects/pswritehtml" />
                  <link rel="alternate" hreflang="pl" href="https://evotec.pl/projects/pswritehtml" />
                  <link rel="alternate" hreflang="fr" href="https://evotec.xyz/fr/projects/pswritehtml" />
                  <link rel="alternate" hreflang="de" href="https://evotec.xyz/de/projects/pswritehtml" />
                  <link rel="alternate" hreflang="es" href="https://evotec.xyz/es/projects/pswritehtml" />
                  <link rel="alternate" hreflang="x-default" href="https://evotec.xyz/projects/pswritehtml" />
                </head>
                <body><h1>PSWriteHTML</h1></body>
                </html>
                """);

            File.WriteAllText(Path.Combine(plRoot, "projects", "pswritehtml", "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>PSWriteHTML PL</title>
                  <meta name="robots" content="noindex,follow" />
                  <link rel="canonical" href="https://evotec.pl/projects/pswritehtml" />
                  <link rel="alternate" hreflang="pl" href="https://evotec.pl/projects/pswritehtml" />
                  <link rel="alternate" hreflang="en" href="https://evotec.xyz/projects/pswritehtml" />
                  <link rel="alternate" hreflang="fr" href="https://evotec.xyz/fr/projects/pswritehtml" />
                  <link rel="alternate" hreflang="de" href="https://evotec.xyz/de/projects/pswritehtml" />
                  <link rel="alternate" hreflang="es" href="https://evotec.xyz/es/projects/pswritehtml" />
                  <link rel="alternate" hreflang="x-default" href="https://evotec.xyz/projects/pswritehtml" />
                </head>
                <body><h1>PSWriteHTML PL</h1></body>
                </html>
                """);

            File.WriteAllText(Path.Combine(xyzRoot, "fr", "projects", "pswritehtml", "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>PSWriteHTML FR</title>
                  <meta name="robots" content="noindex,follow" />
                  <link rel="canonical" href="https://evotec.xyz/fr/projects/pswritehtml" />
                  <link rel="alternate" hreflang="fr" href="https://evotec.xyz/fr/projects/pswritehtml" />
                  <link rel="alternate" hreflang="en" href="https://evotec.xyz/projects/pswritehtml" />
                  <link rel="alternate" hreflang="pl" href="https://evotec.pl/projects/pswritehtml" />
                  <link rel="alternate" hreflang="de" href="https://evotec.xyz/de/projects/pswritehtml" />
                  <link rel="alternate" hreflang="es" href="https://evotec.xyz/es/projects/pswritehtml" />
                  <link rel="alternate" hreflang="x-default" href="https://evotec.xyz/projects/pswritehtml" />
                </head>
                <body><h1>PSWriteHTML FR</h1></body>
                </html>
                """);

            File.WriteAllText(Path.Combine(xyzRoot, "de", "projects", "pswritehtml", "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>PSWriteHTML DE</title>
                  <meta name="robots" content="noindex,follow" />
                  <link rel="canonical" href="https://evotec.xyz/de/projects/pswritehtml" />
                  <link rel="alternate" hreflang="de" href="https://evotec.xyz/de/projects/pswritehtml" />
                  <link rel="alternate" hreflang="en" href="https://evotec.xyz/projects/pswritehtml" />
                  <link rel="alternate" hreflang="pl" href="https://evotec.pl/projects/pswritehtml" />
                  <link rel="alternate" hreflang="fr" href="https://evotec.xyz/fr/projects/pswritehtml" />
                  <link rel="alternate" hreflang="es" href="https://evotec.xyz/es/projects/pswritehtml" />
                  <link rel="alternate" hreflang="x-default" href="https://evotec.xyz/projects/pswritehtml" />
                </head>
                <body><h1>PSWriteHTML DE</h1></body>
                </html>
                """);

            File.WriteAllText(Path.Combine(xyzRoot, "es", "projects", "pswritehtml", "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>PSWriteHTML ES</title>
                  <meta name="robots" content="noindex,follow" />
                  <link rel="canonical" href="https://evotec.xyz/es/projects/pswritehtml" />
                  <link rel="alternate" hreflang="es" href="https://evotec.xyz/es/projects/pswritehtml" />
                  <link rel="alternate" hreflang="en" href="https://evotec.xyz/projects/pswritehtml" />
                  <link rel="alternate" hreflang="pl" href="https://evotec.pl/projects/pswritehtml" />
                  <link rel="alternate" hreflang="fr" href="https://evotec.xyz/fr/projects/pswritehtml" />
                  <link rel="alternate" hreflang="de" href="https://evotec.xyz/de/projects/pswritehtml" />
                  <link rel="alternate" hreflang="x-default" href="https://evotec.xyz/projects/pswritehtml" />
                </head>
                <body><h1>PSWriteHTML ES</h1></body>
                </html>
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "seo-doctor",
                      "siteRoot": "./_site-xyz",
                      "referenceSiteRoots": ["./_site-pl"],
                      "include": "projects/**",
                      "checkTitleLength": false,
                      "checkDescriptionLength": false,
                      "checkH1": false,
                      "checkImageAlt": false,
                      "checkDuplicateTitles": false,
                      "checkOrphanPages": false,
                      "checkStructuredData": false,
                      "failOnWarnings": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success, result.Steps.FirstOrDefault()?.Message);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success, result.Steps[0].Message);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_SeoDoctor_ConfigLanguageRootHosts_AllowsSingleTreeRootServedLanguageValidation()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-seo-doctor-root-host-config-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var siteRoot = Path.Combine(root, "_site");
            Directory.CreateDirectory(Path.Combine(siteRoot, "projects"));
            Directory.CreateDirectory(Path.Combine(siteRoot, "pl", "projekty"));

            File.WriteAllText(Path.Combine(root, "site.json"),
                """
                {
                  "name": "Evotec",
                  "baseUrl": "https://evotec.xyz",
                  "links": {
                    "languageRootHosts": {
                      "evotec.pl": "pl"
                    }
                  },
                  "localization": {
                    "enabled": true,
                    "defaultLanguage": "en",
                    "languages": [
                      { "code": "en", "default": true, "baseUrl": "https://evotec.xyz", "renderAtRoot": true },
                      { "code": "pl", "baseUrl": "https://evotec.pl", "renderAtRoot": true }
                    ]
                  }
                }
                """);

            File.WriteAllText(Path.Combine(siteRoot, "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>Home</title>
                  <link rel="canonical" href="https://evotec.xyz/" />
                  <link rel="alternate" hreflang="en" href="https://evotec.xyz/" />
                  <link rel="alternate" hreflang="pl" href="https://evotec.pl/" />
                  <link rel="alternate" hreflang="x-default" href="https://evotec.xyz/" />
                </head>
                <body><h1>Home</h1></body>
                </html>
                """);

            File.WriteAllText(Path.Combine(siteRoot, "pl", "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>Start</title>
                  <link rel="canonical" href="https://evotec.pl/" />
                  <link rel="alternate" hreflang="en" href="https://evotec.xyz/" />
                  <link rel="alternate" hreflang="pl" href="https://evotec.pl/" />
                  <link rel="alternate" hreflang="x-default" href="https://evotec.xyz/" />
                </head>
                <body><h1>Start</h1></body>
                </html>
                """);

            File.WriteAllText(Path.Combine(siteRoot, "projects", "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>Projects</title>
                  <link rel="canonical" href="https://evotec.xyz/projects" />
                  <link rel="alternate" hreflang="en" href="https://evotec.xyz/projects" />
                  <link rel="alternate" hreflang="pl" href="https://evotec.pl/projekty" />
                  <link rel="alternate" hreflang="x-default" href="https://evotec.xyz/projects" />
                </head>
                <body><h1>Projects</h1></body>
                </html>
                """);

            File.WriteAllText(Path.Combine(siteRoot, "pl", "projekty", "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>Projekty</title>
                  <link rel="canonical" href="https://evotec.pl/projekty" />
                  <link rel="alternate" hreflang="en" href="https://evotec.xyz/projects" />
                  <link rel="alternate" hreflang="pl" href="https://evotec.pl/projekty" />
                  <link rel="alternate" hreflang="x-default" href="https://evotec.xyz/projects" />
                </head>
                <body><h1>Projekty</h1></body>
                </html>
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "seo-doctor",
                      "config": "./site.json",
                      "siteRoot": "./_site",
                      "checkTitleLength": false,
                      "checkDescriptionLength": false,
                      "checkH1": false,
                      "checkImageAlt": false,
                      "checkDuplicateTitles": false,
                      "checkOrphanPages": false,
                      "checkStructuredData": false,
                      "failOnWarnings": true
                    }
                  ]
                }
                """);

            var result = WebPipelineRunner.RunPipeline(pipelinePath, logger: null);
            Assert.True(result.Success, result.Steps.FirstOrDefault()?.Message);
            Assert.Single(result.Steps);
            Assert.True(result.Steps[0].Success, result.Steps[0].Message);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void RunPipeline_SeoDoctor_PageAssertions_FailOnLocalizedLeakAndMissingText()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-pipeline-seo-doctor-page-assertions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var siteRoot = Path.Combine(root, "_site");
            Directory.CreateDirectory(Path.Combine(siteRoot, "fr", "contact"));

            File.WriteAllText(Path.Combine(siteRoot, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Home</title></head>
                <body><h1>Home</h1></body>
                </html>
                """);

            File.WriteAllText(Path.Combine(siteRoot, "fr", "contact", "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Contact</title></head>
                <body>
                  <main>
                    <article>
                      <h1>Contact</h1>
                      <p>translation_key: "contact" meta.raw_html: true</p>
                    </article>
                  </main>
                </body>
                </html>
                """);

            var pipelinePath = Path.Combine(root, "pipeline.json");
            File.WriteAllText(pipelinePath,
                """
                {
                  "steps": [
                    {
                      "task": "seo-doctor",
                      "siteRoot": "./_site",
                      "maxHtmlFiles": 1,
                      "checkTitleLength": false,
                      "checkDescriptionLength": false,
                      "checkH1": false,
                      "checkImageAlt": false,
                      "checkDuplicateTitles": false,
                      "checkOrphanPages": false,
                      "checkCanonical": false,
                      "checkHreflang": false,
                      "checkStructuredData": false,
                      "checkContentLeaks": false,
                      "pageAssertions": [
                        {
                          "path": "/fr/contact/",
                          "label": "French contact",
                          "contains": ["Contactez-nous"],
                          "notContains": ["translation_key:", "meta.raw_html: true"]
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
            Assert.Contains("errors", result.Steps[0].Message, StringComparison.OrdinalIgnoreCase);
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
