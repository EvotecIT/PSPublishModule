using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class ProjectBuildGitHubCreationReceiptTests
{
    [Theory]
    [Trait("Category", "DotNetPublishPrGate")]
    [InlineData("Single", false, true, true)]
    [InlineData("Single", true, true, false)]
    [InlineData("PerProject", false, true, true)]
    [InlineData("PerProject", true, true, false)]
    [InlineData("Single", false, false, false)]
    public void Publish_PreservesPositiveCreationEvidenceWithoutPromotingReusedOrLegacyResults(
        string mode, bool reused, bool creationKnown, bool expectedCreated)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.GitHubReceiptTests", Guid.NewGuid().ToString("N")));
        try
        {
            var release = new DotNetRepositoryReleaseResult { Success = true };
            foreach (var name in new[] { "First", "Second" })
            {
                var artifact = Path.Combine(root.FullName, name + ".zip");
                File.WriteAllText(artifact, "artifact fixture");
                release.Projects.Add(new DotNetRepositoryProjectResult
                {
                    ProjectName = name, IsPackable = true, NewVersion = "1.0.0", ReleaseZipPath = artifact
                });
            }
            var calls = 0;
            var publisher = new ProjectBuildGitHubPublisher(new NullLogger(), request =>
            {
                calls++;
                return new GitHubReleasePublishResult
                {
                    Succeeded = true, ReleaseCreationSucceeded = creationKnown,
                    ReleaseId = 42, ReusedExistingRelease = reused
                };
            });
            var result = publisher.Publish(new ProjectBuildGitHubPublishRequest
            {
                Owner = "EvotecIT", Repository = "Sample", Token = "test-token",
                ReleaseMode = mode, TagConflictPolicy = "Reuse", Release = release
            });
            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(mode == "PerProject" ? 2 : 1, calls);
            Assert.Equal(2, result.Results.Count);
            Assert.All(result.Results, receipt =>
            {
                Assert.True(receipt.Success);
                Assert.Equal(42, receipt.ReleaseId);
                Assert.Equal(expectedCreated, receipt.CreatedRelease);
            });
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }
}
