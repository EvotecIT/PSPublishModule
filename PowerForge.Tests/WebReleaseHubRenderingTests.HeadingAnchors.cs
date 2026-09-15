using System.Text.Json;
using HtmlTinkerX;

namespace PowerForge.Tests;

public partial class WebReleaseHubRenderingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_ReleaseHeadingLinksRetainDistinctUnicodeAndAuthoredTargets(bool authoredHtml)
    {
        const string body = """
            <h2>プライバシー</h2>
            <h2>隐私</h2>
            <h2>隐私</h2>
            <h2 id="Mixed%23&amp;Value">Authored</h2>
            <h2 id="Case">Uppercase</h2>
            <h2 id="case">Lowercase</h2>
            <h2 id="v1-2-0-case">Already prefixed</h2>
            <div id="v1-2-0-隐私">Reserved non-heading target</div>
            <p><a href="#プライバシー">プライバシー</a>
            <a href="#%E9%9A%90%E7%A7%81">隐私</a>
            <a href="#隐私-2">隐私</a>
            <a href="#Mixed%2523%26Value">Authored</a>
            <a href="#Case">Uppercase</a>
            <a href="#case">Lowercase</a>
            <a href="#v1-2-0-case">Already prefixed</a>
            <a class="external" href="https://example.test/#隐私">External</a>
            <a class="reserved" href="#v1-2-0-隐私">Reserved</a></p>
            """;
        var html = BuildSinglePageSite(
            "{{< release-changelog product=\"*\" limit=\"5\" includePreview=\"true\" >}}",
            setup: root =>
            {
                var data = Path.Combine(root, "data");
                Directory.CreateDirectory(data);
                var releases = new[] { "v1.2.0", "v1.3.0" }.Select(tag => new Dictionary<string, object>
                {
                    ["tag"] = tag,
                    ["title"] = tag,
                    ["url"] = "https://example.test/releases/" + tag,
                    ["publishedAt"] = "2026-01-01T10:00:00Z",
                    [authoredHtml ? "body_html" : "body_md"] = body
                });
                File.WriteAllText(Path.Combine(data, "release-hub.json"), JsonSerializer.Serialize(new { releases }));
            },
            useScribanTheme: false,
            scribanLayoutBody: null);
        var document = HtmlParser.ParseWithAngleSharp(html);
        var releaseBodies = document.QuerySelectorAll(".pf-release-body");
        Assert.Equal(2, releaseBodies.Length);

        var headingIds = document.QuerySelectorAll(".pf-release-body h2").Select(heading => heading.Id).ToArray();
        Assert.Equal(headingIds.Length, headingIds.Distinct(StringComparer.Ordinal).Count());
        foreach (var releaseBody in releaseBodies)
        {
            var headings = releaseBody.QuerySelectorAll("h2");
            var links = releaseBody.QuerySelectorAll("a:not(.external):not(.reserved)");
            Assert.Equal(headings.Length, links.Length);
            for (var index = 0; index < headings.Length; index++)
            {
                var target = Uri.UnescapeDataString(links[index].GetAttribute("href")![1..]);
                Assert.Equal(headings[index].Id, target);
                Assert.Single(releaseBody.QuerySelectorAll("[id]"), element => element.Id == target);
            }
            Assert.Equal("https://example.test/#隐私", releaseBody.QuerySelector("a.external")!.GetAttribute("href"));
            Assert.Equal("#v1-2-0-隐私", releaseBody.QuerySelector("a.reserved")!.GetAttribute("href"));
        }
    }
}
