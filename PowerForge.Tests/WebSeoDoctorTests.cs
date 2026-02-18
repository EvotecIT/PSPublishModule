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

