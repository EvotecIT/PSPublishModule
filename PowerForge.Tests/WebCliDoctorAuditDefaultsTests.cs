using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public class WebCliDoctorAuditDefaultsTests
{
    [Fact]
    public void RunDoctorAudit_EnablesSeoMetaChecks_ByDefault()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-doctor-seo-default-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>Home</title>
                </head>
                <body>Home</body>
                </html>
                """);
            File.WriteAllText(Path.Combine(root, "404.html"), "<!doctype html><html><head><title>404</title></head><body>404</body></html>");

            var result = WebCliHelpers.RunDoctorAudit(root, new[]
            {
                "--no-nav",
                "--no-links",
                "--no-assets",
                "--no-ids",
                "--no-structure"
            });

            Assert.Contains(result.Issues, issue => issue.Hint == "seo-missing-canonical");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RunDoctorAudit_CanDisableSeoMetaChecks_WithNoSeoMeta()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-doctor-seo-disabled-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                """
                <!doctype html>
                <html>
                <head>
                  <title>Home</title>
                </head>
                <body>Home</body>
                </html>
                """);
            File.WriteAllText(Path.Combine(root, "404.html"), "<!doctype html><html><head><title>404</title></head><body>404</body></html>");

            var result = WebCliHelpers.RunDoctorAudit(root, new[]
            {
                "--no-seo-meta",
                "--no-nav",
                "--no-links",
                "--no-assets",
                "--no-ids",
                "--no-structure"
            });

            Assert.DoesNotContain(result.Issues, issue => issue.Category == "seo");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
