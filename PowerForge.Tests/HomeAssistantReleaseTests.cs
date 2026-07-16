using PowerForge;
using System.Text.Json.Nodes;

namespace PowerForge.Tests;

public sealed class HomeAssistantReleaseTests {
    [Fact]
    public void Policy_DocumentationAndWorkflowOnlyChangesDoNotRelease() {
        var increment = HomeAssistantReleasePolicy.Resolve(
            Array.Empty<string>(),
            new[] { "README.md", ".github/workflows/validate.yml", "tests/test_client.py" },
            explicitIncrement: null);

        Assert.Equal(HomeAssistantVersionIncrement.None, increment);
    }

    [Fact]
    public void Policy_ProductChangeDefaultsToPatchAndLabelCanOverride() {
        Assert.Equal(
            HomeAssistantVersionIncrement.Patch,
            HomeAssistantReleasePolicy.Resolve(Array.Empty<string>(), new[] { "custom_components/example/sensor.py" }, null));
        Assert.Equal(
            HomeAssistantVersionIncrement.Minor,
            HomeAssistantReleasePolicy.Resolve(new[] { "release:minor" }, new[] { "README.md" }, null));
    }

    [Fact]
    public void Policy_ConflictingReleaseLabelsFail() {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            HomeAssistantReleasePolicy.Resolve(
                new[] { "release:patch", "release:major" },
                new[] { "custom_components/example/sensor.py" },
                null));

