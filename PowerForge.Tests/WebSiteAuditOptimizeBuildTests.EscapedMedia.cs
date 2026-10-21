using PowerForge.Web;

namespace PowerForge.Tests;

public partial class WebSiteAuditOptimizeBuildTests
{
    [Fact]
    public void Audit_WarnsWhenRenderedContentContainsEscapedMediaTags()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-media-escaped-tags-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Home</title></head>
                <body>
                  <main>
                    <p>&lt;img src="/images/hero.jpg" alt="Hero" loading="lazy" decoding="async" /&gt;</p>
                  </main>
                </body>
                </html>
                """);

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                CheckLinks = false,
                CheckAssets = false,
                IgnoreMediaFor = Array.Empty<string>()
            });

            Assert.True(result.Success);
            Assert.Contains(result.Issues, issue => issue.Hint.Equals("media-escaped-html-tag", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_DoesNotWarnForEscapedMediaTagsInsideCodeBlocks()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-media-escaped-code-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Home</title></head>
                <body>
                  <pre><code>&lt;img src="/images/hero.jpg" alt="Hero" /&gt;</code></pre>
                </body>
                </html>
                """);

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                CheckLinks = false,
                CheckAssets = false,
                IgnoreMediaFor = Array.Empty<string>()
            });

            Assert.True(result.Success);
            Assert.DoesNotContain(result.Issues, issue => issue.Hint.Equals("media-escaped-html-tag", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_DoesNotWarnForEscapedMediaTagNamesWithoutAttributes()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-media-escaped-tagname-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Home</title></head>
                <body>
                  <main>
                    <p>Creates an &lt;img&gt; tag.</p>
                    <p>Explains how the &lt;iframe&gt; element behaves.</p>
                  </main>
                </body>
                </html>
                """);

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                CheckLinks = false,
                CheckAssets = false,
                IgnoreMediaFor = Array.Empty<string>()
            });

            Assert.True(result.Success);
            Assert.DoesNotContain(result.Issues, issue => issue.Hint.Equals("media-escaped-html-tag", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("<p>Example: <code>&lt;img src=\"hero.png\"></code>.</p>")]
    [InlineData("<pre>&lt;video src=\"clip.webm\"></pre>")]
    [InlineData("<pre><code><span class=\"token\">&lt;source src=\"clip.webm\"></span></code></pre>")]
    public void Audit_DoesNotWarnForNormalizedEscapedMediaInsideCode(string example)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-media-normalized-code-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                "<!doctype html><html><head><title>Code examples</title></head><body>" + example + "</body></html>");

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                CheckLinks = false,
                CheckAssets = false,
                IgnoreMediaFor = Array.Empty<string>()
            });

            Assert.True(result.Success);
            Assert.DoesNotContain(result.Issues, issue => issue.Hint.Equals("media-escaped-html-tag", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_WarnsForEscapedMediaOutsideAnIdenticalCodeExample()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-media-identical-example-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            const string example = "&lt;img src=\"/images/hero.jpg\" alt=\"Hero\" /&gt;";
            File.WriteAllText(Path.Combine(root, "index.html"),
                "<!doctype html><html><head><title>Mixed content</title></head><body>" +
                "<pre><code>" + example + "</code></pre><p>" + example + "</p></body></html>");

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                CheckLinks = false,
                CheckAssets = false,
                IgnoreMediaFor = Array.Empty<string>()
            });

            Assert.True(result.Success);
            var issue = Assert.Single(result.Issues.Where(issue => issue.Hint.Equals("media-escaped-html-tag", StringComparison.OrdinalIgnoreCase)));
            Assert.Contains("(img)", issue.Message, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void OptimizeDetailed_AuditKeepsEscapedMediaWarningsOutsideCodeExamples()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-opt-audit-media-examples-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "code.html"),
                """
                <!doctype html>
                <html>
                <head><title>Code example</title></head>
                <body>
                  <pre id="example"><code>&lt;source src="clip.webm"&gt;</code></pre>
                  <p id="example">A duplicate ID remains visible to later audit checks.</p>
                </body>
                </html>
                """);
            File.WriteAllText(Path.Combine(root, "mixed.html"),
                """
                <!doctype html>
                <html>
                <head><title>Mixed content</title></head>
                <body>
                  <pre><code>&lt;source src="clip.webm"&gt;</code></pre>
                  <p>&lt;img src="hero.png"&gt;</p>
                </body>
                </html>
                """);
            var auditOptions = new WebAuditOptions
            {
                SiteRoot = root,
                CheckLinks = false,
                CheckAssets = false,
                IgnoreMediaFor = Array.Empty<string>()
            };
            var before = WebSiteAuditor.Audit(auditOptions);
            Assert.DoesNotContain(before.Issues, issue => issue.Path == "code.html" && issue.Hint == "media-escaped-html-tag");

            var optimized = WebAssetOptimizer.OptimizeDetailed(new WebAssetOptimizerOptions
            {
                SiteRoot = root,
                MinifyHtml = true
            });
            Assert.Equal(2, optimized.HtmlMinifiedCount);

            var after = WebSiteAuditor.Audit(auditOptions);

            Assert.True(after.Success);
            var escapedMedia = Assert.Single(after.Issues.Where(issue => issue.Hint == "media-escaped-html-tag"));
            Assert.Equal("mixed.html", escapedMedia.Path);
            Assert.Contains("(img)", escapedMedia.Message, StringComparison.Ordinal);
            Assert.Contains(after.Issues, issue => issue.Path == "code.html" && issue.Category == "duplicate-id");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
