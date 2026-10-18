using PowerForge.Web;

namespace PowerForge.Tests;

public partial class WebReleaseHubRenderingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Build_ChangelogPublicNotes_PreservesNotesWithoutLinksOrDownloads(bool useScriban)
    {
        var html = BuildSinglePageSite(
            "{{< release-changelog includeLinks=\"false\" includeAssets=\"false\" >}}",
            WritePublicNotesData,
            useScriban,
            useScriban ? "{{ pf.release_changelog \"\" 20 true \"\" \"\" false false }}" : null);

        Assert.Contains("IntelligenceX 1.2.0", html);
        Assert.Contains("2026-02-25", html);
        Assert.Contains("Fixed behavior", html);
        Assert.Contains("review details", html);
        Assert.Contains("&lt;a href=", html);
        Assert.DoesNotContain("<a ", html);
        Assert.DoesNotContain("pf-release-assets", html);
        Assert.DoesNotContain("https://private.example/", html);
    }

    [Fact]
    public void Build_ChangelogPlacement_AllowsExplicitLinkAndAssetOverrides()
    {
        string Render(string attributes) => BuildSinglePageSite(
            "{{< release-changelog placement=\"public.timeline\" " + attributes + " >}}",
            root =>
            {
                WritePublicNotesData(root);
                File.WriteAllText(Path.Combine(root, "data", "release_placements.json"),
                    """{"public":{"timeline":{"includeLinks":false,"includeAssets":false}}}""");
            },
            false,
            null);

        var publicNotes = Render("");
        Assert.Contains("Fixed behavior", publicNotes);
        Assert.DoesNotContain("<a ", publicNotes);
        Assert.DoesNotContain("pf-release-assets", publicNotes);

        var linkedNotes = Render("include_links=\"true\" include_assets=\"true\"");
        Assert.Contains("href=\"https://private.example/notes\"", linkedNotes);
        Assert.Contains("href=\"https://private.example/review\"", linkedNotes);
        Assert.Contains("pf-release-assets", linkedNotes);
    }

    private static void WritePublicNotesData(string root)
    {
        WriteReleaseHubData(root);
        var path = Path.Combine(root, "data", "release-hub.json");
        var json = File.ReadAllText(path);
        json = json.Replace("https://github.com/EvotecIT/IntelligenceX/releases/tag/v1.2.0", "https://private.example/notes");
        json = json.Replace("## Added\\n- Stable improvements",
            "## Fixed behavior\\n- [review details](https://private.example/review)\\n- `<a href=example>`\\n- <a title=\"quoted > value\" href=\"https://private.example/raw\">raw details</a>".Replace("\"", "\\\""));
        File.WriteAllText(path, json);
    }
}
