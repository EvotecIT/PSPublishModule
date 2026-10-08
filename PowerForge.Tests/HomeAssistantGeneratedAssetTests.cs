using PowerForge;

namespace PowerForge.Tests;

public sealed class HomeAssistantGeneratedAssetTests {
    [Theory]
    [InlineData("generated", true)]
    [InlineData("pack-only", true)]
    [InlineData("source", false)]
    [InlineData("metadata", false)]
    [InlineData("staged", false)]
    [InlineData("commit", false)]
    [InlineData("mismatch", false)]
    [InlineData("deleted", false)]
    public void BuildPermitsOnlyTheMatchingGeneratedBundle(string mutation, bool succeeds) {
        using var fixture = new PluginFixture();
        var runner = new BuildRunner(fixture.Root, mutation);
        var service = new HomeAssistantReleaseService(
            new NullLogger(), new HomeAssistantRepositoryService(runner),
            new HomeAssistantReleaseGitService(), null, null);
        var spec = new HomeAssistantReleaseBuildSpec {
            RepositoryRoot = fixture.Root, ReleaseVersion = "0.2.15",
            ReleaseCommitSha = fixture.Head
        };

        if (succeeds) {
            var result = service.Build(spec);
            Assert.Equal(HomeAssistantReleaseAction.Built, result.Action);
            Assert.Equal("rebuilt bundle", File.ReadAllText(Assert.Single(result.AssetFiles)));
            Assert.Equal(fixture.Head, Git(fixture.Root, "rev-parse", "HEAD").StdOut.Trim());
        } else {
            Assert.Throws<InvalidOperationException>(() => service.Build(spec));
        }
    }

    private static ProcessRunResult Git(string root, params string[] arguments) {
        var result = new GitClient().RunRawAsync(root, arguments, TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
        Assert.True(result.Succeeded, result.StdErr);
        return result;
    }

    private sealed class BuildRunner(string root, string mutation) : IProcessRunner {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default) {
            if (request.Arguments.Contains("pack")) {
                if (mutation != "pack-only") File.WriteAllText(Path.Combine(root, "card.js"), "rebuilt bundle");
                Directory.CreateDirectory(Path.Combine(root, "release"));
                File.WriteAllText(Path.Combine(root, "release", "card.js"), mutation == "mismatch" ? "different bundle" : "rebuilt bundle");
                if (mutation == "source") File.WriteAllText(Path.Combine(root, "source.ts"), "changed source");
                if (mutation == "metadata") File.WriteAllText(Path.Combine(root, "package.json"), "{}");
                if (mutation == "staged") Git(root, "add", "card.js");
                if (mutation == "commit") {
                    Git(root, "add", "card.js");
                    Git(root, "commit", "-m", "Unexpected build commit");
                }
                if (mutation == "deleted") File.Delete(Path.Combine(root, "card.js"));
            }
            return Task.FromResult(new ProcessRunResult(0, "", "", request.FileName, TimeSpan.Zero, timedOut: false));
        }
    }

    private sealed class PluginFixture : IDisposable {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "powerforge-hacs-generated-" + Guid.NewGuid().ToString("N"));
        public string Head { get; }

        public PluginFixture() {
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path.Combine(Root, "hacs.json"), "{\"filename\":\"card.js\"}");
            File.WriteAllText(Path.Combine(Root, "package.json"), "{\"version\":\"0.2.15\"}");
            File.WriteAllText(Path.Combine(Root, "package-lock.json"), "{\"version\":\"0.2.15\",\"packages\":{\"\":{\"version\":\"0.2.15\"}}}");
            File.WriteAllText(Path.Combine(Root, "card.js"), "old generated bundle");
            File.WriteAllText(Path.Combine(Root, "source.ts"), "source");
            Git(Root, "init", "-b", "main");
            Git(Root, "config", "user.name", "PowerForge Tests");
            Git(Root, "config", "user.email", "powerforge-tests@example.invalid");
            Git(Root, "add", ".");
            Git(Root, "commit", "-m", "Prepared plugin release");
            Head = Git(Root, "rev-parse", "HEAD").StdOut.Trim();
        }

        public void Dispose() {
            foreach (var file in new DirectoryInfo(Root).EnumerateFiles("*", SearchOption.AllDirectories))
                file.Attributes = FileAttributes.Normal;
            Directory.Delete(Root, recursive: true);
        }
    }
}
