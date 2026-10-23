using System.Collections.Concurrent;
using System.Net;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using PowerForge.Web;

namespace PowerForge.Tests;

public sealed partial class WebSiteAuditOwnedDomainsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pf-owned-links-" + Guid.NewGuid().ToString("N"));
    private readonly HttpListener _listener;
    private readonly Task _server;
    private readonly ConcurrentDictionary<string, int> _requests = new();
    private readonly ConcurrentDictionary<string, (int Status, string Body, string? Location)> _responses = new();
    private readonly TaskCompletionSource _releaseHeld = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _origin;
    private readonly string _domain;

    public WebSiteAuditOwnedDomainsTests()
    {
        Directory.CreateDirectory(_root);
        var port = LoopbackHttpListener.GetCandidatePort();
        _listener = LoopbackHttpListener.Start(ref port);
        _origin = $"http://127.0.0.1:{port}";
        _domain = $"127.0.0.1:{port}";
        _server = ServeAsync();
        Respond("/sitemap.xml", 200, Fixture("evotec-sitemap.xml"));
        Respond("/projects/powerbginfo/", 200);
        Respond("/projects/powerbginfo", 301, location: "/projects/powerbginfo/");
        Respond("/projects/powerbginfo/index.html", 308, location: "/projects/powerbginfo/");
        Respond("/old-powerbginfo/", 302, location: "/projects/powerbginfo/");
    }

    [Fact]
    public void Audit_ReproducesSixMissingGuides_WhileAcceptingPagesRedirectsAndAnchors()
    {
        Page(Fixture("incident.html"));
        var result = Audit(Options("error"));
        var issues = result.Issues.Where(i => i.Category == "owned-link").ToArray();
        Assert.False(result.Success);
        Assert.Equal(6, result.BrokenLinkCount);
        Assert.Equal(6, issues.Length);
        Assert.All(issues, i => Assert.Equal("error", i.Severity));
        Assert.All(issues, i => Assert.Contains("/projects/powerbginfo/docs/", i.Message));
        Assert.Equal(1, Count("/sitemap.xml"));
        Assert.Equal(3, Count("/projects/powerbginfo/")); // Redirect destinations only; sitemap/anchor matches made no request.
        Assert.Equal(10 + 1, result.LinkCount);
    }

    [Fact]
    public void Audit_DefaultIsOffline_AndRepeatedChecksHaveNoCrossRunCache()
    {
        Page("<a href='" + _origin + "/missing'>Missing</a>");
        var disabled = Options();
        disabled.Enabled = false;
        Assert.DoesNotContain(Audit(disabled).Issues, i => i.Category == "owned-link");
        Assert.Empty(_requests);
        Assert.Single(Audit(Options()).Issues, i => i.Category == "owned-link");
        Respond("/missing", 200);
        Assert.DoesNotContain(Audit(Options()).Issues, i => i.Category == "owned-link");
        Assert.Equal(2, Count("/sitemap.xml"));
        Assert.Equal(2, Count("/missing"));
    }

    [Fact]
    public void Audit_CachesFallbacksAndChecksAssetsSrcsetsQueriesAndLocales()
    {
        Respond("/unlisted", 200);
        Page($"<a href='{_origin}/unlisted#one'>One</a><a href='{_origin}/unlisted#two'>Two</a>" +
             $"<a href='{_origin}/pl/projects/powerbginfo/#anchor'>Polish</a>" +
             $"<a href='{_origin}/de/projects/powerbginfo/'>German</a>" +
             $"<a href='{_origin}/projects/powerbginfo/?missing=true'>Query</a>" +
             $"<img src='{_origin}/missing.png' srcset='{_origin}/large.png 2x'>");
        var result = Audit(Options());
        Assert.Equal(4, result.Issues.Count(i => i.Category == "owned-link"));
        Assert.Equal(2, result.BrokenLinkCount);
        Assert.Equal(2, result.MissingAssetCount);
        Assert.Equal(1, Count("/unlisted"));
        Assert.Equal(0, Count("/pl/projects/powerbginfo/"));
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Errors)); // warning severity is nonfatal
    }

    [Theory]
    [InlineData(503, "unavailable")]
    [InlineData(200, "<html>not a sitemap</html>")]
    [InlineData(200, "<!DOCTYPE urlset [<!ENTITY x SYSTEM 'file:///etc/passwd'>]><urlset>&x;</urlset>")]
    public void Audit_UnavailableOrInvalidSitemapIsDistinctFromBrokenLinks(int status, string body)
    {
        Respond("/sitemap.xml", status, body);
        Respond("/unlisted", 200);
        Page($"<a href='{_origin}/unlisted'>Existing</a><a href='{_origin}/busy'>Busy</a>");
        Respond("/busy", 429);
        var result = Audit(Options("error"));
        var issues = result.Issues.Where(i => i.Category == "owned-link").ToArray();
        Assert.Equal(0, result.BrokenLinkCount);
        Assert.Equal(2, issues.Length);
        Assert.All(issues, i => Assert.Equal("warning", i.Severity));
        Assert.Contains(issues, i => i.Hint!.StartsWith("sitemap-unavailable"));
        Assert.Contains(issues, i => i.Message.Contains("HTTP 429"));
    }

    [Fact]
    public void Audit_EnforcesByteDocumentAndFallbackLimits_WithoutFalseBrokenLinks()
    {
        Page($"<a href='{_origin}/missing'>Missing</a>");
        var limited = Options("error");
        limited.MaxSitemapBytes = 10;
        limited.MaxLinkChecks = 0;
        var result = Audit(limited);
        Assert.Equal(0, result.BrokenLinkCount);
        Assert.Equal(2, result.Issues.Count(i => i.Category == "owned-link"));
        Assert.Equal(0, Count("/missing"));
        Respond("/sitemap.xml", 200, Index("/child.xml"));
        Respond("/child.xml", 200, Fixture("evotec-sitemap.xml"));
        limited = Options();
        limited.MaxSitemaps = 1;
        Audit(limited);
        Assert.Equal(0, Count("/child.xml"));
    }

    [Fact]
    public void Audit_TraversesSitemapIndexesOnce_AndHandlesCycles()
    {
        Respond("/sitemap.xml", 200, Index("/child.xml", "/child.xml", "/sitemap.xml"));
        Respond("/child.xml", 200, Fixture("evotec-sitemap.xml"));
        Page($"<a href='{_origin}/projects/powerbginfo/#first'>Present</a>");
        Assert.DoesNotContain(Audit(Options()).Issues, i => i.Category == "owned-link");
        Assert.Equal(1, Count("/child.xml"));
        Assert.Equal(0, Count("/projects/powerbginfo/"));
    }

    [Theory]
    [InlineData("https://unowned.example/missing")]
    [InlineData("/loop")]
    public void Audit_RedirectOutsideOwnershipOrLoopIsUnknown(string location)
    {
        Respond("/loop", 302, location: location);
        Page($"<a href='{_origin}/loop'>Redirect</a>");
        var result = Audit(Options("error"));
        Assert.Equal(0, result.BrokenLinkCount);
        var issue = Assert.Single(result.Issues, i => i.Category == "owned-link");
        Assert.Equal("warning", issue.Severity);
        Assert.Contains("unknown", issue.Message);
    }

    [Fact]
    public void Audit_IssuesParticipateInBaselineSuppressionAndGates()
    {
        Page($"<a href='{_origin}/missing'>Missing</a>");
        var first = Audit(Options());
        var issue = Assert.Single(first.Issues, i => i.Category == "owned-link");
        var baseline = Path.Combine(_root, "baseline.json");
        File.WriteAllText(baseline, JsonSerializer.Serialize(new { issueKeys = new[] { issue.Key } }));
        var baselined = WebSiteAuditor.Audit(new WebAuditOptions {
            SiteRoot = _root, OwnedLinks = Options(), BaselinePath = baseline,
            FailOnNewIssues = true, NavRequired = false, CheckLinkPurposeConsistency = false
        });
        Assert.DoesNotContain(baselined.Issues, i => i.Category == "owned-link" && i.IsNew);
        var suppressed = WebSiteAuditor.Audit(new WebAuditOptions {
            SiteRoot = _root, OwnedLinks = Options("error"), SuppressIssues = ["PFAUDIT.OWNED-LINK"], NavRequired = false
        });
        Assert.DoesNotContain(suppressed.Issues, i => i.Category == "owned-link");
        var gated = WebSiteAuditor.Audit(new WebAuditOptions {
            SiteRoot = _root, OwnedLinks = Options(), FailOnCategories = ["owned-link"], NavRequired = false
        });
        Assert.False(gated.Success);
    }

    [Fact]
    public void Audit_TimeoutAndTotalBudgetReportUnknown_WithoutIssuingRemainingRequests()
    {
        Page($"<a href='{_origin}/missing'>Missing</a>");
        var options = Options("error");
        options.Domains[0].SitemapUrl = _origin + "/held";
        options.TimeoutSeconds = 1;
        options.MaxDurationSeconds = 1;
        try
        {
            var result = Audit(options);
            Assert.Equal(0, result.BrokenLinkCount);
            Assert.Equal(2, result.Issues.Count(i => i.Category == "owned-link"));
            Assert.Equal(0, Count("/missing"));
        }
        finally { _releaseHeld.TrySetResult(); }
    }

    [Fact]
    public void Audit_ReadsGzipSitemap_AndBoundsChunkedAndExpandedBodies()
    {
        Page($"<a href='{_origin}/projects/powerbginfo/'>Present</a>");
        var options = Options();
        options.Domains[0].SitemapUrl = _origin + "/sitemap.xml.gz";
        Respond("/sitemap.xml.gz", 200, Fixture("evotec-sitemap.xml"));
        Assert.DoesNotContain(Audit(options).Issues, i => i.Category == "owned-link");
        Assert.Equal(0, Count("/projects/powerbginfo/"));
        options.MaxSitemapBytes = 250; // compressed content fits, decompressed XML does not
        Assert.Contains(Audit(options).Issues, i => i.Hint!.StartsWith("sitemap-unavailable"));
        options.Domains[0].SitemapUrl = _origin + "/chunked.xml";
        Respond("/chunked.xml", 200, Fixture("evotec-sitemap.xml"));
        Assert.Contains(Audit(options).Issues, i => i.Hint!.StartsWith("sitemap-unavailable"));
    }

    [Fact]
    public void Audit_ResolvesRelativeAndProtocolRelativeLinksAgainstAnExternalBase()
    {
        Page($"<base href='{_origin}/projects/powerbginfo/'><a href='?missing=true'>Query</a>" +
             $"<a href='//{_domain}/projects/powerbginfo/#install'>Present</a><img src='/missing.png'>");
        var result = Audit(Options("error"));
        Assert.Equal(1, result.BrokenLinkCount);
        Assert.Equal(1, result.MissingAssetCount);
        Assert.Equal(0, Count("/projects/powerbginfo/"));
    }

    [Theory]
    [InlineData("https://evotec.xyz")]
    [InlineData("evotec.xyz/path")]
    [InlineData("user@evotec.xyz")]
    [InlineData("evotec.xyz?query=1")]
    public void Audit_RejectsInvalidDomainConfigurationBeforeNetworkAccess(string domain)
    {
        Page("<a href='https://evotec.xyz/'>Page</a>");
        var options = Options();
        options.Domains = [new() { Domain = domain }];
        Assert.Throws<ArgumentException>(() => Audit(options));
        Assert.Empty(_requests);
    }

    [Theory]
    [InlineData(404, "/guides/task-scheduler/", "/guides/task/scheduler/")]
    [InlineData(429, "/guides/task-scheduler/", "/guides/task/scheduler/")]
    [InlineData(404, "/Guide/", "/guide/")]
    [InlineData(429, "/guide?x=a-b", "/guide?x=a/b")]
    public void Audit_BaselineKeepsDistinctDestinationsNew(int status, string oldPath, string newPath)
    {
        Respond(oldPath, status);
        Respond(newPath, status);
        Page($"<a href='{_origin}{oldPath}'>Old</a>");
        var first = Audit(Options());
        var old = Assert.Single(first.Issues, i => i.Category == "owned-link");
        var baseline = Path.Combine(_root, "baseline.json");
        File.WriteAllText(baseline, JsonSerializer.Serialize(new { issueKeys = new[] { old.Key } }));
        Page($"<a href='{_origin}{oldPath}'>Old</a><a href='{_origin}{newPath}'>New</a>");
        var next = WebSiteAuditor.Audit(new WebAuditOptions {
            SiteRoot = _root, OwnedLinks = Options(), BaselinePath = baseline, FailOnNewIssues = true,
            NavRequired = false, CheckLinkPurposeConsistency = false
        });
        var findings = next.Issues.Where(i => i.Category == "owned-link").ToArray();
        Assert.Equal(2, findings.Select(i => i.Key).Distinct().Count());
        Assert.Single(findings, i => i.IsNew);
        Assert.False(next.Success);
    }

    private WebOwnedLinkAuditOptions Options(string severity = "warning") => new() {
        Enabled = true, Severity = severity, Domains = [new() { Domain = _domain, SitemapUrl = _origin + "/sitemap.xml" }]
    };
    private WebAuditResult Audit(WebOwnedLinkAuditOptions options) => WebSiteAuditor.Audit(new WebAuditOptions {
        SiteRoot = _root, OwnedLinks = options, NavRequired = false, CheckLinkPurposeConsistency = false
    });
    private string Fixture(string file) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "OwnedDomainAudit", file))
        .Replace("https://evotec.xyz", _origin, StringComparison.Ordinal);
    private void Page(string html) => File.WriteAllText(Path.Combine(_root, "index.html"),
        html.Contains("<html", StringComparison.OrdinalIgnoreCase) ? html :
        "<!doctype html><html><head><meta charset='utf-8'><title>Owned links</title></head><body>" + html + "</body></html>");
    private void Respond(string path, int status, string body = "", string? location = null) => _responses[path] = (status, body, location);
    private int Count(string path) => _requests.GetValueOrDefault(path);
    private string Index(params string[] children) => "<sitemapindex xmlns='http://www.sitemaps.org/schemas/sitemap/0.9'>" +
        string.Join("", children.Select(path => $"<sitemap><loc>{_origin}{path}</loc></sitemap>")) + "</sitemapindex>";
    private async Task ServeAsync()
    {
        while (_listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await _listener.GetContextAsync(); }
            catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException) { return; }
            var path = context.Request.RawUrl!;
            if (path == "/held") await _releaseHeld.Task;
            _requests.AddOrUpdate(path, 1, (_, count) => count + 1);
            try
            {
                var response = _responses.GetValueOrDefault(path, (404, "", null));
                context.Response.StatusCode = response.Item1;
                if (response.Item3 is not null) context.Response.RedirectLocation = response.Item3;
                var bytes = Encoding.UTF8.GetBytes(response.Item2);
                if (path.EndsWith(".gz", StringComparison.Ordinal))
                {
                    using var output = new MemoryStream();
                    using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true)) gzip.Write(bytes);
                    bytes = output.ToArray();
            }
                if (path == "/chunked.xml") context.Response.SendChunked = true;
                else context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
            }
            catch (Exception ex) when (ex is HttpListenerException or IOException or ObjectDisposedException) { }
            finally { context.Response.Close(); }
        }
    }
    public void Dispose()
    {
        _releaseHeld.TrySetResult();
        _listener.Close();
        _server.GetAwaiter().GetResult();
        Directory.Delete(_root, true);
    }
}
