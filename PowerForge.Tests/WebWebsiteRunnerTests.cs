using PowerForge.Web;

namespace PowerForge.Tests;

public sealed class WebWebsiteRunnerTests
{
    [Fact]
    public void ResolveRequestedEngineMode_UsesBinaryWhenTagIsProvided()
    {
        var mode = WebWebsiteRunner.ResolveRequestedEngineMode(new WebWebsiteRunnerOptions
        {
            EngineMode = string.Empty,
            PowerForgeWebTag = "PowerForgeWeb-v1.0.0-preview"
        });

        Assert.Equal("binary", mode);
    }

    [Fact]
    public void ResolveRequestedEngineMode_DefaultsToSourceWhenNoBinaryInputsExist()
    {
        var mode = WebWebsiteRunner.ResolveRequestedEngineMode(new WebWebsiteRunnerOptions
        {
            EngineMode = string.Empty
        });

        Assert.Equal("source", mode);
    }

    [Fact]
    public void ResolveReleaseAsset_SelectsCurrentRuntimeAsset_WhenExactAssetIsNotProvided()
    {
        var runtimeIdentifier = WebWebsiteRunner.GetCurrentRuntimeIdentifier();
        var assets = new[]
        {
            new WebWebsiteRunner.WebGitHubReleaseAsset
            {
                Name = $"PowerForgeWeb-1.0.0-net10.0-{runtimeIdentifier}-SingleContained.zip",
                DownloadUrl = "https://example.invalid/current.zip",
                Sha256 = "abc"
            },
            new WebWebsiteRunner.WebGitHubReleaseAsset
            {
                Name = "PowerForgeWeb-1.0.0-net10.0-linux-arm64-SingleContained.zip",
                DownloadUrl = "https://example.invalid/other.zip",
                Sha256 = "def"
            }
        };

        var resolved = WebWebsiteRunner.ResolveReleaseAsset(assets, "PowerForgeWeb", string.Empty, runtimeIdentifier);

        Assert.Equal($"PowerForgeWeb-1.0.0-net10.0-{runtimeIdentifier}-SingleContained.zip", resolved.Name);
        Assert.Equal("abc", resolved.Sha256);
    }

    [Fact]
    public void ResolveReleaseAsset_HonorsExplicitAsset()
    {
        var assets = new[]
        {
            new WebWebsiteRunner.WebGitHubReleaseAsset
            {
                Name = "PowerForgeWeb-1.0.0-net10.0-win-x64-SingleContained.zip",
                DownloadUrl = "https://example.invalid/one.zip",
                Sha256 = "abc"
            },
            new WebWebsiteRunner.WebGitHubReleaseAsset
            {
                Name = "PowerForgeWeb-1.0.0-net10.0-linux-x64-SingleContained.zip",
                DownloadUrl = "https://example.invalid/two.zip",
                Sha256 = "def"
            }
        };

        var resolved = WebWebsiteRunner.ResolveReleaseAsset(
            assets,
            "PowerForgeWeb",
            "PowerForgeWeb-1.0.0-net10.0-linux-x64-SingleContained.zip",
            "win-x64");

        Assert.Equal("PowerForgeWeb-1.0.0-net10.0-linux-x64-SingleContained.zip", resolved.Name);
        Assert.Equal("def", resolved.Sha256);
    }

    [Fact]
    public void Run_RejectsUnsupportedEngineMode()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-website-runner-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var pipelineConfig = Path.Combine(root, "pipeline.json");
        File.WriteAllText(pipelineConfig, "{}");

        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => WebWebsiteRunner.Run(new WebWebsiteRunnerOptions
            {
                WebsiteRoot = root,
                PipelineConfig = pipelineConfig,
                EngineMode = "banana"
            }));

            Assert.Contains("Unsupported engine mode", ex.Message);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Run_ReportsMissingWebsiteRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "powerforge-website-runner-test-" + Guid.NewGuid().ToString("N"));
        var pipelineConfig = Path.Combine(Path.GetTempPath(), "powerforge-website-runner-pipeline-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(pipelineConfig, "{}");

        try
        {
            var ex = Assert.Throws<DirectoryNotFoundException>(() => WebWebsiteRunner.Run(
                new WebWebsiteRunnerOptions
                {
                    WebsiteRoot = root,
                    PipelineConfig = pipelineConfig,
                    EngineMode = "source"
                }));

            Assert.Contains("Website root not found", ex.Message);
        }
        finally
        {
            if (File.Exists(pipelineConfig))
                File.Delete(pipelineConfig);
        }
    }
}
