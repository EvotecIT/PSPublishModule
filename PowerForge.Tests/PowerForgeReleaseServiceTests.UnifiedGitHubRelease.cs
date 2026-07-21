namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void UnifiedGitHubRelease_ModuleVersionSourceUsesResolvedManifestVersion()
    {
        var root = CreateSandbox();
        try
        {
            var manifestPath = Path.Combine(root, "Company.Tools.psd1");
            File.WriteAllText(manifestPath, """
@{
    ModuleVersion = '3.0.74'
    PrivateData = @{
        PSData = @{
            Prerelease = 'preview1'
        }
    }
}
""");
            var plan = new PowerForgeModuleReleasePlanSummary
            {
                ManifestPath = manifestPath,
                ModuleVersion = "3.0.X"
            };
            var result = new PowerForgeReleaseResult
            {
                ModulePlan = plan,
                ReleaseAssetEntries = new[]
                {
                    new PowerForgeReleaseAssetEntry { Version = "1.0.7" }
                }
            };

            PowerForgeReleaseService.UpdateResolvedModuleVersion(plan);
            var version = PowerForgeReleaseService.ResolveUnifiedReleaseVersion(
                new PowerForgeReleaseGitHubOptions { VersionSource = PowerForgeReleaseVersionSource.Module },
                result,
                sharedReleaseVersion: null);

            Assert.Equal("3.0.74", plan.ModuleVersion);
            Assert.Equal("preview1", plan.PreReleaseTag);
            Assert.Equal("3.0.74-preview1", version);

            var explicitPlan = new PowerForgeModuleReleasePlanSummary
            {
                ManifestPath = manifestPath,
                ModuleVersion = "4.0.0"
            };
            PowerForgeReleaseService.UpdateResolvedModuleVersion(explicitPlan);
            Assert.Equal("4.0.0", explicitPlan.ModuleVersion);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void UnifiedGitHubRelease_PublishesAllZippedToolFamiliesToOneRelease()
    {
        var root = CreateSandbox();
        try
        {
            var powerForgeZip = Path.Combine(root, "PowerForge-1.0.7-osx-arm64.zip");
            var powerForgeWebZip = Path.Combine(root, "PowerForgeWeb-1.0.7-osx-arm64.zip");
            var powerForgeExecutable = Path.Combine(root, "PowerForge");
            var powerForgeWebExecutable = Path.Combine(root, "PowerForgeWeb");
            File.WriteAllText(powerForgeZip, "zip");
            File.WriteAllText(powerForgeWebZip, "zip");
            File.WriteAllText(powerForgeExecutable, "exe");
            File.WriteAllText(powerForgeWebExecutable, "exe");

            var publishCalls = new List<GitHubReleasePublishRequest>();
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => new PowerForgeToolReleasePlan(),
                runTools: _ => new PowerForgeToolReleaseResult
                {
                    Success = true,
                    Artefacts = new[]
                    {
                        new PowerForgeToolReleaseArtifactResult
                        {
                            Target = "PowerForge",
                            Version = "1.0.7",
                            ExecutablePath = powerForgeExecutable,
                            ZipPath = powerForgeZip
                        },
                        new PowerForgeToolReleaseArtifactResult
                        {
                            Target = "PowerForgeWeb",
                            Version = "1.0.7",
                            ExecutablePath = powerForgeWebExecutable,
                            ZipPath = powerForgeWebZip
                        }
                    }
                },
                publishGitHubRelease: request =>
                {
                    publishCalls.Add(request);
                    return new GitHubReleasePublishResult
                    {
                        Succeeded = true,
                        ReusedExistingRelease = true,
                        HtmlUrl = "https://github.com/EvotecIT/PSPublishModule/releases/tag/v1.0.7"
                    };
                });

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        GitHub = new PowerForgeToolReleaseGitHubOptions { Publish = false }
                    },
                    GitHub = new PowerForgeReleaseGitHubOptions
                    {
                        Publish = true,
                        VersionSource = PowerForgeReleaseVersionSource.Assets,
                        Owner = "EvotecIT",
                        Repository = "PSPublishModule",
                        Token = "token"
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    ToolsOnly = true
                });

            Assert.True(result.Success);
            Assert.Empty(result.ToolGitHubReleases);
            var publish = Assert.Single(publishCalls);
            Assert.Equal("v1.0.7", publish.TagName);
            Assert.Equal(
                new[] { powerForgeZip, powerForgeWebZip }.OrderBy(static path => path),
                publish.AssetFilePaths.OrderBy(static path => path));
            Assert.DoesNotContain(powerForgeExecutable, publish.AssetFilePaths);
            Assert.DoesNotContain(powerForgeWebExecutable, publish.AssetFilePaths);
        }
        finally
        {
            TryDelete(root);
        }
    }
}
