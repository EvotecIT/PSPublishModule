using System.Text;
using PowerForge.Web;

namespace PowerForge.Tests;

public class WebMarkdownArtifactEncodingTests
{
    [Fact]
    public void Prepare_AdvertisesMarkdownWithoutAddingByteOrderMarks()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-md-encoding-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var page = Path.Combine(root, "index.html");
            File.WriteAllText(page,
                "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Home</title></head>" +
                "<body><main><h1>Welcome</h1><p>Hello agents.</p></main></body></html>",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            WebAgentReadiness.Prepare(new WebAgentReadinessPrepareOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                SiteName = "Example",
                AgentReadiness = new AgentReadinessSpec
                {
                    Enabled = true,
                    MarkdownArtifacts = new AgentMarkdownArtifactsSpec { Enabled = true },
                    ApiCatalog = new AgentApiCatalogSpec { Enabled = false },
                    AgentSkills = new AgentSkillsDiscoverySpec { Enabled = false },
                    AgentsJson = new AgentDiscoveryDocumentSpec { Enabled = false }
                }
            });

            Assert.Contains("type=\"text/markdown\" href=\"/index.md\"", File.ReadAllText(page), StringComparison.Ordinal);
            Assert.StartsWith("<!doctype html>", Encoding.ASCII.GetString(File.ReadAllBytes(page), 0, 15), StringComparison.Ordinal);
            Assert.Equal((byte)'#', File.ReadAllBytes(Path.Combine(root, "index.md"))[0]);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Audit_RecognizesRedirectPagesSavedWithByteOrderMark()
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-web-audit-bom-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "old"));
        try
        {
            File.WriteAllText(Path.Combine(root, "index.html"),
                "<!doctype html><html><head><meta charset=\"utf-8\"><title>Home</title></head><body><header><nav><a href=\"/\">Home</a></nav></header></body></html>");
            File.WriteAllText(Path.Combine(root, "old", "index.html"),
                "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Redirecting...</title>" +
                "<meta name=\"robots\" content=\"noindex,follow\" data-pf=\"static-redirect\">" +
                "<meta http-equiv=\"refresh\" content=\"0;url=/\"><link rel=\"canonical\" href=\"/\"></head>" +
                "<body><p>This page moved to <a href=\"/\">/</a>.</p></body></html>",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            var result = WebSiteAuditor.Audit(new WebAuditOptions
            {
                SiteRoot = root,
                IgnoreNavFor = Array.Empty<string>(),
                NavSelector = "header nav",
                NavRequired = true,
                CheckLinks = false,
                CheckAssets = false,
                CheckLinkPurposeConsistency = false,
                CheckNetworkHints = false,
                CheckRenderBlockingResources = false,
                CheckUtf8 = true
            });

            Assert.Equal(1, result.NavCheckedCount);
            Assert.Equal(1, result.NavIgnoredCount);
            Assert.DoesNotContain(result.Issues, issue => issue.Category.Equals("nav", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }
}
