using System.Text.Json;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed class WebStaticRedirectExporterTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "pf-static-redirects-" + Guid.NewGuid().ToString("N"));

    public WebStaticRedirectExporterTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "_powerforge"));
        Directory.CreateDirectory(Path.Combine(root, "api", "reader-ocr", "type"));
        File.WriteAllText(Path.Combine(root, "api", "reader-ocr", "type", "index.html"), "<h1>OCR type</h1>");
    }

    [Fact]
    public void Hosting_GithubPagesWritesMovedApiAliasAndPreservesCanonicalContent()
    {
        WriteRules(new RedirectSpec { From = "/api/reader/type/", To = "/api/reader-ocr/type/?view=full" });
        var pipeline = Path.Combine(root, "pipeline.json");
        File.WriteAllText(pipeline, """{"steps":[{"task":"hosting","siteRoot":".","target":"github-pages"}]}""");
        var result = WebPipelineRunner.RunPipeline(pipeline, logger: null);
        Assert.True(result.Success, string.Join(";", result.Steps.Select(s => s.Message)));
        var html = File.ReadAllText(Path.Combine(root, "api", "reader", "type", "index.html"));
        Assert.Contains("http-equiv=\"refresh\"", html);
        Assert.Contains("href=\"/api/reader-ocr/type/?view=full\"", html);
        Assert.Contains("noindex,follow", html);
        Assert.Contains("window.location.search", html);
        Assert.Contains("target.hash = window.location.hash", html);
        Assert.Equal("<h1>OCR type</h1>", File.ReadAllText(Path.Combine(root, "api", "reader-ocr", "type", "index.html")));
        Assert.Equal(1, WebStaticRedirectExporter.Export(root, dryRun: false));
    }

    [Fact]
    public void CaseDistinctSourcesArePreservedOnLinuxAndRejectedOnOtherBuildHosts()
    {
        WriteRules(new RedirectSpec { From = "/Legacy/", To = "/api/reader-ocr/type/" },
            new RedirectSpec { From = "/legacy/", To = "/api/reader-ocr/type/" });
        if (OperatingSystem.IsLinux())
        {
            Assert.Equal(2, WebStaticRedirectExporter.Export(root, false));
            Assert.True(File.Exists(Path.Combine(root, "Legacy", "index.html")));
            Assert.True(File.Exists(Path.Combine(root, "legacy", "index.html")));
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => WebStaticRedirectExporter.Export(root, false));
            Assert.False(Directory.Exists(Path.Combine(root, "Legacy")));
        }
    }

    [Fact]
    public void CaseOnlyMoveDoesNotBecomeAFalseRedirectLoopOnLinux()
    {
        WriteRules(new RedirectSpec { From = "/api/reader-ocr/TYPE/", To = "/api/reader-ocr/type/" });
        if (OperatingSystem.IsLinux())
            Assert.Equal(1, WebStaticRedirectExporter.Export(root, false));
        else
        {
            // The existing lowercase canonical page must remain protected on case-insensitive hosts.
            Assert.Throws<InvalidOperationException>(() => WebStaticRedirectExporter.Export(root, false));
            Assert.Equal("<h1>OCR type</h1>", File.ReadAllText(Path.Combine(root, "api", "reader-ocr", "type", "index.html")));
        }
    }

    [Fact]
    public void DryRunValidatesAllRulesWithoutWriting()
    {
        WriteRules(new RedirectSpec { From = "/old.html", To = "/api/reader-ocr/type/" });
        Assert.Equal(1, WebStaticRedirectExporter.Export(root, dryRun: true));
        Assert.False(File.Exists(Path.Combine(root, "old.html")));
    }

    [Theory]
    [InlineData("/../escape/")]
    [InlineData("/%2e%2e/escape/")]
    [InlineData("//outside.test/path/")]
    [InlineData("/old?query=1")]
    [InlineData("/old.xml")]
    [InlineData("/old\\escape/")]
    public void UnsafeOrNonPageSourcesCannotWrite(string source)
    {
        WriteRules(new RedirectSpec { From = source, To = "/api/reader-ocr/type/" });
        Assert.Throws<InvalidOperationException>(() => WebStaticRedirectExporter.Export(root, false));
    }

    [Fact]
    public void InvalidLaterRuleDoesNotLeaveEarlierRedirect()
    {
        WriteRules(new RedirectSpec { From = "/old/", To = "/api/reader-ocr/type/" },
            new RedirectSpec { From = "/unsupported/*", To = "/api/{path}", MatchType = RedirectMatchType.Wildcard });
        Assert.Throws<InvalidOperationException>(() => WebStaticRedirectExporter.Export(root, false));
        Assert.False(Directory.Exists(Path.Combine(root, "old")));
    }

    [Fact]
    public void ExistingPageIsNeverReplaced()
    {
        File.WriteAllText(Path.Combine(root, "index.html"), "Existing home");
        WriteRules(new RedirectSpec { From = "/", To = "/api/reader-ocr/type/" });
        Assert.Throws<InvalidOperationException>(() => WebStaticRedirectExporter.Export(root, false));
        Assert.Equal("Existing home", File.ReadAllText(Path.Combine(root, "index.html")));
    }

    [Fact]
    public void MissingCanonicalDestinationFailsBeforeWriting()
    {
        WriteRules(new RedirectSpec { From = "/old/", To = "/missing/" });
        Assert.Throws<InvalidOperationException>(() => WebStaticRedirectExporter.Export(root, false));
        Assert.False(Directory.Exists(Path.Combine(root, "old")));
    }

    [Fact]
    public void ExternalDestinationIsEscapedAndQueryPreservationCanBeDisabled()
    {
        WriteRules(new RedirectSpec { From = "/old.html", To = "https://example.test/new/?x='&y=<value>#section", PreserveQuery = false });
        WebStaticRedirectExporter.Export(root, false);
        var html = File.ReadAllText(Path.Combine(root, "old.html"));
        Assert.Contains("&amp;y=&lt;value&gt;", html);
        Assert.DoesNotContain("window.location.search", html);
        Assert.DoesNotContain("y=<value>", html);
        Assert.Contains("if (!target.hash)", html);
    }

    [Fact]
    public void LinkedOutputDirectoryCannotEscapeSiteRoot()
    {
        var outside = Path.Combine(Path.GetTempPath(), "pf-redirect-outside-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outside);
        var link = Path.Combine(root, "linked");
        try
        {
            try { Directory.CreateSymbolicLink(link, outside); }
            catch (UnauthorizedAccessException) when (OperatingSystem.IsWindows()) { return; }
            WriteRules(new RedirectSpec { From = "/linked/alias/", To = "/api/reader-ocr/type/" });
            Assert.Throws<InvalidOperationException>(() => WebStaticRedirectExporter.Export(root, false));
            Assert.Empty(Directory.EnumerateFileSystemEntries(outside));
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            Directory.Delete(outside);
        }
    }

    private void WriteRules(params RedirectSpec[] rules) => File.WriteAllText(Path.Combine(root, "_powerforge", "redirects.json"),
        JsonSerializer.Serialize(new { redirects = rules }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));

    public void Dispose() => Directory.Delete(root, recursive: true);
}
