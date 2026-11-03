using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class ModulePipelineUnifiedReleaseTests
{
    [Theory]
    [Trait("Category", "DotNetPublishPrGate")]
    [InlineData("matching")]
    [InlineData("other-tag")]
    [InlineData("other-tag-case")]
    [InlineData("checkpoint-tag-case")]
    [InlineData("other-repository")]
    [InlineData("no-receipt")]
    [InlineData("reused")]
    [InlineData("missing-id")]
    [InlineData("conflicting-ids")]
    public void Run_BindsOnlyExactPackageReleaseIdentity(string identity)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var staging = Path.Combine(Path.GetTempPath(), "PowerForge.Tests.Staging", Guid.NewGuid().ToString("N"));
        var secondStaging = staging + ".second";
        try
        {
            const string moduleName = "TestModule";
            const string version = "2.0.10";
            const string tag = "TestModule-v2.0.10";
            WriteMinimalModule(root.FullName, moduleName, version);
            WriteSynchronizedProjectBuildConfig(root.FullName, "project.build.json", moduleName, false, publishGitHub: true);
            InitializeModuleSourceGit(root.FullName);
            var sourceCommit = ReleaseSourceCommit.Capture(root.FullName);
            var spec = CreateCoordinatedGitHubSpec(root.FullName, staging, moduleName, tag);
            var release = spec.Segments.OfType<ConfigurationReleaseSegment>().Single().Configuration!;
            release.VersionSource = identity == "checkpoint-tag-case" ? ReleaseVersionSource.ProjectBuild : ReleaseVersionSource.Module;
            release.SynchronizeModuleVersion = identity == "checkpoint-tag-case";
            release.ResumeIncompleteRelease = false;
            var project = spec.Segments.OfType<ConfigurationProjectBuildSegment>().Single().Configuration!;
            project.UpdateVersions = false;
            project.UseAsReleaseVersionSource = identity == "checkpoint-tag-case";
            var calls = 0;
            var completedInvocation = false;
            var runner = CreateRunner(new FakeHostedOperations(new List<string>()),
                (request, _, configPath) =>
                {
                    var result = CreateProjectBuildResult(root.FullName, moduleName, version,
                        Path.Combine(root.FullName, "PackageOutput", "NuGet"), request, configPath);
                    if (request.PublishGitHub == true && identity != "no-receipt")
                    {
                        foreach (var id in identity == "conflicting-ids" ? new long[] { 4242, 4343 } : new long[] { identity == "missing-id" ? 0 : 4242 })
                            result.Result.GitHub.Add(new ProjectBuildGitHubResult
                            {
                                ProjectName = moduleName, Owner = "EvotecIT", Success = true,
                                Repository = identity == "other-repository" ? "OtherModule" : moduleName,
                                TagName = identity == "other-tag" ? "different-tag"
                                    : identity is "other-tag-case" or "checkpoint-tag-case" ? tag.ToLowerInvariant() : tag,
                                ReleaseId = id,
                                CreatedRelease = identity != "reused" && !completedInvocation
                            });
                    }
                    return result;
                },
                request =>
                {
                    calls++;
                    var reuse = identity == "matching" && calls == 1;
                    Assert.Equal(reuse, request.ReuseExistingReleaseOnConflict);
                    Assert.Equal(reuse, request.RequireExpectedExistingRelease);
                    Assert.Equal(reuse ? 4242L : (long?)null, request.ExpectedExistingReleaseId);
                    Assert.False(request.ReplaceExistingAssets);
                    Assert.Equal(sourceCommit, request.ExpectedTagCommitSha);
                    if (calls > 1 || identity == "reused")
                        throw new InvalidOperationException("Pre-existing release collision remains blocked.");
                    return new GitHubReleasePublishResult
                    {
                        Succeeded = true, ReleaseCreationSucceeded = true, ReleaseId = 4242,
                        AllAssetUploadsSucceeded = true, HtmlUrl = "https://github.com/EvotecIT/TestModule/releases/tag/" + tag
                    };
                });

            if (identity is "missing-id" or "conflicting-ids")
            {
                var error = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));
                Assert.Contains(identity == "missing-id" ? "invalid GitHub release identifier" : "multiple GitHub release identifiers", error.Message);
                Assert.Equal(0, calls);
            }
            else if (identity == "reused")
            {
                var collision = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));
                Assert.Contains("Pre-existing release collision remains blocked", collision.Message);
                Assert.Equal(1, calls);
            }
            else
            {
                var result = runner.Run(spec);
                Assert.Equal(1, calls);
                Assert.True(result.PublishResults.Single().Succeeded);
                Assert.Equal(version, result.Plan.ResolvedVersion);
                if (identity == "matching")
                {
                    // The second package publication successfully reuses the same existing release.
                    completedInvocation = true;
                    spec.Build.StagingPath = secondStaging;
                    var collision = Assert.Throws<InvalidOperationException>(() => runner.Run(spec));
                    Assert.Contains("Pre-existing release collision remains blocked", collision.Message);
                    Assert.Equal(2, calls);
                }
            }
        }
        finally
        {
            var git = new DirectoryInfo(Path.Combine(root.FullName, ".git"));
            if (git.Exists)
                foreach (var file in git.EnumerateFiles("*", SearchOption.AllDirectories).Where(file => file.IsReadOnly))
                    file.IsReadOnly = false;
            root.Delete(recursive: true);
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            if (Directory.Exists(secondStaging)) Directory.Delete(secondStaging, recursive: true);
        }
    }
}
