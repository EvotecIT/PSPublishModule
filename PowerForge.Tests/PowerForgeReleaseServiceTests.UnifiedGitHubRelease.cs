using System.IO.Compression;

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
    public void UnifiedGitHubRelease_ModuleVersionSourcePrefersBuiltArchiveManifest()
    {
        var root = CreateSandbox();
        try
        {
            var sourceManifestPath = Path.Combine(root, "Company.Tools.psd1");
            File.WriteAllText(sourceManifestPath, "@{ ModuleVersion = '3.0.73' }");
            var packedPath = Path.Combine(root, "Company.Tools.3.0.74.zip");
            using (var archive = ZipFile.Open(packedPath, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("Company.Tools/Company.Tools.psd1");
                using var writer = new StreamWriter(entry.Open());
                writer.Write("""
@{
    ModuleVersion = '3.0.74'
    PrivateData = @{
        PSData = @{
            Prerelease = 'preview1'
        }
    }
}
""");
            }

            var plan = new PowerForgeModuleReleasePlanSummary
            {
                ManifestPath = sourceManifestPath,
                ModuleVersion = "3.0.X"
            };
            var result = new PowerForgeReleaseResult { ModulePlan = plan };

            PowerForgeReleaseService.UpdateResolvedModuleVersion(plan, new[] { packedPath });
            var version = PowerForgeReleaseService.ResolveUnifiedReleaseVersion(
                new PowerForgeReleaseGitHubOptions { VersionSource = PowerForgeReleaseVersionSource.Module },
                result,
                sharedReleaseVersion: null);

            Assert.Equal("3.0.74", plan.ModuleVersion);
            Assert.Equal("preview1", plan.PreReleaseTag);
            Assert.Equal("3.0.74-preview1", version);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void UnifiedGitHubRelease_AcceptsXInsidePrereleaseLabel()
    {
        var result = new PowerForgeReleaseResult
        {
            ReleaseAssetEntries = new[]
            {
                new PowerForgeReleaseAssetEntry { Version = "1.2.3-next.1" }
            }
        };

        var version = PowerForgeReleaseService.ResolveUnifiedReleaseVersion(
            new PowerForgeReleaseGitHubOptions { VersionSource = PowerForgeReleaseVersionSource.Assets },
            result,
            sharedReleaseVersion: null);

        Assert.Equal("1.2.3-next.1", version);
    }

    [Fact]
    public void UnifiedGitHubRelease_ExpandsTopLevelModuleArchiveDirectoryToFiles()
    {
        var root = CreateSandbox();
        try
        {
            var packedPath = Path.Combine(root, "Company.Tools.3.0.74.zip");
            File.WriteAllText(packedPath, "zip");
            var unpackedPath = Path.Combine(root, "3.0.74", "Company.Tools.psd1");
            Directory.CreateDirectory(Path.GetDirectoryName(unpackedPath)!);
            File.WriteAllText(unpackedPath, "manifest");

            var entries = PowerForgeReleaseService.CreateModuleAssetEntries(root).ToArray();

            var entry = Assert.Single(entries);
            Assert.Equal(packedPath, entry.Path);
            Assert.Equal(PowerForgeReleaseAssetCategory.Module, entry.Category);
            Assert.Equal("Module", entry.Source);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void UnifiedGitHubRelease_FiltersModuleArchiveDirectoryToResolvedBuildVersion()
    {
        var root = CreateSandbox();
        try
        {
            var manifestPath = Path.Combine(root, "Company.Tools.psd1");
            File.WriteAllText(manifestPath, "@{ ModuleVersion = '3.0.74' }");
            var stalePath = Path.Combine(root, "Company.Tools.v3.0.73.zip");
            var currentPath = Path.Combine(root, "Company.Tools.v3.0.74.zip");
            var currentFullPath = Path.Combine(root, "Company.Tools.v3.0.74-FullPackage.zip");

            CreateModuleArchive(stalePath, "3.0.73");
            CreateModuleArchive(currentPath, "3.0.74");
            CreateModuleArchive(currentFullPath, "3.0.74");

            var plan = new PowerForgeModuleReleasePlanSummary
            {
                ManifestPath = manifestPath,
                ModuleVersion = "3.0.74"
            };
            var entries = PowerForgeReleaseService.CreateModuleAssetEntries(root, plan).ToArray();

            Assert.Equal(
                new[] { currentFullPath, currentPath }.OrderBy(static path => path, StringComparer.OrdinalIgnoreCase),
                entries.Select(static entry => entry.Path));
            Assert.DoesNotContain(entries, entry => string.Equals(entry.Path, stalePath, StringComparison.OrdinalIgnoreCase));

            void CreateModuleArchive(string archivePath, string version)
            {
                using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create);
                var entry = archive.CreateEntry("Company.Tools/Company.Tools.psd1");
                using var writer = new StreamWriter(entry.Open());
                writer.Write($"@{{ ModuleVersion = '{version}' }}");
            }
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ModuleReleaseStage_TracksUnifiedGitHubPublishing(bool publishUnifiedGitHub)
    {
        var root = CreateSandbox();
        try
        {
            var configPath = Path.Combine(root, "release.json");
            var scriptPath = Path.Combine(root, "Build-Module.ps1");
            var manifestPath = Path.Combine(root, "Company.Tools.psd1");
            File.WriteAllText(configPath, "{}");
            File.WriteAllText(scriptPath, "param([switch] $PowerForgeReleaseStage)");
            File.WriteAllText(manifestPath, "@{ ModuleVersion = '1.0.0' }");

            var result = new PowerForgeReleaseService(new NullLogger()).Execute(
                new PowerForgeReleaseSpec
                {
                    Module = new PowerForgeModuleReleaseOptions
                    {
                        RepositoryRoot = root,
                        ScriptPath = scriptPath,
                        ManifestPath = manifestPath
                    },
                    GitHub = publishUnifiedGitHub
                        ? new PowerForgeReleaseGitHubOptions { Publish = true }
                        : null
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = configPath,
                    ModuleOnly = true,
                    PlanOnly = true
                });

            Assert.True(result.Success);
            Assert.NotNull(result.ModulePlan);
            Assert.True(result.ModulePlan!.PowerForgeReleaseStage);
            Assert.Equal(publishUnifiedGitHub, result.ModulePlan.UnifiedGitHubRelease);
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

    [Fact]
    public void ToolsOnly_ExplicitToolGitHubPublishBypassesModuleVersionedUnifiedRelease()
    {
        var root = CreateSandbox();
        try
        {
            var zipPath = Path.Combine(root, "PowerForge-1.0.7-osx-arm64.zip");
            File.WriteAllText(zipPath, "zip");
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
                            ZipPath = zipPath
                        }
                    }
                },
                publishGitHubRelease: request =>
                {
                    publishCalls.Add(request);
                    return new GitHubReleasePublishResult { Succeeded = true };
                });

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        GitHub = new PowerForgeToolReleaseGitHubOptions
                        {
                            Publish = false,
                            Owner = "EvotecIT",
                            Repository = "PSPublishModule",
                            Token = "token"
                        }
                    },
                    GitHub = new PowerForgeReleaseGitHubOptions
                    {
                        Publish = true,
                        VersionSource = PowerForgeReleaseVersionSource.Module,
                        Owner = "EvotecIT",
                        Repository = "PSPublishModule",
                        Token = "token"
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    ToolsOnly = true,
                    PublishToolGitHub = true
                });

            Assert.True(result.Success);
            Assert.Null(result.UnifiedGitHubRelease);
            var publish = Assert.Single(publishCalls);
            Assert.Equal("PowerForge-v1.0.7", publish.TagName);
        }
        finally
        {
            TryDelete(root);
        }
    }
}
