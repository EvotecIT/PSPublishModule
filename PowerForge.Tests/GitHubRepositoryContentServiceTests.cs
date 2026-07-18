using System.Net;
using System.Text;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class GitHubRepositoryContentServiceTests
{
    [Fact]
    public void Sync_CreatesFullRosterAndUpdatesCompactReadmeBlock()
    {
        var root = CreateTempRoot();
        var readme = Path.Combine(root, "README.md");
        File.WriteAllText(readme, "# Demo\n\n<!-- POWERFORGE:sponsors:START -->\nold\n<!-- POWERFORGE:sponsors:END -->\n");
        using var httpClient = new HttpClient(new SponsorsHandler(activeOnly => activeOnly
            ? Response(Node("alice", "Alice", 30) + "," + Node("custom", "Custom", null))
            : Response(Node("alice", "Alice", 30) + "," + Node("custom", "Custom", null) + "," + Node("former", "Former", 5))));
        var service = new GitHubRepositoryContentService(
            sponsorsClient: new GitHubSponsorsClient(httpClient));

        var result = service.Sync(new GitHubRepositoryContentSpec
        {
            Token = "token",
            Sponsors = new GitHubSponsorsContentSpec
            {
                Enabled = true,
                SponsorableLogin = "owner",
                TierRecognition = new GitHubSponsorTierRecognitionSpec { Enabled = true },
                Overrides = new[] { new GitHubSponsorOverrideSpec { Login = "custom", RecognitionTierKey = "Platinum" } },
                Outputs = new[]
                {
                    new GitHubSponsorsOutputSpec
                    {
                        Path = "SPONSORS.md",
                        BlockId = "sponsors",
                        CreateIfMissing = true,
                        Introduction = "Thank you for supporting this project."
                    },
                    new GitHubSponsorsOutputSpec
                    {
                        Path = "README.md",
                        BlockId = "sponsors",
                        Layout = GitHubSponsorsMarkdownLayout.Compact,
                        MoreLink = "SPONSORS.md"
                    }
                }
            }
        }, root);

        Assert.True(result.Success);
        Assert.Equal(2, result.CurrentSponsors.Length);
        Assert.Single(result.FormerSponsors);
        Assert.Equal(2, result.Documents.Length);
        var sponsors = File.ReadAllText(Path.Combine(root, "SPONSORS.md"));
        Assert.Contains("# Sponsors", sponsors, StringComparison.Ordinal);
        Assert.Contains("## Platinum Sponsors", sponsors, StringComparison.Ordinal);
        Assert.Contains("## Gold Sponsors", sponsors, StringComparison.Ordinal);
        Assert.Contains("## Past Sponsors", sponsors, StringComparison.Ordinal);
        var readmeText = File.ReadAllText(readme);
        Assert.DoesNotContain("old", readmeText, StringComparison.Ordinal);
        Assert.Contains("[See all sponsors](SPONSORS.md)", readmeText, StringComparison.Ordinal);
    }

    [Fact]
    public void Sync_DoesNotModifyDocumentsWhenGitHubReturnsNoCurrentSponsors()
    {
        var root = CreateTempRoot();
        var readme = Path.Combine(root, "README.md");
        const string original = "<!-- POWERFORGE:sponsors:START -->\nkeep\n<!-- POWERFORGE:sponsors:END -->\n";
        File.WriteAllText(readme, original);
        using var httpClient = new HttpClient(new SponsorsHandler(_ => Response(string.Empty)));
        var service = new GitHubRepositoryContentService(sponsorsClient: new GitHubSponsorsClient(httpClient));

        var exception = Assert.Throws<InvalidOperationException>(() => service.Sync(new GitHubRepositoryContentSpec
        {
            Token = "token",
            Sponsors = new GitHubSponsorsContentSpec
            {
                Enabled = true,
                SponsorableLogin = "owner",
                Outputs = new[] { new GitHubSponsorsOutputSpec { Path = "README.md", BlockId = "sponsors" } }
            }
        }, root));

        Assert.Contains("no public current sponsors", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, File.ReadAllText(readme));
    }

    [Fact]
    public void Sync_PreflightsEveryDestinationBeforeWritingAnyFile()
    {
        var root = CreateTempRoot();
        var first = Path.Combine(root, "SPONSORS.md");
        File.WriteAllText(first, "<!-- POWERFORGE:sponsors:START -->\nold\n<!-- POWERFORGE:sponsors:END -->\n");
        var invalidReadme = Path.Combine(root, "README.md");
        File.WriteAllText(invalidReadme, "# Missing marker\n");
        using var httpClient = new HttpClient(new SponsorsHandler(_ => Response(Node("alice", "Alice", 5))));
        var service = new GitHubRepositoryContentService(sponsorsClient: new GitHubSponsorsClient(httpClient));

        Assert.Throws<InvalidOperationException>(() => service.Sync(new GitHubRepositoryContentSpec
        {
            Token = "token",
            Sponsors = new GitHubSponsorsContentSpec
            {
                Enabled = true,
                SponsorableLogin = "owner",
                IncludeFormer = false,
                Outputs = new[]
                {
                    new GitHubSponsorsOutputSpec { Path = "SPONSORS.md", BlockId = "sponsors" },
                    new GitHubSponsorsOutputSpec { Path = "README.md", BlockId = "sponsors" }
                }
            }
        }, root));

        Assert.Contains("old", File.ReadAllText(first), StringComparison.Ordinal);
    }

    [Fact]
    public void Sync_RefusesToCollapseTieredRosterWhenAllFundingTiersAreWithheld()
    {
        var root = CreateTempRoot();
        var readme = Path.Combine(root, "README.md");
        const string original = "<!-- POWERFORGE:sponsors:START -->\nkeep\n<!-- POWERFORGE:sponsors:END -->\n";
        File.WriteAllText(readme, original);
        using var httpClient = new HttpClient(new SponsorsHandler(_ => Response(Node("custom", "Custom", null))));
        var service = new GitHubRepositoryContentService(sponsorsClient: new GitHubSponsorsClient(httpClient));

        var exception = Assert.Throws<InvalidOperationException>(() => service.Sync(new GitHubRepositoryContentSpec
        {
            Token = "token",
            Sponsors = new GitHubSponsorsContentSpec
            {
                Enabled = true,
                SponsorableLogin = "owner",
                IncludeFormer = false,
                RequireFundingTierData = true,
                TierRecognition = new GitHubSponsorTierRecognitionSpec { Enabled = true },
                Outputs = new[] { new GitHubSponsorsOutputSpec { Path = "README.md", BlockId = "sponsors" } }
            }
        }, root));

        Assert.Contains("withheld funding-tier data", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, File.ReadAllText(readme));
    }

    [Fact]
    public void Sync_RejectsOutsideOutputBeforeWritingAnyRestrictedDocument()
    {
        var root = CreateTempRoot();
        var outsideRoot = CreateTempRoot();
        var first = Path.Combine(root, "SPONSORS.md");
        const string original = "<!-- POWERFORGE:sponsors:START -->\nold\n<!-- POWERFORGE:sponsors:END -->\n";
        File.WriteAllText(first, original);
        using var httpClient = new HttpClient(new SponsorsHandler(_ => Response(Node("alice", "Alice", 5))));
        var service = new GitHubRepositoryContentService(sponsorsClient: new GitHubSponsorsClient(httpClient));

        var exception = Assert.Throws<InvalidOperationException>(() => service.Sync(new GitHubRepositoryContentSpec
        {
            Token = "token",
            Sponsors = new GitHubSponsorsContentSpec
            {
                Enabled = true,
                SponsorableLogin = "owner",
                IncludeFormer = false,
                Outputs = new[]
                {
                    new GitHubSponsorsOutputSpec { Path = "SPONSORS.md", BlockId = "sponsors" },
                    new GitHubSponsorsOutputSpec
                    {
                        Path = Path.Combine(outsideRoot, "OUTSIDE.md"),
                        BlockId = "sponsors",
                        CreateIfMissing = true
                    }
                }
            }
        }, root, restrictedOutputRoot: root));

        Assert.Contains("outside the restricted output root", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, File.ReadAllText(first));
        Assert.False(File.Exists(Path.Combine(outsideRoot, "OUTSIDE.md")));
    }

    [Fact]
    public void Sync_RejectsOutputThroughSymlinkInsideRestrictedRoot()
    {
        var root = CreateTempRoot();
        var outsideRoot = CreateTempRoot();
        var link = Path.Combine(root, "linked-output");
        try
        {
            Directory.CreateSymbolicLink(link, outsideRoot);
        }
        catch (Exception linkException) when (linkException is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        using var httpClient = new HttpClient(new SponsorsHandler(_ => Response(Node("alice", "Alice", 5))));
        var service = new GitHubRepositoryContentService(sponsorsClient: new GitHubSponsorsClient(httpClient));

        var exception = Assert.Throws<InvalidOperationException>(() => service.Sync(new GitHubRepositoryContentSpec
        {
            Token = "token",
            Sponsors = new GitHubSponsorsContentSpec
            {
                Enabled = true,
                SponsorableLogin = "owner",
                IncludeFormer = false,
                Outputs = new[]
                {
                    new GitHubSponsorsOutputSpec
                    {
                        Path = Path.Combine("linked-output", "SPONSORS.md"),
                        BlockId = "sponsors",
                        CreateIfMissing = true
                    }
                }
            }
        }, root, restrictedOutputRoot: root));

        Assert.Contains("symbolic link or reparse point", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(outsideRoot, "SPONSORS.md")));
    }

    [Fact]
    public void Sync_RejectsDanglingFileSymlinkInsideRestrictedRoot()
    {
        var root = CreateTempRoot();
        var outsideRoot = CreateTempRoot();
        var outsideTarget = Path.Combine(outsideRoot, "SPONSORS.md");
        var link = Path.Combine(root, "SPONSORS.md");
        try
        {
            File.CreateSymbolicLink(link, outsideTarget);
        }
        catch (Exception linkException) when (linkException is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        Assert.False(File.Exists(outsideTarget));
        using var httpClient = new HttpClient(new SponsorsHandler(_ => Response(Node("alice", "Alice", 5))));
        var service = new GitHubRepositoryContentService(sponsorsClient: new GitHubSponsorsClient(httpClient));

        var exception = Assert.Throws<InvalidOperationException>(() => service.Sync(new GitHubRepositoryContentSpec
        {
            Token = "token",
            Sponsors = new GitHubSponsorsContentSpec
            {
                Enabled = true,
                SponsorableLogin = "owner",
                IncludeFormer = false,
                Outputs = new[]
                {
                    new GitHubSponsorsOutputSpec
                    {
                        Path = "SPONSORS.md",
                        BlockId = "sponsors",
                        CreateIfMissing = true
                    }
                }
            }
        }, root, restrictedOutputRoot: root));

        Assert.Contains("symbolic link or reparse point", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(outsideTarget));
    }

    [Fact]
    public void Sync_RejectsCaseVariantDanglingSymlinkConservatively()
    {
        var root = CreateTempRoot();
        var outsideRoot = CreateTempRoot();
        var outsideTarget = Path.Combine(outsideRoot, "SPONSORS.md");
        var link = Path.Combine(root, "SPONSORS.md");
        try
        {
            File.CreateSymbolicLink(link, outsideTarget);
        }
        catch (Exception linkException) when (linkException is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        using var httpClient = new HttpClient(new SponsorsHandler(_ => Response(Node("alice", "Alice", 5))));
        var service = new GitHubRepositoryContentService(sponsorsClient: new GitHubSponsorsClient(httpClient));
        var exception = Assert.Throws<InvalidOperationException>(() => service.Sync(new GitHubRepositoryContentSpec
        {
            Token = "token",
            Sponsors = new GitHubSponsorsContentSpec
            {
                Enabled = true,
                SponsorableLogin = "owner",
                IncludeFormer = false,
                Outputs = new[]
                {
                    new GitHubSponsorsOutputSpec
                    {
                        Path = "sponsors.md",
                        BlockId = "sponsors",
                        CreateIfMissing = true
                    }
                }
            }
        }, root, restrictedOutputRoot: root));

        Assert.Contains("symbolic link or reparse point", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(outsideTarget));
    }

    [Fact]
    public void Sync_PrefersExactDanglingSymlinkOverCaseCollidingRegularFile()
    {
        var root = CreateTempRoot();
        var outsideRoot = CreateTempRoot();
        var outsideTarget = Path.Combine(outsideRoot, "SPONSORS.md");
        File.WriteAllText(Path.Combine(root, "SPONSORS.md"), "regular file with different casing");
        var exactLink = Path.Combine(root, "sponsors.md");
        try
        {
            File.CreateSymbolicLink(exactLink, outsideTarget);
        }
        catch (Exception linkException) when (linkException is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        using var httpClient = new HttpClient(new SponsorsHandler(_ => Response(Node("alice", "Alice", 5))));
        var service = new GitHubRepositoryContentService(sponsorsClient: new GitHubSponsorsClient(httpClient));
        var exception = Assert.Throws<InvalidOperationException>(() => service.Sync(new GitHubRepositoryContentSpec
        {
            Token = "token",
            Sponsors = new GitHubSponsorsContentSpec
            {
                Enabled = true,
                SponsorableLogin = "owner",
                IncludeFormer = false,
                Outputs = new[]
                {
                    new GitHubSponsorsOutputSpec
                    {
                        Path = "sponsors.md",
                        BlockId = "sponsors",
                        CreateIfMissing = true
                    }
                }
            }
        }, root, restrictedOutputRoot: root));

        Assert.Contains("symbolic link or reparse point", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(outsideTarget));
    }

    private static string Node(string login, string name, int? amount)
    {
        var tier = amount is null ? "null" : $"{{\"name\":\"Tier\",\"monthlyPriceInDollars\":{amount.Value}}}";
        return $"{{\"sponsorEntity\":{{\"__typename\":\"User\",\"login\":\"{login}\",\"name\":\"{name}\",\"avatarUrl\":\"https://avatars.example/{login}\",\"url\":\"https://github.com/{login}\"}},\"tier\":{tier}}}";
    }

    private static string Response(string nodes)
        => "{\"data\":{\"owner\":{\"__typename\":\"User\",\"sponsorshipsAsMaintainer\":{\"nodes\":[" + nodes + "],\"pageInfo\":{\"hasNextPage\":false,\"endCursor\":null}}}}}";

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class SponsorsHandler(Func<bool, string> responseFactory) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var activeOnly = document.RootElement.GetProperty("variables").GetProperty("activeOnly").GetBoolean();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseFactory(activeOnly), Encoding.UTF8, "application/json")
            };
        }
    }
}
