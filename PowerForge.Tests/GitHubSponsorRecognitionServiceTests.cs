namespace PowerForge.Tests;

public sealed class GitHubSponsorRecognitionServiceTests
{
    [Fact]
    public void Prepare_UsesOptInBandsAndLetsManualOverridesWin()
    {
        var source = new[]
        {
            Sponsor("platinum", 100),
            Sponsor("gold", 30),
            Sponsor("bronze", 5),
            Sponsor("custom", null),
            Sponsor("hidden", 100)
        };
        var spec = new GitHubSponsorsContentSpec
        {
            TierRecognition = new GitHubSponsorTierRecognitionSpec { Enabled = true },
            Overrides = new[]
            {
                new GitHubSponsorOverrideSpec { Login = "bronze", RecognitionTierKey = "Gold" },
                new GitHubSponsorOverrideSpec { Login = "custom", RecognitionTierKey = "Platinum", DisplayName = "Custom Company" },
                new GitHubSponsorOverrideSpec { Login = "hidden", Exclude = true }
            },
            ManualEntries = new[]
            {
                new GitHubManualSponsorSpec
                {
                    Key = "invoice-company",
                    DisplayName = "Invoice Company",
                    ProfileUrl = "https://example.com",
                    RecognitionTierKey = "Principal"
                }
            }
        };

        var result = new GitHubSponsorRecognitionService().Prepare(source, spec);

        Assert.True(result.TierRecognitionEnabled);
        Assert.Equal("Platinum", Assert.Single(result.Sponsors, item => item.Key == "platinum").RecognitionTierKey);
        Assert.Equal("Gold", Assert.Single(result.Sponsors, item => item.Key == "gold").RecognitionTierKey);
        Assert.Equal("Gold", Assert.Single(result.Sponsors, item => item.Key == "bronze").RecognitionTierKey);
        var custom = Assert.Single(result.Sponsors, item => item.Key == "custom");
        Assert.Equal("Platinum", custom.RecognitionTierKey);
        Assert.Equal("Custom Company", custom.DisplayName);
        Assert.Equal("Principal", Assert.Single(result.Sponsors, item => item.Key == "invoice-company").RecognitionTierKey);
        Assert.DoesNotContain(result.Sponsors, item => item.Key == "hidden");
    }

    [Fact]
    public void Prepare_LeavesFundingTierPrivateFromRecognitionWhenTieringIsDisabled()
    {
        var result = new GitHubSponsorRecognitionService().Prepare(new[] { Sponsor("gold", 30) }, new GitHubSponsorsContentSpec
        {
            TierRecognition = new GitHubSponsorTierRecognitionSpec { Enabled = false },
            Overrides = new[] { new GitHubSponsorOverrideSpec { Login = "gold", RecognitionTierKey = "Principal" } }
        });

        Assert.False(result.TierRecognitionEnabled);
        Assert.Null(Assert.Single(result.Sponsors).RecognitionTierKey);
    }

    [Fact]
    public void Prepare_RejectsUnknownExplicitRecognitionTier()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new GitHubSponsorRecognitionService().Prepare(
            new[] { Sponsor("alice", 5) },
            new GitHubSponsorsContentSpec
            {
                TierRecognition = new GitHubSponsorTierRecognitionSpec { Enabled = true },
                Overrides = new[] { new GitHubSponsorOverrideSpec { Login = "alice", RecognitionTierKey = "Diamond" } }
            }));

        Assert.Contains("Diamond", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Prepare_MapsUnknownGitHubTierToNeutralFallback()
    {
        var result = new GitHubSponsorRecognitionService().Prepare(new[] { Sponsor("custom", null) }, new GitHubSponsorsContentSpec
        {
            TierRecognition = new GitHubSponsorTierRecognitionSpec { Enabled = true }
        });

        Assert.Equal("Sponsors", Assert.Single(result.Sponsors).RecognitionTierKey);
    }

    [Fact]
    public void Prepare_DoesNotPublishHistoricalTierClassificationForFormerSponsors()
    {
        var former = Sponsor("former", 100);
        former.Sponsor.Status = GitHubSponsorStatus.Former;

        var result = new GitHubSponsorRecognitionService().Prepare(new[] { former }, new GitHubSponsorsContentSpec
        {
            TierRecognition = new GitHubSponsorTierRecognitionSpec { Enabled = true }
        });

        Assert.Null(Assert.Single(result.Sponsors).RecognitionTierKey);
    }

    [Fact]
    public void Prepare_RejectsManualLoginThatDuplicatesGitHubOrAnotherManualIdentity()
    {
        var service = new GitHubSponsorRecognitionService();
        var githubCollision = Assert.Throws<InvalidOperationException>(() => service.Prepare(
            new[] { Sponsor("alice", 5) },
            new GitHubSponsorsContentSpec
            {
                ManualEntries = new[]
                {
                    new GitHubManualSponsorSpec { Key = "invoice-alice", Login = "alice", DisplayName = "Alice" }
                }
            }));
        Assert.Contains("duplicates an existing sponsor", githubCollision.Message, StringComparison.OrdinalIgnoreCase);

        var manualCollision = Assert.Throws<InvalidOperationException>(() => service.Prepare(
            Array.Empty<GitHubSponsorSourceRecord>(),
            new GitHubSponsorsContentSpec
            {
                ManualEntries = new[]
                {
                    new GitHubManualSponsorSpec { Key = "company-one", Login = "same-login", DisplayName = "One" },
                    new GitHubManualSponsorSpec { Key = "same-login", DisplayName = "Two" }
                }
            }));
        Assert.Contains("configured more than once", manualCollision.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static GitHubSponsorSourceRecord Sponsor(string login, int? amount)
        => new()
        {
            Sponsor = new GitHubSponsorRecord
            {
                Key = login,
                Login = login,
                DisplayName = login,
                ProfileUrl = $"https://github.com/{login}",
                AvatarUrl = $"https://github.com/{login}.png",
                Status = GitHubSponsorStatus.Current,
                EntityType = GitHubSponsorEntityType.User
            },
            FundingTierMonthlyDollars = amount
        };
}
