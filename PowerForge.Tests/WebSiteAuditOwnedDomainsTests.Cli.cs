using System.Text.Json;
using PowerForge.Web;
using PowerForge.Web.Cli;

namespace PowerForge.Tests;

public sealed partial class WebSiteAuditOwnedDomainsTests
{
    [Theory]
    [InlineData("audit")]
    [InlineData("doctor")]
    public void Pipeline_AuditReadsSiteConfig_AndDoesNotReuseLiveResults(string task)
    {
        Page($"<a href='{_origin}/missing'>Missing</a>");
        File.WriteAllText(Path.Combine(_root, "404.html"), "<html><head><title>Not found</title></head><body>Not found</body></html>");
        var site = Path.Combine(_root, "site.json");
        File.WriteAllText(site, JsonSerializer.Serialize(new SiteSpec { OwnedLinks = Options("error") }));
        var pipeline = Path.Combine(_root, "pipeline.json");
        File.WriteAllText(pipeline, $$"""
            {"cache":true,"steps":[{"task":"{{task}}","config":"site.json","siteRoot":".","build":false,"verify":false,"audit":true,"checkNav":false,"checkSeoMeta":false}]}
            """);
        Respond("/missing", 200);
        var first = WebPipelineRunner.RunPipeline(pipeline, logger: null);
        Assert.True(first.Success, JsonSerializer.Serialize(first));
        Respond("/missing", 404);
        var second = WebPipelineRunner.RunPipeline(pipeline, logger: null);
        Assert.False(second.Success);
        Assert.False(second.Steps[0].Cached);
        Assert.Equal(2, Count("/missing"));
    }

    [Fact]
    public void Doctor_UsesTheSameOwnedLinkPolicy()
    {
        Page($"<a href='{_origin}/missing'>Missing</a>");
        var result = WebCliHelpers.RunDoctorAudit(_root,
            ["--no-nav", "--no-seo-meta", "--no-structure"], Options("error"));
        Assert.False(result.Success);
        Assert.Equal(1, result.BrokenLinkCount);
    }

}
