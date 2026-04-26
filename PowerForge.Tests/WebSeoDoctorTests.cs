using PowerForge.Web;

namespace PowerForge.Tests;

public class WebSeoDoctorTests
{
    [Fact]
    public void Analyze_FlagsExpectedIssues_AndSkipsNoIndexByDefault()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-seo-doctor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "linked"));
            Directory.CreateDirectory(Path.Combine(root, "orphan"));
            Directory.CreateDirectory(Path.Combine(root, "search"));

            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>Home</title>
                </head>
                <body>
                  <h2>Welcome</h2>
                  <img src="/images/hero.png" />
                  <a href="/linked/">Linked page</a>
                </body>
                </html>
                """);

            File.WriteAllText(Path.Combine(root, "linked", "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>Home</title>
                  <meta name="description" content="This page exists to validate duplicate title and link graph behavior in SEO doctor tests." />
                </head>
                <body>
                  <h1>Linked</h1>
                </body>
                </html>
                """);

            File.WriteAllText(Path.Combine(root, "orphan", "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>Orphan page title for SEO doctor checks</title>
                  <meta name="description" content="This orphan page has no inbound links from scanned pages and should be reported." />
                </head>
                <body>
                  <h1>Orphan</h1>
                </body>
                </html>
                """);

            File.WriteAllText(Path.Combine(root, "search", "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <meta name="robots" content="noindex,nofollow" />
                </head>
                <body>Noindex page</body>
                </html>
                """);

            var result = WebSeoDoctor.Analyze(new WebSeoDoctorOptions
            {
                SiteRoot = root
            });

            Assert.True(result.Success);
            Assert.Contains(result.Issues, issue => issue.Hint == "title-short" && issue.Path == "index.html");
            Assert.Contains(result.Issues, issue => issue.Hint == "description-missing" && issue.Path == "index.html");
            Assert.Contains(result.Issues, issue => issue.Hint == "h1-missing" && issue.Path == "index.html");
            Assert.Contains(result.Issues, issue => issue.Hint == "image-alt-missing" && issue.Path == "index.html");
            Assert.Contains(result.Issues, issue => issue.Hint == "duplicate-title-intent");
            Assert.Contains(result.Issues, issue => issue.Hint == "orphan-page" && issue.Path == "/orphan/");
            Assert.DoesNotContain(result.Issues, issue => issue.Path == "search/index.html");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Analyze_BacklogMetrics_FlagsEmptyAltAndSourceMarkdown()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-seo-doctor-backlog-" + Guid.NewGuid().ToString("N"));
        var siteRoot = Path.Combine(root, "_site");
        var contentRoot = Path.Combine(root, "content");
        Directory.CreateDirectory(siteRoot);
        Directory.CreateDirectory(contentRoot);

        try
        {
            File.WriteAllText(Path.Combine(siteRoot, "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>PowerForge SEO Doctor Backlog Page</title>
                  <meta name="description" content="This page exists to validate native SEO backlog metrics for empty image alt text." />
                </head>
                <body>
                  <h1>Backlog</h1>
                  <img src="/images/decorative.svg" alt="" />
                </body>
                </html>
                """);

            File.WriteAllText(Path.Combine(contentRoot, "post.md"),
                """
                # Post

                ![](/images/source.png)
                """);

            var result = WebSeoDoctor.Analyze(new WebSeoDoctorOptions
            {
                SiteRoot = siteRoot,
                ContentRoot = contentRoot,
                CheckEmptyImageAlt = true,
                CheckSourceMarkdownImageAlt = true,
                CheckDuplicateTitles = false,
                CheckOrphanPages = false,
                CheckCanonical = false,
                CheckHreflang = false,
                CheckStructuredData = false
            });

            Assert.Contains(result.Issues, issue => issue.Hint == "image-alt-empty" && issue.Path == "index.html");
            Assert.Contains(result.Issues, issue => issue.Hint == "source-image-alt-empty" && issue.Path == "/source/post.md");
            Assert.Equal(1, result.PagesWithEmptyAlt);
            Assert.Equal(1, result.TotalEmptyAlt);
            Assert.Equal(1, result.SourceMarkdownFilesWithEmptyAlt);
            Assert.Equal(1, result.TotalSourceMarkdownEmptyAlt);
            Assert.Single(result.PageMetrics);
            Assert.Single(result.SourceMarkdownMetrics);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Analyze_SourceMarkdownEmptyAlt_ReportsLineStarts()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-seo-doctor-source-lines-" + Guid.NewGuid().ToString("N"));
        var siteRoot = Path.Combine(root, "_site");
        var contentRoot = Path.Combine(root, "content");
        Directory.CreateDirectory(siteRoot);
        Directory.CreateDirectory(contentRoot);

        try
        {
            File.WriteAllText(Path.Combine(siteRoot, "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>PowerForge SEO Doctor Source Lines</title>
                  <meta name="description" content="This page exists to keep SEO doctor source line tests focused." />
                </head>
                <body><h1>Source lines</h1></body>
                </html>
                """);

            File.WriteAllText(Path.Combine(contentRoot, "lines.md"),
                """
                ![](/images/first.png)
                Text between images.
                ![](/images/third.png)
                """);

            var result = WebSeoDoctor.Analyze(new WebSeoDoctorOptions
            {
                SiteRoot = siteRoot,
                ContentRoot = contentRoot,
                CheckEmptyImageAlt = true,
                CheckSourceMarkdownImageAlt = true,
                CheckDuplicateTitles = false,
                CheckOrphanPages = false,
                CheckCanonical = false,
                CheckHreflang = false,
                CheckStructuredData = false
            });

            var metric = Assert.Single(result.SourceMarkdownMetrics);
            Assert.Equal("lines.md", metric.Path);
            Assert.Equal(2, metric.EmptyMarkdownAltCount);
            Assert.Equal("1; 3", metric.SampleLineNumbers);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Analyze_FocusKeyphrase_FlagsTitleAndBodyCoverage()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-seo-doctor-focus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>SEO checks for docs</title>
                  <meta name="description" content="A page for keyphrase validation in tests." />
                  <meta name="pf:focus-keyphrase" content="powerforge web" />
                </head>
                <body>
                  <h1>Docs</h1>
                  <p>This body mentions powerforge web once.</p>
                </body>
                </html>
                """);

            var result = WebSeoDoctor.Analyze(new WebSeoDoctorOptions
            {
                SiteRoot = root,
                CheckTitleLength = false,
                CheckDescriptionLength = false,
                CheckH1 = false,
                CheckImageAlt = false,
                CheckDuplicateTitles = false,
                CheckOrphanPages = false,
                CheckFocusKeyphrase = true,
                MinFocusKeyphraseMentions = 2
            });

            Assert.Contains(result.Issues, issue => issue.Hint == "focus-keyphrase-title" && issue.Path == "index.html");
            Assert.Contains(result.Issues, issue => issue.Hint == "focus-keyphrase-body" && issue.Path == "index.html");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Analyze_CanonicalHreflangAndStructuredData_FlagsExpectedIssues()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-seo-doctor-technical-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>Technical SEO test page</title>
                  <link rel="canonical" href="/docs/" />
                  <link rel="canonical" href="https://example.com/docs/" />
                  <link rel="alternate" hreflang="en" href="/en/" />
                  <link rel="alternate" hreflang="en" href="https://example.com/en/" />
                  <link rel="alternate" hreflang="bad_value" href="https://example.com/bad/" />
                  <link rel="alternate" hreflang="x-default" href="https://example.com/" />
                  <script type="application/ld+json">{ invalid }</script>
                  <script type="application/ld+json">{"@context":"https://schema.org"}</script>
                  <script type="application/ld+json">{"@type":"Article"}</script>
                </head>
                <body>
                  <h1>Docs</h1>
                </body>
                </html>
                """);

            var result = WebSeoDoctor.Analyze(new WebSeoDoctorOptions
            {
                SiteRoot = root,
                CheckTitleLength = false,
                CheckDescriptionLength = false,
                CheckH1 = false,
                CheckImageAlt = false,
                CheckDuplicateTitles = false,
                CheckOrphanPages = false,
                CheckCanonical = true,
                CheckHreflang = true,
                CheckStructuredData = true
            });

            Assert.Contains(result.Issues, issue => issue.Hint == "canonical-duplicate" && issue.Path == "index.html");
            Assert.Contains(result.Issues, issue => issue.Hint == "canonical-absolute" && issue.Path == "index.html");
            Assert.Contains(result.Issues, issue => issue.Hint == "hreflang-duplicate" && issue.Path == "index.html");
            Assert.Contains(result.Issues, issue => issue.Hint == "hreflang-absolute" && issue.Path == "index.html");
            Assert.Contains(result.Issues, issue => issue.Hint == "hreflang-invalid" && issue.Path == "index.html");
            Assert.Contains(result.Issues, issue => issue.Hint == "structured-data-json-invalid" && issue.Path == "index.html");
            Assert.Contains(result.Issues, issue => issue.Hint == "structured-data-missing-context" && issue.Path == "index.html");
            Assert.Contains(result.Issues, issue => issue.Hint == "structured-data-missing-type" && issue.Path == "index.html");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Analyze_StructuredDataProfiles_FlagsMissingProfileFields()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-seo-doctor-profiles-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>Structured profile checks</title>
                  <script type="application/ld+json">{"@context":"https://schema.org","@type":"FAQPage"}</script>
                  <script type="application/ld+json">{"@context":"https://schema.org","@type":"HowTo","name":"Publish module"}</script>
                  <script type="application/ld+json">{"@context":"https://schema.org","@type":"NewsArticle","headline":"Release","author":{"@type":"Organization","name":"Evotec"},"publisher":{"@type":"Organization","name":"Evotec"}}</script>
                  <script type="application/ld+json">{"@context":"https://schema.org","@type":"Product"}</script>
                  <script type="application/ld+json">{"@context":"https://schema.org","@type":"SoftwareApplication"}</script>
                </head>
                <body>
                  <h1>Structured profile checks</h1>
                </body>
                </html>
                """);

            var result = WebSeoDoctor.Analyze(new WebSeoDoctorOptions
            {
                SiteRoot = root,
                CheckTitleLength = false,
                CheckDescriptionLength = false,
                CheckH1 = false,
                CheckImageAlt = false,
                CheckDuplicateTitles = false,
                CheckOrphanPages = false,
                CheckStructuredData = true
            });

            Assert.Contains(result.Issues, issue => issue.Hint == "structured-data-faq-main-entity" && issue.Path == "index.html");
            Assert.Contains(result.Issues, issue => issue.Hint == "structured-data-howto-step" && issue.Path == "index.html");
            Assert.Contains(result.Issues, issue => issue.Hint == "structured-data-news-date-published" && issue.Path == "index.html");
            Assert.Contains(result.Issues, issue => issue.Hint == "structured-data-product-name" && issue.Path == "index.html");
            Assert.Contains(result.Issues, issue => issue.Hint == "structured-data-software-name" && issue.Path == "index.html");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Analyze_StructuredData_FlagsOversizedJsonLdPayload()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-seo-doctor-jsonld-large-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var oversizedPayload = new string('x', 1_000_100);
            File.WriteAllText(Path.Combine(root, "index.html"),
                $$"""
                <!doctype html>
                <html>
                <head>
                  <title>Large JSON-LD</title>
                  <script type="application/ld+json">{"@context":"https://schema.org","@type":"Article","headline":"{{oversizedPayload}}"}</script>
                </head>
                <body>
                  <h1>Large JSON-LD</h1>
                </body>
                </html>
                """);

            var result = WebSeoDoctor.Analyze(new WebSeoDoctorOptions
            {
                SiteRoot = root,
                CheckTitleLength = false,
                CheckDescriptionLength = false,
                CheckH1 = false,
                CheckImageAlt = false,
                CheckDuplicateTitles = false,
                CheckOrphanPages = false,
                CheckStructuredData = true
            });

            Assert.Contains(result.Issues, issue => issue.Hint == "structured-data-payload-too-large" && issue.Path == "index.html");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Analyze_RequireFlags_FlagsMissingCanonicalHreflangAndStructuredData()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-seo-doctor-required-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>Required checks</title>
                </head>
                <body>
                  <h1>Required checks</h1>
                </body>
                </html>
                """);

            var result = WebSeoDoctor.Analyze(new WebSeoDoctorOptions
            {
                SiteRoot = root,
                CheckTitleLength = false,
                CheckDescriptionLength = false,
                CheckH1 = false,
                CheckImageAlt = false,
                CheckDuplicateTitles = false,
                CheckOrphanPages = false,
                CheckCanonical = true,
                CheckHreflang = true,
                CheckStructuredData = true,
                RequireCanonical = true,
                RequireHreflang = true,
                RequireStructuredData = true
            });

            Assert.Contains(result.Issues, issue => issue.Hint == "canonical-missing" && issue.Path == "index.html");
            Assert.Contains(result.Issues, issue => issue.Hint == "hreflang-missing" && issue.Path == "index.html");
            Assert.Contains(result.Issues, issue => issue.Hint == "structured-data-missing" && issue.Path == "index.html");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Analyze_LocalizedAlternates_FlagRenderAtRootBreakage()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-seo-doctor-localized-root-breakage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "kontakt"));
            Directory.CreateDirectory(Path.Combine(root, "en", "contact"));

            File.WriteAllText(Path.Combine(root, "kontakt", "index.html"),
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

            File.WriteAllText(Path.Combine(root, "en", "contact", "index.html"),
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

            var result = WebSeoDoctor.Analyze(new WebSeoDoctorOptions
            {
                SiteRoot = root,
                CheckTitleLength = false,
                CheckDescriptionLength = false,
                CheckH1 = false,
                CheckImageAlt = false,
                CheckDuplicateTitles = false,
                CheckOrphanPages = false,
                CheckStructuredData = false
            });

            Assert.Contains(result.Issues, issue => issue.Hint == "canonical-route-missing" && issue.Path == "kontakt/index.html");
            Assert.Contains(result.Issues, issue => issue.Hint == "hreflang-route-missing" && issue.Path == "kontakt/index.html");
            Assert.Contains(result.Issues, issue => issue.Hint == "hreflang-self-missing" && issue.Path == "kontakt/index.html");
            Assert.Contains(result.Issues, issue => issue.Hint == "hreflang-return-link-missing" && issue.Path == "kontakt/index.html");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Analyze_LocalizedAlternates_FlagTargetCanonicalMismatch()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-seo-doctor-localized-canonical-mismatch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "kontakt"));
            Directory.CreateDirectory(Path.Combine(root, "en", "contact"));

            File.WriteAllText(Path.Combine(root, "en", "contact", "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>Contact</title>
                  <link rel="canonical" href="https://en.example.test/en/contact/" />
                  <link rel="alternate" hreflang="en" href="https://en.example.test/en/contact/" />
                  <link rel="alternate" hreflang="pl" href="https://pl.example.test/kontakt/" />
                </head>
                <body>
                  <h1>Contact</h1>
                </body>
                </html>
                """);

            File.WriteAllText(Path.Combine(root, "kontakt", "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>Kontakt</title>
                  <link rel="canonical" href="https://pl.example.test/support/kontakt/" />
                  <link rel="alternate" hreflang="pl" href="https://pl.example.test/kontakt/" />
                  <link rel="alternate" hreflang="en" href="https://en.example.test/en/contact/" />
                </head>
                <body>
                  <h1>Kontakt</h1>
                </body>
                </html>
                """);

            var result = WebSeoDoctor.Analyze(new WebSeoDoctorOptions
            {
                SiteRoot = root,
                CheckTitleLength = false,
                CheckDescriptionLength = false,
                CheckH1 = false,
                CheckImageAlt = false,
                CheckDuplicateTitles = false,
                CheckOrphanPages = false,
                CheckStructuredData = false
            });

            Assert.Contains(result.Issues, issue => issue.Hint == "hreflang-target-canonical-mismatch" && issue.Path == "en/contact/index.html");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Analyze_LocalizedAlternates_ResolveAcrossReferenceSiteRoots()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-seo-doctor-reference-roots-" + Guid.NewGuid().ToString("N"));
        var xyzRoot = Path.Combine(root, "_site-xyz");
        var plRoot = Path.Combine(root, "_site-pl");
        Directory.CreateDirectory(xyzRoot);
        Directory.CreateDirectory(plRoot);

        try
        {
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

            var result = WebSeoDoctor.Analyze(new WebSeoDoctorOptions
            {
                SiteRoot = xyzRoot,
                ReferenceSiteRoots = new[] { plRoot },
                CheckTitleLength = false,
                CheckDescriptionLength = false,
                CheckH1 = false,
                CheckImageAlt = false,
                CheckDuplicateTitles = false,
                CheckOrphanPages = false,
                CheckStructuredData = false
            });

            Assert.DoesNotContain(result.Issues, issue => issue.Hint == "hreflang-route-missing");
            Assert.DoesNotContain(result.Issues, issue => issue.Hint == "hreflang-target-canonical-mismatch");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Analyze_LocalizedAlternates_ResolveAcrossSameSiteRoot_WhenIncludedSubsetOmitsTargets()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-seo-doctor-same-root-subset-" + Guid.NewGuid().ToString("N"));
        var xyzRoot = Path.Combine(root, "_site-xyz");
        var plRoot = Path.Combine(root, "_site-pl");
        Directory.CreateDirectory(xyzRoot);
        Directory.CreateDirectory(plRoot);

        try
        {
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

            var result = WebSeoDoctor.Analyze(new WebSeoDoctorOptions
            {
                SiteRoot = xyzRoot,
                ReferenceSiteRoots = new[] { plRoot },
                Include = new[] { "projects/**" },
                CheckTitleLength = false,
                CheckDescriptionLength = false,
                CheckH1 = false,
                CheckImageAlt = false,
                CheckDuplicateTitles = false,
                CheckOrphanPages = false,
                CheckStructuredData = false
            });

            Assert.DoesNotContain(result.Issues, issue =>
                issue.Path == "projects/pswritehtml/index.html" &&
                issue.Hint == "hreflang-route-missing");
            Assert.DoesNotContain(result.Issues, issue =>
                issue.Path == "projects/pswritehtml/index.html" &&
                issue.Hint == "hreflang-target-canonical-mismatch");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Analyze_LocalizedAlternates_ResolveRootServedHostsWithinSameSiteRoot_WhenLanguageRootHostsConfigured()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-seo-doctor-root-hosts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "blog"));
        Directory.CreateDirectory(Path.Combine(root, "projects"));
        Directory.CreateDirectory(Path.Combine(root, "pl", "blog"));
        Directory.CreateDirectory(Path.Combine(root, "pl", "projekty"));

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
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

            File.WriteAllText(Path.Combine(root, "pl", "index.html"),
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

            File.WriteAllText(Path.Combine(root, "blog", "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>Blog</title>
                  <link rel="canonical" href="https://evotec.xyz/blog" />
                  <link rel="alternate" hreflang="en" href="https://evotec.xyz/blog" />
                  <link rel="alternate" hreflang="pl" href="https://evotec.pl/blog" />
                  <link rel="alternate" hreflang="x-default" href="https://evotec.xyz/blog" />
                </head>
                <body><h1>Blog</h1></body>
                </html>
                """);

            File.WriteAllText(Path.Combine(root, "pl", "blog", "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>Blog PL</title>
                  <link rel="canonical" href="https://evotec.pl/blog" />
                  <link rel="alternate" hreflang="en" href="https://evotec.xyz/blog" />
                  <link rel="alternate" hreflang="pl" href="https://evotec.pl/blog" />
                  <link rel="alternate" hreflang="x-default" href="https://evotec.xyz/blog" />
                </head>
                <body><h1>Blog PL</h1></body>
                </html>
                """);

            File.WriteAllText(Path.Combine(root, "projects", "index.html"),
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

            File.WriteAllText(Path.Combine(root, "pl", "projekty", "index.html"),
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

            var result = WebSeoDoctor.Analyze(new WebSeoDoctorOptions
            {
                SiteRoot = root,
                LanguageRootHosts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["evotec.pl"] = "pl"
                },
                CheckTitleLength = false,
                CheckDescriptionLength = false,
                CheckH1 = false,
                CheckImageAlt = false,
                CheckDuplicateTitles = false,
                CheckOrphanPages = false,
                CheckStructuredData = false
            });

            Assert.DoesNotContain(result.Issues, issue => issue.Hint == "hreflang-route-missing");
            Assert.DoesNotContain(result.Issues, issue => issue.Hint == "hreflang-target-canonical-mismatch");
            Assert.DoesNotContain(result.Issues, issue => issue.Hint == "hreflang-self-missing");
            Assert.DoesNotContain(result.Issues, issue => issue.Hint == "hreflang-return-link-missing");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Analyze_DoesNotFlagContentLeak_ForInlineBootstrapScript()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-seo-doctor-inline-script-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Home</title></head>
                <body>
                  <script>(function(){ var stored = localStorage.getItem('theme'); })();</script>
                  <main><h1>Home</h1><p>Rendered content only.</p></main>
                </body>
                </html>
                """);

            var result = WebSeoDoctor.Analyze(new WebSeoDoctorOptions
            {
                SiteRoot = root,
                CheckTitleLength = false,
                CheckDescriptionLength = false,
                CheckH1 = false,
                CheckImageAlt = false,
                CheckDuplicateTitles = false,
                CheckOrphanPages = false,
                CheckStructuredData = false,
                CheckContentLeaks = true
            });

            Assert.DoesNotContain(result.Issues, issue => issue.Hint == "content-frontmatter-leak");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Analyze_FlagsFlatAliasWithoutNoIndex_WhenDirectoryCanonicalExists()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-seo-doctor-alias-noindex-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "api", "sample-type"));
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html><head><title>Home</title></head><body><a href="/api/sample-type/">Type</a></body></html>
                """);
            File.WriteAllText(Path.Combine(root, "api", "sample-type", "index.html"),
                """
                <!doctype html>
                <html><head><title>Sample Type</title></head><body><h1>Sample Type</h1></body></html>
                """);
            File.WriteAllText(Path.Combine(root, "api", "sample-type.html"),
                """
                <!doctype html>
                <html><head><title>Sample Type Alias</title></head><body><h1>Sample Type Alias</h1></body></html>
                """);

            var result = WebSeoDoctor.Analyze(new WebSeoDoctorOptions
            {
                SiteRoot = root,
                CheckTitleLength = false,
                CheckDescriptionLength = false,
                CheckH1 = false,
                CheckImageAlt = false,
                CheckDuplicateTitles = false,
                CheckOrphanPages = false
            });

            Assert.Contains(result.Issues, issue =>
                issue.Hint == "canonical-alias-noindex" &&
                issue.Path == "api/sample-type.html");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Analyze_DoesNotFlagFlatAlias_WhenNoIndexRobotsIsPresent()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-seo-doctor-alias-noindex-ok-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "api", "sample-type"));
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html><head><title>Home</title></head><body><a href="/api/sample-type/">Type</a></body></html>
                """);
            File.WriteAllText(Path.Combine(root, "api", "sample-type", "index.html"),
                """
                <!doctype html>
                <html><head><title>Sample Type</title></head><body><h1>Sample Type</h1></body></html>
                """);
            File.WriteAllText(Path.Combine(root, "api", "sample-type.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>Sample Type Alias</title>
                  <meta name="robots" content="noindex,follow" />
                </head>
                <body><h1>Sample Type Alias</h1></body>
                </html>
                """);

            var result = WebSeoDoctor.Analyze(new WebSeoDoctorOptions
            {
                SiteRoot = root,
                CheckTitleLength = false,
                CheckDescriptionLength = false,
                CheckH1 = false,
                CheckImageAlt = false,
                CheckDuplicateTitles = false,
                CheckOrphanPages = false,
                IncludeNoIndexPages = true
            });

            Assert.DoesNotContain(result.Issues, issue =>
                issue.Hint == "canonical-alias-noindex" &&
                issue.Path == "api/sample-type.html");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Analyze_FlagsRenderedFrontMatterAndRawHtmlLeaks()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-seo-doctor-content-leak-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Contact</title></head>
                <body>
                  <main>
                    <article>
                      <p>-- title: "Contact" description: "Contact us" layout: contact translation_key: "contact" meta.raw_html: true -- &lt;div class="ev-contact-info"&gt;&lt;h2&gt;Contact&lt;/h2&gt;</p>
                    </article>
                  </main>
                </body>
                </html>
                """);

            var result = WebSeoDoctor.Analyze(new WebSeoDoctorOptions
            {
                SiteRoot = root,
                CheckTitleLength = false,
                CheckDescriptionLength = false,
                CheckH1 = false,
                CheckImageAlt = false,
                CheckDuplicateTitles = false,
                CheckOrphanPages = false,
                CheckCanonical = false,
                CheckHreflang = false,
                CheckStructuredData = false
            });

            Assert.Contains(result.Issues, issue =>
                issue.Hint == "content-frontmatter-leak" &&
                issue.Path == "index.html" &&
                issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Analyze_DoesNotTreatTwoDashProseAsFrontMatterDelimiter()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-seo-doctor-two-dash-prose-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Operator Notes</title></head>
                <body>
                  <main>
                    <article>
                      <p>-- title: Operator notes description: SQL examples layout: docs</p>
                    </article>
                  </main>
                </body>
                </html>
                """);

            var result = WebSeoDoctor.Analyze(new WebSeoDoctorOptions
            {
                SiteRoot = root,
                CheckTitleLength = false,
                CheckDescriptionLength = false,
                CheckH1 = false,
                CheckImageAlt = false,
                CheckDuplicateTitles = false,
                CheckOrphanPages = false,
                CheckCanonical = false,
                CheckHreflang = false,
                CheckStructuredData = false,
                CheckContentLeaks = true
            });

            Assert.DoesNotContain(result.Issues, issue => issue.Hint == "content-frontmatter-leak");
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Analyze_FlagsRenderedMarkdownLeakWhenEscapedHtmlAndMarkdownAreVisible()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-seo-doctor-markdown-leak-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Contact</title></head>
                <body>
                  <main>
                    <article>
                      <p>&lt;div class="ev-contact-info"&gt; # Contact [Write to us](/contact/) &lt;/div&gt;</p>
                    </article>
                  </main>
                </body>
                </html>
                """);

            var result = WebSeoDoctor.Analyze(new WebSeoDoctorOptions
            {
                SiteRoot = root,
                CheckTitleLength = false,
                CheckDescriptionLength = false,
                CheckH1 = false,
                CheckImageAlt = false,
                CheckDuplicateTitles = false,
                CheckOrphanPages = false,
                CheckCanonical = false,
                CheckHreflang = false,
                CheckStructuredData = false
            });

            Assert.Contains(result.Issues, issue =>
                issue.Hint == "content-markdown-leak" &&
                issue.Path == "index.html" &&
                issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Analyze_DoesNotFlagContentLeak_ForVisibleLanguageMetricLabel()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-seo-doctor-language-metric-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Project page</title></head>
                <body>
                  <main>
                    <article>
                      <h1>PSWriteHTML</h1>
                      <div class="ev-project-telemetry">
                        <span class="ev-project-metric">Language: PowerShell</span>
                        <span class="ev-project-metric">Updated: 2026-04-21</span>
                      </div>
                    </article>
                  </main>
                </body>
                </html>
                """);

            var result = WebSeoDoctor.Analyze(new WebSeoDoctorOptions
            {
                SiteRoot = root,
                CheckTitleLength = false,
                CheckDescriptionLength = false,
                CheckH1 = false,
                CheckImageAlt = false,
                CheckDuplicateTitles = false,
                CheckOrphanPages = false,
                CheckCanonical = false,
                CheckHreflang = false,
                CheckStructuredData = false,
                CheckContentLeaks = true
            });

            Assert.DoesNotContain(result.Issues, issue => issue.Hint == "content-frontmatter-leak");
            Assert.True(result.Success);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Analyze_DoesNotFlagContentLeak_ForApiStyleDocumentationContent()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-seo-doctor-api-docs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>API reference</title></head>
                <body>
                  <main>
                    <article>
                      <h1>ConvertFrom-OfficeWordHtml</h1>
                      <p>Language: PowerShell</p>
                      <p>Description: Converts HTML into a Word document.</p>
                      <p>Layout: Technical reference page</p>
                      <p>Render &lt;pre&gt; elements as single-cell tables and map &lt;section&gt; tags into Word.</p>
                      <div class="member-header">
                        <a class="member-anchor" href="#method-convertfrom-officewordhtml">#</a>
                        <code>ConvertFrom-OfficeWordHtml [-SectionTagHandling &lt;Nullable`1&gt;] [&lt;CommonParameters&gt;]</code>
                      </div>
                    </article>
                  </main>
                </body>
                </html>
                """);

            var result = WebSeoDoctor.Analyze(new WebSeoDoctorOptions
            {
                SiteRoot = root,
                CheckTitleLength = false,
                CheckDescriptionLength = false,
                CheckH1 = false,
                CheckImageAlt = false,
                CheckDuplicateTitles = false,
                CheckOrphanPages = false,
                CheckCanonical = false,
                CheckHreflang = false,
                CheckStructuredData = false,
                CheckContentLeaks = true
            });

            Assert.DoesNotContain(result.Issues, issue => issue.Hint == "content-frontmatter-leak");
            Assert.DoesNotContain(result.Issues, issue => issue.Hint == "content-markdown-leak");
            Assert.True(result.Success);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Analyze_PageAssertions_ValidateRepresentativeLocalizedPageOutsideScannedSubset()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-seo-doctor-page-assertions-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Directory.CreateDirectory(Path.Combine(root, "fr", "contact"));

            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Home</title></head>
                <body><h1>Home</h1></body>
                </html>
                """);

            File.WriteAllText(Path.Combine(root, "fr", "contact", "index.html"),
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

            var result = WebSeoDoctor.Analyze(new WebSeoDoctorOptions
            {
                SiteRoot = root,
                MaxHtmlFiles = 1,
                CheckTitleLength = false,
                CheckDescriptionLength = false,
                CheckH1 = false,
                CheckImageAlt = false,
                CheckDuplicateTitles = false,
                CheckOrphanPages = false,
                CheckCanonical = false,
                CheckHreflang = false,
                CheckStructuredData = false,
                CheckContentLeaks = false,
                PageAssertions = new[]
                {
                    new WebSeoDoctorPageAssertion
                    {
                        Path = "/fr/contact/",
                        Label = "French contact",
                        Contains = new[] { "Contactez-nous" },
                        NotContains = new[] { "translation_key:", "meta.raw_html: true" }
                    }
                }
            });

            Assert.Contains(result.Issues, issue =>
                issue.Hint == "page-assertion-contains" &&
                issue.Path == "fr/contact/index.html" &&
                issue.Severity.Equals("error", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Issues, issue =>
                issue.Hint == "page-assertion-not-contains" &&
                issue.Path == "fr/contact/index.html" &&
                issue.Message.Contains("translation_key:", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Issues, issue =>
                issue.Hint == "page-assertion-not-contains" &&
                issue.Path == "fr/contact/index.html" &&
                issue.Message.Contains("meta.raw_html: true", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Analyze_PageAssertions_DoesNotReportMissingPageWhenMustExistIsFalse()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-seo-doctor-page-assertions-optional-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Home</title></head>
                <body><h1>Home</h1></body>
                </html>
                """);

            var result = WebSeoDoctor.Analyze(new WebSeoDoctorOptions
            {
                SiteRoot = root,
                MaxHtmlFiles = 1,
                CheckTitleLength = false,
                CheckDescriptionLength = false,
                CheckH1 = false,
                CheckImageAlt = false,
                CheckDuplicateTitles = false,
                CheckOrphanPages = false,
                CheckCanonical = false,
                CheckHreflang = false,
                CheckStructuredData = false,
                CheckContentLeaks = false,
                PageAssertions = new[]
                {
                    new WebSeoDoctorPageAssertion
                    {
                        Path = "/fr/contact/",
                        Label = "Optional French contact",
                        MustExist = false,
                        Contains = new[] { "Contactez-nous" }
                    }
                }
            });

            Assert.DoesNotContain(result.Issues, issue => issue.Hint == "page-assertion-missing-page");
            Assert.True(result.Success);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Analyze_PageAssertions_HtmlScopeInspectsRawMarkup()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-seo-doctor-page-assertions-html-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>Home</title>
                  <script type="application/ld+json">{"@type":"Organization"}</script>
                </head>
                <body><h1>Home</h1></body>
                </html>
                """);

            var result = WebSeoDoctor.Analyze(new WebSeoDoctorOptions
            {
                SiteRoot = root,
                CheckTitleLength = false,
                CheckDescriptionLength = false,
                CheckH1 = false,
                CheckImageAlt = false,
                CheckDuplicateTitles = false,
                CheckOrphanPages = false,
                CheckCanonical = false,
                CheckHreflang = false,
                CheckStructuredData = false,
                CheckContentLeaks = false,
                PageAssertions = new[]
                {
                    new WebSeoDoctorPageAssertion
                    {
                        Path = "/",
                        Label = "Home raw html",
                        Scope = "html",
                        Contains = new[] { "application/ld+json" },
                        NotContains = new[] { "meta.raw_html: true" }
                    }
                }
            });

            Assert.DoesNotContain(result.Issues, issue => issue.Hint is "page-assertion-contains" or "page-assertion-not-contains");
            Assert.True(result.Success);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Analyze_PageAssertions_RenderedScopeAliasesToBody()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-seo-doctor-page-assertions-rendered-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Home</title></head>
                <body><h1>Contactez-nous</h1></body>
                </html>
                """);

            var result = WebSeoDoctor.Analyze(new WebSeoDoctorOptions
            {
                SiteRoot = root,
                CheckTitleLength = false,
                CheckDescriptionLength = false,
                CheckH1 = false,
                CheckImageAlt = false,
                CheckDuplicateTitles = false,
                CheckOrphanPages = false,
                CheckCanonical = false,
                CheckHreflang = false,
                CheckStructuredData = false,
                CheckContentLeaks = false,
                PageAssertions = new[]
                {
                    new WebSeoDoctorPageAssertion
                    {
                        Path = "/",
                        Label = "Home rendered alias",
                        Scope = "rendered",
                        Contains = new[] { "Contactez-nous" }
                    }
                }
            });

            Assert.DoesNotContain(result.Issues, issue => issue.Category == "page-assertion");
            Assert.True(result.Success);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Analyze_PageAssertions_MissingInRootPathReportsRelativeIndex()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-seo-doctor-page-assertions-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Home</title></head>
                <body><h1>Home</h1></body>
                </html>
                """);

            var result = WebSeoDoctor.Analyze(new WebSeoDoctorOptions
            {
                SiteRoot = root,
                MaxHtmlFiles = 1,
                CheckTitleLength = false,
                CheckDescriptionLength = false,
                CheckH1 = false,
                CheckImageAlt = false,
                CheckDuplicateTitles = false,
                CheckOrphanPages = false,
                CheckCanonical = false,
                CheckHreflang = false,
                CheckStructuredData = false,
                CheckContentLeaks = false,
                PageAssertions = new[]
                {
                    new WebSeoDoctorPageAssertion
                    {
                        Path = "/fr/contact/",
                        Label = "French contact must exist"
                    }
                }
            });

            Assert.Contains(result.Issues, issue =>
                issue.Hint == "page-assertion-missing-page" &&
                issue.Path == "fr/contact/index.html" &&
                issue.Message.Contains("French contact must exist", StringComparison.OrdinalIgnoreCase));
            Assert.False(result.Success);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Analyze_PageAssertions_TreatsDottedRouteSegmentsAsDirectories()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-seo-doctor-page-assertions-dotted-route-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Home</title></head>
                <body><h1>Home</h1></body>
                </html>
                """);

            var dottedRoute = Path.Combine(root, "docs", "v1.0");
            Directory.CreateDirectory(dottedRoute);
            File.WriteAllText(Path.Combine(dottedRoute, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Versioned docs</title></head>
                <body><h1>Documentation v1.0</h1></body>
                </html>
                """);

            var result = WebSeoDoctor.Analyze(new WebSeoDoctorOptions
            {
                SiteRoot = root,
                MaxHtmlFiles = 1,
                CheckTitleLength = false,
                CheckDescriptionLength = false,
                CheckH1 = false,
                CheckImageAlt = false,
                CheckDuplicateTitles = false,
                CheckOrphanPages = false,
                CheckCanonical = false,
                CheckHreflang = false,
                CheckStructuredData = false,
                CheckContentLeaks = false,
                PageAssertions = new[]
                {
                    new WebSeoDoctorPageAssertion
                    {
                        Path = "/docs/v1.0",
                        Label = "Versioned docs route",
                        Contains = new[] { "Documentation v1.0" }
                    }
                }
            });

            Assert.DoesNotContain(result.Issues, issue =>
                issue.Hint == "page-assertion-missing-page" ||
                issue.Hint == "page-assertion-contains");
            Assert.True(result.Success);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    [Fact]
    public void Analyze_PageAssertions_DoNotResolveSiblingPathsThatOnlyShareTheRootPrefix()
    {
        var parentRoot = Path.Combine(Path.GetTempPath(), "pf-web-seo-doctor-page-assertions-prefix-" + Guid.NewGuid().ToString("N"));
        var root = Path.Combine(parentRoot, "site");
        var sibling = Path.Combine(parentRoot, "site-copy");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(sibling);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Home</title></head>
                <body><h1>Home</h1></body>
                </html>
                """);

            Directory.CreateDirectory(Path.Combine(sibling, "fr", "contact"));
            File.WriteAllText(Path.Combine(sibling, "fr", "contact", "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Leaked sibling</title></head>
                <body><p>Contactez-nous</p></body>
                </html>
                """);

            var result = WebSeoDoctor.Analyze(new WebSeoDoctorOptions
            {
                SiteRoot = root,
                MaxHtmlFiles = 1,
                CheckTitleLength = false,
                CheckDescriptionLength = false,
                CheckH1 = false,
                CheckImageAlt = false,
                CheckDuplicateTitles = false,
                CheckOrphanPages = false,
                CheckCanonical = false,
                CheckHreflang = false,
                CheckStructuredData = false,
                CheckContentLeaks = false,
                PageAssertions = new[]
                {
                    new WebSeoDoctorPageAssertion
                    {
                        Path = "../site-copy/fr/contact/",
                        Label = "Sibling path traversal"
                    }
                }
            });

            Assert.Contains(result.Issues, issue =>
                issue.Hint == "page-assertion-missing-page" &&
                issue.Path == "../site-copy/fr/contact/index.html");
            Assert.DoesNotContain(result.Issues, issue =>
                issue.Hint == "page-assertion-contains" ||
                issue.Hint == "page-assertion-not-contains" ||
                issue.Message.Contains("Contactez-nous", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDeleteDirectory(parentRoot);
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
