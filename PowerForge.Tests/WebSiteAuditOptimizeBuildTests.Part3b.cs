using PowerForge.Web;
using ImageMagick;
using System.Xml.Linq;

namespace PowerForge.Tests;

public partial class WebSiteAuditOptimizeBuildTests
{
    [Fact]
    public void Audit_FailOnWarnings_TriggersGateError()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Home</title></head>
                <body>
                  <nav><a href="/docs/">Docs</a></nav>
                </body>
                </html>
                """);

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                NavRequiredLinks = new[] { "/" },
                CheckLinks = false,
                CheckAssets = false,
                FailOnWarnings = true
            });

            Assert.False(result.Success);
            Assert.Contains(result.Errors, error => error.Contains("fail-on-warnings", StringComparison.OrdinalIgnoreCase));
            Assert.True(result.WarningCount > 0);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_FailOnIssueCodes_TriggersGateForMatchingIssueHint()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-gate-issue-codes-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Home</title></head>
                <body>
                  <nav><a href="/">Home</a></nav>
                  <main>
                    <img src="/images/hero.jpg" loading="lazy" decoding="async" />
                  </main>
                </body>
                </html>
                """);

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                CheckLinks = false,
                CheckAssets = false,
                FailOnIssueCodes = new[] { "media-img-dimensions" }
            });

            Assert.False(result.Success);
            Assert.Contains(result.Errors, error => error.Contains("fail issue codes", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Issues, issue => issue.Hint.Equals("media-img-dimensions", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_FailOnIssueCodes_DoesNotFailWhenPatternDoesNotMatch()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-gate-issue-codes-nonmatching-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Home</title></head>
                <body>
                  <nav><a href="/">Home</a></nav>
                  <main>
                    <img src="/images/hero.jpg" loading="lazy" decoding="async" />
                  </main>
                </body>
                </html>
                """);

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                CheckLinks = false,
                CheckAssets = false,
                FailOnIssueCodes = new[] { "heading-order" }
            });

            Assert.True(result.Success);
            Assert.Contains(result.Issues, issue => issue.Hint.Equals("media-img-dimensions", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(result.Errors, error => error.Contains("fail issue codes", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_Baseline_SuppressesExistingIssuesForFailOnNew()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-baseline-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Home</title></head>
                <body>
                  <nav><a href="/docs/">Docs</a></nav>
                </body>
                </html>
                """);

            var summaryPath = Path.Combine(root, "audit-baseline.json");
            var first = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                NavRequiredLinks = new[] { "/" },
                CheckLinks = false,
                CheckAssets = false,
                SummaryPath = summaryPath
            });

            Assert.True(first.WarningCount > 0);
            Assert.True(File.Exists(summaryPath));

            var second = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                NavRequiredLinks = new[] { "/" },
                CheckLinks = false,
                CheckAssets = false,
                BaselinePath = summaryPath,
                FailOnNewIssues = true
            });

            Assert.True(second.Success);
            Assert.Equal(0, second.NewIssueCount);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_FailOnNewIssues_WithMissingBaseline_FailsClearly()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-fail-on-new-missing-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Home</title></head>
                <body></body>
                </html>
                """);

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                BaselinePath = "missing-baseline.json",
                FailOnNewIssues = true,
                CheckLinks = false,
                CheckAssets = false,
                CheckNavConsistency = false,
                NavRequired = false
            });

            Assert.False(result.Success);
            Assert.Contains(result.Errors, e => e.Contains("fail-on-new", StringComparison.OrdinalIgnoreCase) &&
                                               e.Contains("baseline", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_BaselinePathOutsideRoot_IsAllowedWhenAbsolute()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-baseline-path-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><html><head><title>Home</title></head><body></body></html>");
            var outsideBaselinePath = Path.Combine(Path.GetTempPath(), "pf-web-audit-outside-" + Guid.NewGuid().ToString("N") + ".json");

            // Absolute baseline paths are allowed (baseline is a control input, not an output artifact).
            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                CheckLinks = false,
                CheckAssets = false,
                BaselinePath = outsideBaselinePath
            });

            Assert.True(result.Success);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_BaselinePathRelativeEscape_Throws()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-baseline-escape-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"), "<!doctype html><html><head><title>Home</title></head><body></body></html>");

            Assert.Throws<InvalidOperationException>(() => WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                BaselineRoot = root,
                CheckLinks = false,
                CheckAssets = false,
                BaselinePath = "..\\outside.json"
            }));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_UsesCanonicalNavPathForConsistency()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-canonical-nav-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head><title>Home</title></head>
                <body>
                  <header><nav><a href="/">Home</a><a href="/docs/">Docs</a></nav></header>
                </body>
                </html>
                """);

            File.WriteAllText(Path.Combine(root, "404.html"),
                """
                <!doctype html>
                <html>
                <head><title>Not found</title></head>
                <body>
                  <header><nav><a href="/docs/">Docs</a></nav></header>
                </body>
                </html>
                """);

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                NavSelector = "header nav",
                NavCanonicalPath = "index.html",
                CheckLinks = false,
                CheckAssets = false
            });

            Assert.True(result.Success);
            Assert.Equal(1, result.NavMismatchCount);
            Assert.Contains(result.Warnings, warning => warning.Contains("nav differs from baseline", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_DetectsInvalidUtf8()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-utf8-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var invalidBytes = new byte[] { 0x3C, 0x68, 0x74, 0x6D, 0x6C, 0x3E, 0xC3, 0x28, 0x3C, 0x2F, 0x68, 0x74, 0x6D, 0x6C, 0x3E };
            File.WriteAllBytes(Path.Combine(root, "index.html"), invalidBytes);

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                CheckLinks = false,
                CheckAssets = false,
                CheckTitles = false,
                CheckHtmlStructure = false
            });

            Assert.False(result.Success);
            Assert.Contains(result.Errors, error => error.Contains("invalid UTF-8", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Errors, error => error.Contains("byte offset", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(result.Issues, issue => issue.Category.Equals("utf8", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
