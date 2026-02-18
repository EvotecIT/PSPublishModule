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
