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
