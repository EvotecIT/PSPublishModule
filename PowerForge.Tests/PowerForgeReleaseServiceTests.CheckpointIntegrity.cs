namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void PublishBuiltReleaseOutputs_rejects_any_missing_unified_github_asset()
    {
        var root = CreateSandbox();
        try
        {
            var present = Path.Combine(root, "present.zip");
            var missing = Path.Combine(root, "missing.zip");
            File.WriteAllText(present, "present");
            var publisherCalled = false;
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Tools should not run."),
                runTools: _ => throw new InvalidOperationException("Tools should not run."),
                publishGitHubRelease: _ =>
                {
                    publisherCalled = true;
                    return new GitHubReleasePublishResult { Succeeded = true };
                });
            var result = new PowerForgeReleaseResult
            {
                Success = true,
                ReleaseAssets = [present, missing]
            };

            service.PublishBuiltReleaseOutputs(
                new PowerForgeReleaseSpec
                {
                    GitHub = new PowerForgeReleaseGitHubOptions
                    {
                        Publish = true,
                        Owner = "EvotecIT",
                        Repository = "Sample",
                        Token = "test-token"
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    ResolvedReleaseVersion = "1.0.0"
                },
                result);

            Assert.False(result.Success);
            Assert.False(publisherCalled);
            Assert.Contains(missing, result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }
}