        Assert.Contains("conflicting", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SemanticVersion_UsesThreePartPublicVersions() {
        var current = HomeAssistantSemanticVersion.Parse("v1.2.3");

        Assert.Equal("1.2.4", current.Increment(HomeAssistantVersionIncrement.Patch).ToString());
        Assert.Equal("1.3.0", current.Increment(HomeAssistantVersionIncrement.Minor).ToString());
        Assert.Equal("2.0.0", current.Increment(HomeAssistantVersionIncrement.Major).ToString());
        Assert.Throws<InvalidOperationException>(() => HomeAssistantSemanticVersion.Parse("1.2.3.4"));
    }

    [Fact]
    public void RepositoryService_SynchronizesIntegrationMetadata() {
        using var fixture = HomeAssistantFixture.CreateIntegration("0.2.6");
        var service = new HomeAssistantRepositoryService();
        var snapshot = service.Inspect(fixture.Root);

        var changed = service.UpdateVersion(snapshot, fixture.Root, "0.2.7");

        Assert.Equal(HomeAssistantRepositoryKind.Integration, snapshot.Kind);
        Assert.Equal(2, changed.Count);
        Assert.Equal("0.2.7", ReadJsonVersion(Path.Combine(fixture.Root, "custom_components", "example", "manifest.json")));
        var pyProject = File.ReadAllText(Path.Combine(fixture.Root, "pyproject.toml"));
        Assert.True(pyProject.Contains("version = \"0.2.7\"", StringComparison.Ordinal), pyProject.Replace("\n", "\\n"));
    }

    [Fact]
    public void RepositoryService_SynchronizesPluginPackageAndLockMetadata() {
        using var fixture = HomeAssistantFixture.CreatePlugin("0.1.10");
        var service = new HomeAssistantRepositoryService();
        var snapshot = service.Inspect(fixture.Root);

        var changed = service.UpdateVersion(snapshot, fixture.Root, "0.1.11");

        Assert.Equal(HomeAssistantRepositoryKind.LovelacePlugin, snapshot.Kind);
        Assert.Equal(2, changed.Count);
        Assert.Equal("0.1.11", ReadJsonVersion(Path.Combine(fixture.Root, "package.json")));
        var packageLock = JsonNode.Parse(File.ReadAllText(Path.Combine(fixture.Root, "package-lock.json")))!.AsObject();
        Assert.Equal("0.1.11", packageLock["version"]!.GetValue<string>());
        Assert.Equal("0.1.11", packageLock["packages"]![""]!["version"]!.GetValue<string>());
    }

    [Fact]
    public void ReleaseService_ReusesVerifiedSourceMarkerWithoutPublishingAgain() {
        using var fixture = HomeAssistantFixture.CreateIntegration("0.2.7");
        const string mergeSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var marker = HomeAssistantReleasePolicy.BuildMarker(42, mergeSha);
        var github = new FakeGitHubClient {
            PullRequest = new HomeAssistantPullRequest {
                Number = 42,
                Merged = true,
                HeadSha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
                MergeCommitSha = mergeSha
            },
            MarkerRelease = new HomeAssistantGitHubRelease {
                TagName = "v0.2.7",
                Body = marker,
                HtmlUrl = "https://github.example/releases/v0.2.7"
            },
            TagCommitSha = "cccccccccccccccccccccccccccccccccccccccc"
        };
        var publisher = new RecordingPublisher();
        var service = new HomeAssistantReleaseService(
            new NullLogger(),
            new HomeAssistantRepositoryService(),
            new HomeAssistantReleaseGitService(),
            github,
            publisher);

        var result = service.Run(new HomeAssistantReleaseSpec {
            RepositoryRoot = fixture.Root,
            Owner = "EvotecIT",
            Repository = "example",
            Token = "token",
            PullRequestNumber = 42,
            MergeCommitSha = mergeSha,
            Apply = true,
            Publish = true
        });

        Assert.Equal(HomeAssistantReleaseAction.Reused, result.Action);
        Assert.Equal("v0.2.7", result.TagName);
        Assert.Equal(0, publisher.PublishCalls);
    }

    private static string ReadJsonVersion(string path)
        => JsonNode.Parse(File.ReadAllText(path))!["version"]!.GetValue<string>();

    private sealed class FakeGitHubClient : IHomeAssistantGitHubClient {
        internal HomeAssistantPullRequest PullRequest { get; set; } = new();
        internal HomeAssistantGitHubRelease? MarkerRelease { get; set; }
        internal string? TagCommitSha { get; set; }

        public HomeAssistantPullRequest GetPullRequest(int number) => PullRequest;
        public HomeAssistantCheckSummary GetCheckSummary(string commitSha) => new() { Total = 1 };
        public HomeAssistantGitHubRelease? GetLatestRelease() => null;
        public HomeAssistantGitHubRelease? FindReleaseByMarker(string marker) => MarkerRelease;
        public HomeAssistantGitHubRelease? GetReleaseByTag(string tagName) => MarkerRelease;
        public string? GetTagCommitSha(string tagName) => TagCommitSha;
    }

    private sealed class RecordingPublisher : IHomeAssistantReleasePublisher {
        internal int PublishCalls { get; private set; }

        public GitHubReleasePublishResult Publish(GitHubReleasePublishRequest request) {
            PublishCalls++;
            return new GitHubReleasePublishResult { Succeeded = true };
        }
    }

    private sealed class HomeAssistantFixture : IDisposable {
        private HomeAssistantFixture(string root) {
            Root = root;
        }

        internal string Root { get; }

        internal static HomeAssistantFixture CreateIntegration(string version) {
            var fixture = Create();
            var integration = Path.Combine(fixture.Root, "custom_components", "example");
            Directory.CreateDirectory(integration);
            File.WriteAllText(Path.Combine(fixture.Root, "hacs.json"), "{\"name\":\"Example\"}");
            File.WriteAllText(Path.Combine(integration, "manifest.json"), $"{{\"domain\":\"example\",\"version\":\"{version}\"}}");
            File.WriteAllText(Path.Combine(fixture.Root, "pyproject.toml"), $"[project]\nname = \"example\"\nversion = \"{version}\"\n\n[tool.pytest.ini_options]\n");
            return fixture;
        }

        internal static HomeAssistantFixture CreatePlugin(string version) {
            var fixture = Create();
            File.WriteAllText(Path.Combine(fixture.Root, "hacs.json"), "{\"name\":\"Example\",\"filename\":\"example.js\"}");
            File.WriteAllText(Path.Combine(fixture.Root, "package.json"), $"{{\"name\":\"example\",\"version\":\"{version}\"}}");
            File.WriteAllText(
                Path.Combine(fixture.Root, "package-lock.json"),
                $"{{\"name\":\"example\",\"version\":\"{version}\",\"lockfileVersion\":3,\"packages\":{{\"\":{{\"name\":\"example\",\"version\":\"{version}\"}}}}}}");
            return fixture;
        }

        private static HomeAssistantFixture Create() {
            var root = Path.Combine(Path.GetTempPath(), "PowerForge.HomeAssistant.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            return new HomeAssistantFixture(root);
        }

        public void Dispose() {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}