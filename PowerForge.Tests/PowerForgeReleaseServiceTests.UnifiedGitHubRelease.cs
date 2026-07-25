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
    public void UnifiedGitHubRelease_ExpandsJsonScaffoldModuleArtifactTokensAfterVersionResolution()
    {
        var paths = PowerForgeReleaseService.ExpandModuleArtifactPaths(
            new[] { Path.Combine("CustomModule", "Output", "<TagModuleVersionWithPreRelease>") },
            "Sample",
            "1.2.3",
            "preview1");

        Assert.Equal(
            Path.Combine("CustomModule", "Output", "v1.2.3-preview1"),
            Assert.Single(paths));
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
    public void UnifiedGitHubRelease_ModuleVersionSourceResolvesTokenizedBuiltDirectory()
    {
        var root = CreateSandbox();
        try
        {
            var sourceManifestPath = Path.Combine(root, "Company.Tools.psd1");
            File.WriteAllText(sourceManifestPath, "@{ ModuleVersion = '3.0.73' }");
            var staleDirectory = Directory.CreateDirectory(
                Path.Combine(root, "Artifacts", "Unpacked", "v3.0.73")).FullName;
            var staleManifest = Path.Combine(staleDirectory, "Company.Tools.psd1");
            File.WriteAllText(staleManifest, "@{ ModuleVersion = '3.0.73' }");
            File.SetLastWriteTimeUtc(staleManifest, DateTime.UtcNow.AddDays(-1));
            var builtDirectory = Directory.CreateDirectory(
                Path.Combine(root, "Artifacts", "Unpacked", "v3.0.74")).FullName;
            var builtManifest = Path.Combine(builtDirectory, "Company.Tools.psd1");
            File.WriteAllText(builtManifest, "@{ ModuleVersion = '3.0.74' }");
            File.SetLastWriteTimeUtc(builtManifest, DateTime.UtcNow);

            var plan = new PowerForgeModuleReleasePlanSummary
            {
                ModuleName = "Company.Tools",
                ManifestPath = sourceManifestPath,
                ModuleVersion = "3.0.X"
            };

            PowerForgeReleaseService.UpdateResolvedModuleVersion(
                plan,
                new[]
                {
                    Path.Combine(root, "Artifacts", "Unpacked", "<TagModuleVersionWithPreRelease>")
                });

            Assert.Equal("3.0.74", plan.ModuleVersion);
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
            var moduleHostPath = Path.Combine(root, "PSPublishModule.psd1");
            File.WriteAllText(configPath, "{}");
            File.WriteAllText(scriptPath, "param([switch] $PowerForgeReleaseStage)");
            File.WriteAllText(manifestPath, "@{ ModuleVersion = '1.0.0' }");
            File.WriteAllText(moduleHostPath, "@{ ModuleVersion = '1.0.0' }");

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
                    PlanOnly = true,
                    ModuleHostPath = moduleHostPath,
                    ModuleSkipInstall = true,
                    ModuleRunMode = publishUnifiedGitHub
                        ? ConfigurationGateMode.Publish
                        : ConfigurationGateMode.Build
                });

            Assert.True(result.Success);
            Assert.NotNull(result.ModulePlan);
            Assert.True(result.ModulePlan!.PowerForgeReleaseStage);
            Assert.Equal(moduleHostPath, result.ModulePlan.ModulePath);
            Assert.True(result.ModulePlan.SkipInstall);
            Assert.Equal(publishUnifiedGitHub, result.ModulePlan.UnifiedGitHubRelease);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ModuleReleaseStage_OmittedRunModeDefaultsToBuildAndSuppressesUnifiedGitHubPublishing()
    {
        var root = CreateSandbox();
        try
        {
            var configPath = Path.Combine(root, "release.json");
            var scriptPath = Path.Combine(root, "Build-Module.ps1");
            File.WriteAllText(configPath, "{}");
            File.WriteAllText(scriptPath, "param([switch] $PowerForgeReleaseStage)");

            var result = new PowerForgeReleaseService(new NullLogger()).Execute(
                new PowerForgeReleaseSpec
                {
                    Module = new PowerForgeModuleReleaseOptions
                    {
                        RepositoryRoot = root,
                        ScriptPath = scriptPath
                    },
                    GitHub = new PowerForgeReleaseGitHubOptions { Publish = true }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = configPath,
                    ModuleOnly = true,
                    PlanOnly = true
                });

            Assert.True(result.Success);
            Assert.NotNull(result.ModulePlan);
            Assert.Equal(ConfigurationGateMode.Build, result.ModulePlan!.RunMode);
            Assert.False(result.ModulePlan.UnifiedGitHubRelease);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData(ConfigurationGateMode.Manifest, false)]
    [InlineData(ConfigurationGateMode.Documentation, false)]
    [InlineData(ConfigurationGateMode.Build, false)]
    [InlineData(ConfigurationGateMode.Publish, true)]
    public void ModuleReleaseStage_PreservesLegacyHostFallbackAndOnlyPublishesUnifiedGitHubAtPublishGate(
        ConfigurationGateMode runMode,
        bool expectedUnifiedGitHub)
    {
        var root = CreateSandbox();
        try
        {
            var configPath = Path.Combine(root, "release.json");
            var scriptPath = Path.Combine(root, "Build-Module.ps1");
            File.WriteAllText(configPath, "{}");
            File.WriteAllText(scriptPath, "param([switch] $PowerForgeReleaseStage)");

            var result = new PowerForgeReleaseService(new NullLogger()).Execute(
                new PowerForgeReleaseSpec
                {
                    Module = new PowerForgeModuleReleaseOptions
                    {
                        RepositoryRoot = root,
                        ScriptPath = scriptPath
                    },
                    GitHub = new PowerForgeReleaseGitHubOptions { Publish = true }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = configPath,
                    ModuleOnly = true,
                    PlanOnly = true,
                    ModuleRunMode = runMode
                });

            Assert.True(result.Success);
            Assert.NotNull(result.ModulePlan);
            Assert.Null(result.ModulePlan!.Framework);
            Assert.Equal(expectedUnifiedGitHub, result.ModulePlan.UnifiedGitHubRelease);
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
    public void PublishBuiltReleaseOutputs_UsesCapturedArtifactsWithoutRebuilding()
    {
        var root = CreateSandbox();
        try
        {
            var zipPath = Path.Combine(root, "PowerForge-1.0.7-win-x64.zip");
            File.WriteAllText(zipPath, "zip");
            var publishCalls = new List<GitHubReleasePublishRequest>();
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages must not rebuild during staged publishing."),
                planTools: (_, _, _) => throw new InvalidOperationException("Tools must not replan during staged publishing."),
                runTools: _ => throw new InvalidOperationException("Tools must not rebuild during staged publishing."),
                publishGitHubRelease: request =>
                {
                    publishCalls.Add(request);
                    return new GitHubReleasePublishResult {
                        Succeeded = true,
                        HtmlUrl = "https://github.com/EvotecIT/PSPublishModule/releases/tag/v1.0.7"
                    };
                });
            var spec = new PowerForgeReleaseSpec {
                GitHub = new PowerForgeReleaseGitHubOptions {
                    Publish = true,
                    VersionSource = PowerForgeReleaseVersionSource.Assets,
                    Owner = "EvotecIT",
                    Repository = "PSPublishModule",
                    Token = "token"
                }
            };
            var built = new PowerForgeReleaseResult {
                Success = true,
                ReleaseAssets = [zipPath],
                ReleaseAssetEntries = [
                    new PowerForgeReleaseAssetEntry {
                        Path = zipPath,
                        Version = "1.0.7",
                        Category = PowerForgeReleaseAssetCategory.Portable
                    }
                ]
            };

            var result = service.PublishBuiltReleaseOutputs(
                spec,
                new PowerForgeReleaseRequest {
                    ConfigPath = Path.Combine(root, "release.json")
                },
                built);

            Assert.True(result.Success);
            Assert.NotNull(result.UnifiedGitHubRelease);
            Assert.Equal(zipPath, Assert.Single(Assert.Single(publishCalls).AssetFilePaths));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PublishBuiltReleaseOutputs_RejectsDuplicateGitHubAssetFileNames()
    {
        var root = CreateSandbox();
        try
        {
            var firstDirectory = Path.Combine(root, "first");
            var secondDirectory = Path.Combine(root, "second");
            Directory.CreateDirectory(firstDirectory);
            Directory.CreateDirectory(secondDirectory);
            var firstAsset = Path.Combine(firstDirectory, "Sample-1.0.7.zip");
            var secondAsset = Path.Combine(secondDirectory, "Sample-1.0.7.zip");
            File.WriteAllText(firstAsset, "first");
            File.WriteAllText(secondAsset, "second");
            var publishCalls = new List<GitHubReleasePublishRequest>();
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages must not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Tools must not plan."),
                runTools: _ => throw new InvalidOperationException("Tools must not run."),
                publishGitHubRelease: request =>
                {
                    publishCalls.Add(request);
                    return new GitHubReleasePublishResult { Succeeded = true };
                });
            var built = new PowerForgeReleaseResult
            {
                Success = true,
                ReleaseAssets = [firstAsset, secondAsset],
                ReleaseAssetEntries =
                [
                    new PowerForgeReleaseAssetEntry { Path = firstAsset, Version = "1.0.7" },
                    new PowerForgeReleaseAssetEntry { Path = secondAsset, Version = "1.0.7" }
                ]
            };

            var result = service.PublishBuiltReleaseOutputs(
                new PowerForgeReleaseSpec
                {
                    GitHub = new PowerForgeReleaseGitHubOptions
                    {
                        Publish = true,
                        VersionSource = PowerForgeReleaseVersionSource.Assets,
                        Owner = "EvotecIT",
                        Repository = "Sample",
                        Token = "token"
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json")
                },
                built);

            Assert.False(result.Success);
            Assert.Contains("unique file names", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(firstAsset, result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(secondAsset, result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(publishCalls);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void PublishBuiltReleaseOutputs_SubmitsPreviouslyGeneratedWingetManifests()
    {
        var root = CreateSandbox();
        try
        {
            PowerForgeWingetSubmissionPlan? capturedPlan = null;
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages must not run during staged publishing."),
                planTools: (_, _, _) => throw new InvalidOperationException("Tools must not plan during staged publishing."),
                runTools: _ => throw new InvalidOperationException("Tools must not run during staged publishing."),
                loadDotNetToolsSpec: (_, _) => throw new InvalidOperationException("DotNet tools must not load during staged publishing."),
                planDotNetTools: (_, _, _, _) => throw new InvalidOperationException("DotNet tools must not plan during staged publishing."),
                runDotNetTools: _ => throw new InvalidOperationException("DotNet tools must not run during staged publishing."),
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub must not run during this WinGet-only publish."),
                submitWinget: plan =>
                {
                    capturedPlan = plan;
                    return new PowerForgeWingetSubmissionResult { Succeeded = true };
                });
            var spec = new PowerForgeReleaseSpec {
                Winget = new PowerForgeReleaseWingetOptions {
                    Enabled = true,
                    Submit = true,
                    Submission = new PowerForgeReleaseWingetSubmissionOptions {
                        Token = "secret"
                    }
                }
            };
            var manifestPath = Path.Combine(root, "manifests", "EvotecIT.Tool.yaml");
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            File.WriteAllText(manifestPath, "PackageIdentifier: EvotecIT.Tool");
            var built = new PowerForgeReleaseResult {
                Success = true,
                WingetManifestPaths = [manifestPath],
                WingetManifests = [
                    new PowerForgeWingetManifestArtifact {
                        PackageIdentifier = "EvotecIT.Tool",
                        PackageVersion = "1.0.0",
                        ManifestPath = manifestPath
                    }
                ]
            };

            var result = service.PublishBuiltReleaseOutputs(
                spec,
                new PowerForgeReleaseRequest {
                    ConfigPath = Path.Combine(root, "release.json")
                },
                built);

            Assert.True(result.Success);
            Assert.NotNull(capturedPlan);
            Assert.True(capturedPlan!.Enabled);
            Assert.True(result.WingetSubmission?.Succeeded);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ShouldPublishUnifiedGitHub_ToolsOnlyBuildMode_DoesNotPublish()
    {
        var spec = new PowerForgeReleaseSpec {
            GitHub = new PowerForgeReleaseGitHubOptions {
                Publish = true,
                VersionSource = PowerForgeReleaseVersionSource.Module
            }
        };
        var request = new PowerForgeReleaseRequest {
            ToolsOnly = true,
            ModuleRunMode = ConfigurationGateMode.Build
        };

        Assert.False(PowerForgeReleaseService.ShouldPublishUnifiedGitHub(spec, request, moduleSelected: false));
    }

    [Fact]
    public void TargetedToolRelease_DoesNotApplyTheInactiveModuleBuildGateToUnifiedGitHubPublishing()
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
                    Artefacts =
                    [
                        new PowerForgeToolReleaseArtifactResult
                        {
                            Target = "PowerForge",
                            Version = "1.0.7",
                            ZipPath = zipPath
                        }
                    ]
                },
                publishGitHubRelease: request =>
                {
                    publishCalls.Add(request);
                    return new GitHubReleasePublishResult { Succeeded = true };
                });

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Module = new PowerForgeModuleReleaseOptions
                    {
                        RepositoryRoot = root,
                        ScriptPath = "Missing-Build-Module.ps1"
                    },
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        Targets =
                        [
                            new PowerForgeToolReleaseTarget
                            {
                                Name = "PowerForge",
                                ProjectPath = "PowerForge.Cli.csproj"
                            }
                        ],
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
                    Targets = ["PowerForge"]
                });

            Assert.True(result.Success);
            Assert.Null(result.ModulePlan);
            Assert.Single(publishCalls);
            Assert.NotNull(result.UnifiedGitHubRelease);
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
