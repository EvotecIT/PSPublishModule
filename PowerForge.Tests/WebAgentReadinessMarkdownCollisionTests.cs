using System.Text;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebAgentReadinessMarkdownCollisionTests
{
    [Theory]
    [InlineData(".md")]
    [InlineData(".markdown")]
    public void Prepare_PreservesAuthoredDownloadsAcrossReruns(string extension)
    {
        WithSite(extension, (root, options) =>
        {
            var download = Path.Combine(root, "session" + extension);
            var authored = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes("# Recorded session\n\n```text\nfull transcript\n```\n")).ToArray();
            File.WriteAllBytes(download, authored);
            for (var run = 0; run < 2; run++)
            {
                var result = WebAgentReadiness.Prepare(options);
                Assert.Equal(authored, File.ReadAllBytes(download));
                Assert.DoesNotContain(download, result.WrittenFiles);
                Assert.Contains(result.Warnings, warning => warning.Contains("/session" + extension, StringComparison.Ordinal));
            }
        });
    }

    [Fact]
    public void Prepare_RefreshesGeneratedSidecarsAndPreservesSubsequentEdits()
    {
        WithSite(".md", (root, options) =>
        {
            var path = Path.Combine(root, "session.md");
            WebAgentReadiness.Prepare(options);
            Assert.Contains("First content", File.ReadAllText(path));
            File.WriteAllText(Path.Combine(root, "session.html"), "<html><head><title>Session</title></head><body><main><p>Updated content</p></main></body></html>");
            WebAgentReadiness.Prepare(options);
            Assert.Contains("Updated content", File.ReadAllText(path));
            Assert.DoesNotContain("First content", File.ReadAllText(path));
            var edited = File.ReadAllText(path).Replace("Updated content", "Authored replacement", StringComparison.Ordinal);
            File.WriteAllText(path, edited, Encoding.UTF8);
            WebAgentReadiness.Prepare(options);
            Assert.Equal(edited, File.ReadAllText(path));
            File.Delete(path);
            WebAgentReadiness.Prepare(options);
            Assert.Contains("Updated content", File.ReadAllText(path));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Prepare_KeepsHostConfigurationForPreservedMarkdown(bool editGenerated)
    {
        WithSite(".md", (root, options) =>
        {
            File.Move(Path.Combine(root, "session.html"), Path.Combine(root, "index.html"));
            options.AgentReadiness!.Apache = new AgentApacheSupportSpec { Enabled = true };
            var path = Path.Combine(root, "index.md");
            if (editGenerated)
            {
                WebAgentReadiness.Prepare(options);
                File.AppendAllText(path, "\nAuthored addition\n");
            }
            else File.WriteAllText(path, "# Authored home\n");
            var authored = File.ReadAllBytes(path);
            for (var run = 0; run < 2; run++)
            {
                var result = WebAgentReadiness.Prepare(options);
                Assert.Equal(authored, File.ReadAllBytes(path));
                Assert.DoesNotContain(path, result.WrittenFiles);
                var headers = File.ReadAllText(Path.Combine(root, "_headers"));
                Assert.Contains("/index.md", headers);
                Assert.Contains("Content-Type: text/markdown", headers);
                var apache = File.ReadAllText(Path.Combine(root, ".htaccess"));
                Assert.Contains("AddType text/markdown .md", apache);
                Assert.Contains("Vary \"Accept\"", apache);
                Assert.Contains("RewriteRule", apache);
            }
        });
    }

    private static void WithSite(string extension, Action<string, WebAgentReadinessPrepareOptions> test)
    {
        var root = Path.Combine(Path.GetTempPath(), "pf-markdown-collision-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "session.html"), "<html><head><title>Console session</title></head><body><main><p>First content</p></main></body></html>");
            test(root, new WebAgentReadinessPrepareOptions
            {
                SiteRoot = root,
                BaseUrl = "https://example.test",
                AgentReadiness = new AgentReadinessSpec
                {
                    Enabled = true,
                    MarkdownArtifacts = new AgentMarkdownArtifactsSpec { Enabled = true, Extension = extension }
                }
            });
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
