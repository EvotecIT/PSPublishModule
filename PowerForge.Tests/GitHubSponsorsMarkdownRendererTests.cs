namespace PowerForge.Tests;

public sealed class GitHubSponsorsMarkdownRendererTests
{
    [Fact]
    public void RenderFull_GroupsCurrentSponsorsAndListsFormerSponsorsWithoutFundingAmounts()
    {
        var recognition = new GitHubSponsorRecognitionResult
        {
            TierRecognitionEnabled = true,
            Tiers = new[]
            {
                new GitHubSponsorRecognitionTierSpec { Key = "Gold", Heading = "Gold Sponsors", Order = 10, AvatarSize = 80 },
                new GitHubSponsorRecognitionTierSpec { Key = "Sponsors", Heading = "Sponsors", Order = 20, AvatarSize = 64 }
            },
            Sponsors = new[]
            {
                Sponsor("alice", "Alice <Admin>", GitHubSponsorStatus.Current, "Gold", "https://example.com/alice"),
                Sponsor("former", "Former [Friend]", GitHubSponsorStatus.Former, "Sponsors", "https://example.com/former")
            }
        };

        var markdown = new GitHubSponsorsMarkdownRenderer().Render(recognition, new GitHubSponsorsOutputSpec
        {
            Layout = GitHubSponsorsMarkdownLayout.Full,
            Introduction = "Thank you for supporting the project."
        });

        Assert.Contains("## Gold Sponsors", markdown, StringComparison.Ordinal);
        Assert.Contains("Alice &lt;Admin&gt;", markdown, StringComparison.Ordinal);
        Assert.Contains("width=\"80\"", markdown, StringComparison.Ordinal);
        Assert.Contains("## Past Sponsors", markdown, StringComparison.Ordinal);
        Assert.Contains("<a href=\"https://example.com/former\">Former [Friend]</a>", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("$", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderCompact_LimitsEntriesAndAddsRosterLink()
    {
        var recognition = new GitHubSponsorRecognitionResult
        {
            Sponsors = new[]
            {
                Sponsor("alice", "Alice", GitHubSponsorStatus.Current, null, "https://example.com/alice"),
                Sponsor("bob", "Bob", GitHubSponsorStatus.Current, null, "https://example.com/bob")
            }
        };

        var markdown = new GitHubSponsorsMarkdownRenderer().Render(recognition, new GitHubSponsorsOutputSpec
        {
            Layout = GitHubSponsorsMarkdownLayout.Compact,
            MaxEntries = 1,
            MoreLink = "SPONSORS.md"
        });

        Assert.Contains("Alice", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Bob", markdown, StringComparison.Ordinal);
        Assert.Contains("[See all sponsors](SPONSORS.md)", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderFull_DropsUnsafeRemoteUrls()
    {
        var recognition = new GitHubSponsorRecognitionResult
        {
            Sponsors = new[] { Sponsor("bad", "Bad", GitHubSponsorStatus.Current, null, "javascript:alert(1)") }
        };

        var markdown = new GitHubSponsorsMarkdownRenderer().Render(recognition, new GitHubSponsorsOutputSpec());

        Assert.DoesNotContain("javascript", markdown, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Bad", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void RenderFull_ShowsEmptyStateWhenTierRecognitionIsEnabled()
    {
        var markdown = new GitHubSponsorsMarkdownRenderer().Render(new GitHubSponsorRecognitionResult
        {
            TierRecognitionEnabled = true,
            Tiers = new[] { new GitHubSponsorRecognitionTierSpec { Key = "Sponsors", Heading = "Sponsors" } }
        }, new GitHubSponsorsOutputSpec());

        Assert.Equal("No public sponsors are currently listed.", markdown);
    }

    [Fact]
    public void RenderFull_EncodesFormerSponsorNamesInsideSafeHtmlList()
    {
        var recognition = new GitHubSponsorRecognitionResult
        {
            Sponsors = new[]
            {
                Sponsor("former", "<script>*bold*</script>", GitHubSponsorStatus.Former, null, "https://example.com/former")
            }
        };

        var markdown = new GitHubSponsorsMarkdownRenderer().Render(recognition, new GitHubSponsorsOutputSpec());

        Assert.Contains("<ul>", markdown, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;*bold*&lt;/script&gt;", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", markdown, StringComparison.OrdinalIgnoreCase);
    }

    private static GitHubSponsorRecord Sponsor(string key, string name, GitHubSponsorStatus status, string? tier, string profile)
        => new()
        {
            Key = key,
            Login = key,
            DisplayName = name,
            ProfileUrl = profile,
            AvatarUrl = $"https://avatars.example/{key}",
            Status = status,
            EntityType = GitHubSponsorEntityType.User,
            RecognitionTierKey = tier
        };
}
