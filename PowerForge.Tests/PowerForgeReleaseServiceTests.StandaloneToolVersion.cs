namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void Execute_ToolOnlyReleaseUsesExplicitExactVersion()
    {
        string? capturedVersion = null;
        var service = CreateService(request =>
        {
            capturedVersion = request.ResolvedReleaseVersion;
            return new PowerForgeToolReleasePlan();
        });

        var result = service.Execute(
            new PowerForgeReleaseSpec { Tools = new PowerForgeToolReleaseSpec() },
            new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(Path.GetTempPath(), "release.json"),
                ToolsOnly = true,
                PlanOnly = true,
                ReleaseVersion = "3.0.80"
            });

        Assert.True(result.Success);
        Assert.Equal("3.0.80", capturedVersion);
    }

    [Theory]
    [InlineData("3.0")]
    [InlineData("3.0.80-preview.1")]
    [InlineData("3.0.X")]
    public void Execute_ToolOnlyReleaseRejectsNonExactVersion(string version)
    {
        var service = CreateService(_ => new PowerForgeToolReleasePlan());

        var error = Assert.Throws<ArgumentException>(() => service.Execute(
            new PowerForgeReleaseSpec { Tools = new PowerForgeToolReleaseSpec() },
            new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(Path.GetTempPath(), "release.json"),
                ToolsOnly = true,
                PlanOnly = true,
                ReleaseVersion = version
            }));

        Assert.Contains("x.y.z", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Execute_ReleaseVersionRequiresExplicitToolOnlyScope()
    {
        var service = CreateService(_ => new PowerForgeToolReleasePlan());

        var error = Assert.Throws<InvalidOperationException>(() => service.Execute(
            new PowerForgeReleaseSpec { Tools = new PowerForgeToolReleaseSpec() },
            new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(Path.GetTempPath(), "release.json"),
                PlanOnly = true,
                ReleaseVersion = "3.0.80"
            }));

        Assert.Contains("tool-only", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_StandaloneVersionOwnsThePowerForgeReleaseTag()
    {
        var archive = Path.GetTempFileName();
        try
        {
            var published = new List<GitHubReleasePublishRequest>();
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages must not run."),
                planTools: (_, _, request) => new PowerForgeToolReleasePlan
                {
                    ProjectRoot = Path.GetTempPath(),
                    Configuration = "Release",
                    Targets =
                    [
                        new PowerForgeToolReleaseTargetPlan
                        {
                            Name = "PowerForge",
                            Version = request.ResolvedReleaseVersion!,
                            OutputName = "PowerForge",
                            ArtifactRootPath = Path.GetTempPath()
                        }
                    ]
                },
                runTools: plan => new PowerForgeToolReleaseResult
                {
                    Success = true,
                    Artefacts =
                    [
                        new PowerForgeToolReleaseArtifactResult
                        {
                            Target = "PowerForge",
                            Version = Assert.Single(plan.Targets).Version,
                            OutputName = "PowerForge",
                            Runtime = "osx-arm64",
                            Framework = "net10.0",
                            Flavor = PowerForgeToolReleaseFlavor.SingleContained,
                            OutputPath = Path.GetTempPath(),
                            ExecutablePath = Path.Combine(Path.GetTempPath(), "PowerForge"),
                            ZipPath = archive
                        }
                    ]
                },
                publishGitHubRelease: request =>
                {
                    published.Add(request);
                    return new GitHubReleasePublishResult { Succeeded = true };
                });

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Tools = new PowerForgeToolReleaseSpec
                    {
                        GitHub = new PowerForgeToolReleaseGitHubOptions
                        {
                            Publish = true,
                            Owner = "EvotecIT",
                            Repository = "PSPublishModule",
                            Token = "token",
                            TagTemplate = "{Target}-v{Version}",
                            ReleaseNameTemplate = "{Target} {Version}"
                        }
                    },
                    GitHub = new PowerForgeReleaseGitHubOptions
                    {
                        Publish = true,
                        Owner = "EvotecIT",
                        Repository = "PSPublishModule",
                        Token = "token",
                        TagTemplate = "v{Version}",
                        ReleaseNameTemplate = "PSPublishModule {Version}"
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(Path.GetTempPath(), "release.json"),
                    ToolsOnly = true,
                    PublishToolGitHub = true,
                    ReleaseVersion = "3.0.80"
                });

            Assert.True(result.Success);
            var release = Assert.Single(published);
            Assert.Equal("PowerForge-v3.0.80", release.TagName);
            Assert.Equal("PowerForge 3.0.80", release.ReleaseName);
        }
        finally
        {
            File.Delete(archive);
        }
    }

    [Fact]
    public void Execute_WritesConsumerToolManifestAndIncludesInstallerAsset()
    {
        var root = CreateSandbox();
        try
        {
            var zip = Path.Combine(root, "PowerForge-3.0.110-net10.0-osx-arm64-SingleContained.zip");
            var installer = Path.Combine(root, "Install-PowerForgeTool.ps1");
            var lockManifest = Path.Combine(root, "PowerForge-tool-manifest.json");
            var executableBytes = System.Text.Encoding.UTF8.GetBytes("verified PowerForge executable");
            using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("PowerForge");
                using var stream = entry.Open();
                stream.Write(executableBytes, 0, executableBytes.Length);
            }
            File.WriteAllText(installer, "param()");
            RunSnapshotGit(root, "init", "--quiet");
            RunSnapshotGit(root, "config", "user.name", "PowerForge Tests");
            RunSnapshotGit(root, "config", "user.email", "powerforge-tests@example.invalid");
            RunSnapshotGit(root, "add", ".");
            RunSnapshotGit(root, "commit", "--quiet", "-m", "exact source");
            var commit = RunSnapshotGit(root, "rev-parse", "HEAD").Trim();
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages must not run."),
                planTools: (_, _, request) => new PowerForgeToolReleasePlan
                {
                    ProjectRoot = root,
                    Configuration = "Release",
                    Targets =
                    [
                        new PowerForgeToolReleaseTargetPlan
                        {
                            Name = "PowerForge",
                            Version = request.ResolvedReleaseVersion!,
                            OutputName = "PowerForge",
                            ArtifactRootPath = root,
                            Combinations =
                            [
                                new PowerForgeToolReleaseCombinationPlan
                                {
                                    Runtime = "osx-arm64",
                                    Framework = "net10.0",
                                    Flavor = PowerForgeToolReleaseFlavor.SingleContained,
                                    ZipPath = zip
                                }
                            ]
                        }
                    ]
                },
                runTools: plan => new PowerForgeToolReleaseResult
                {
                    Success = true,
                    Artefacts =
                    [
                        new PowerForgeToolReleaseArtifactResult
                        {
                            Target = "PowerForge",
                            Version = Assert.Single(plan.Targets).Version,
                            OutputName = "PowerForge",
                            Runtime = "osx-arm64",
                            Framework = "net10.0",
                            Flavor = PowerForgeToolReleaseFlavor.SingleContained,
                            OutputPath = root,
                            ExecutablePath = Path.Combine(root, "PowerForge"),
                            ZipPath = zip
                        }
                    ]
                },
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub must not run."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Outputs = new PowerForgeReleaseOutputsOptions
                    {
                        PowerForgeToolManifestPath = lockManifest,
                        AdditionalAssetPaths = [installer]
                    },
                    Tools = new PowerForgeToolReleaseSpec(),
                    GitHub = new PowerForgeReleaseGitHubOptions
                    {
                        Owner = "EvotecIT",
                        Repository = "PSPublishModule",
                        Commitish = commit,
                        TagTemplate = "v{Version}"
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    ToolsOnly = true,
                    ReleaseVersion = "3.0.110"
                });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Contains(lockManifest, result.ReleaseAssets);
            Assert.Contains(installer, result.ReleaseAssets);
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(lockManifest));
            var json = document.RootElement;
            Assert.Equal(2, json.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("3.0.110", json.GetProperty("version").GetString());
            Assert.Equal("v3.0.110", json.GetProperty("releaseTag").GetString());
            Assert.Equal(commit, json.GetProperty("commit").GetString());
            var asset = json.GetProperty("assets").GetProperty("osx-arm64");
            Assert.Equal(Path.GetFileName(zip), asset.GetProperty("name").GetString());
            Assert.Equal(
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(zip))).ToLowerInvariant(),
                asset.GetProperty("sha256").GetString());
            Assert.Equal(
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(executableBytes)).ToLowerInvariant(),
                asset.GetProperty("executableSha256").GetString());
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_DotNetPublishWritesConsumerToolManifest()
    {
        var root = CreateSandbox();
        try
        {
            var zip = Path.Combine(root, "PowerForge-3.0.110-net10.0-osx-arm64-SingleContained.zip");
            var lockManifest = Path.Combine(root, "PowerForge-tool-manifest.json");
            var executableBytes = System.Text.Encoding.UTF8.GetBytes("verified dotnet-publish executable");
            using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("PowerForge");
                using var stream = entry.Open();
                stream.Write(executableBytes, 0, executableBytes.Length);
            }
            RunSnapshotGit(root, "init", "--quiet");
            RunSnapshotGit(root, "config", "user.name", "PowerForge Tests");
            RunSnapshotGit(root, "config", "user.email", "powerforge-tests@example.invalid");
            RunSnapshotGit(root, "add", ".");
            RunSnapshotGit(root, "commit", "--quiet", "-m", "exact source");
            var sourceCommit = RunSnapshotGit(root, "rev-parse", "HEAD").Trim();

            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages must not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools must not run."),
                runTools: _ => throw new InvalidOperationException("Legacy tools must not run."),
                loadDotNetToolsSpec: (_, configPath) => (new DotNetPublishSpec(), configPath),
                planDotNetTools: (_, _, _, _) => new DotNetPublishPlan
                {
                    ProjectRoot = root,
                    Targets =
                    [
                        new DotNetPublishTargetPlan
                        {
                            Name = "PowerForge",
                            Version = "3.0.110",
                            Combinations =
                            [
                                new DotNetPublishTargetCombination
                                {
                                    Framework = "net10.0",
                                    Runtime = "osx-arm64",
                                    Style = DotNetPublishStyle.PortableCompat
                                }
                            ]
                        }
                    ]
                },
                runDotNetTools: _ => new DotNetPublishResult
                {
                    Succeeded = true,
                    Artefacts =
                    [
                        new DotNetPublishArtefactResult
                        {
                            Target = "PowerForge",
                            Runtime = "osx-arm64",
                            Framework = "net10.0",
                            Style = DotNetPublishStyle.PortableCompat,
                            OutputDir = root,
                            ZipPath = zip
                        }
                    ]
                },
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub must not run."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Outputs = new PowerForgeReleaseOutputsOptions { PowerForgeToolManifestPath = lockManifest },
                    Tools = new PowerForgeToolReleaseSpec { DotNetPublish = new DotNetPublishSpec() },
                    GitHub = new PowerForgeReleaseGitHubOptions
                    {
                        Owner = "EvotecIT",
                        Repository = "PSPublishModule",
                        Commitish = sourceCommit,
                        TagTemplate = "v{Version}"
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    ToolsOnly = true,
                    ReleaseVersion = "3.0.110"
                });

            Assert.True(result.Success, result.ErrorMessage);
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(lockManifest));
            Assert.Equal(2, document.RootElement.GetProperty("schemaVersion").GetInt32());
            var asset = document.RootElement.GetProperty("assets").GetProperty("osx-arm64");
            Assert.Equal(Path.GetFileName(zip), asset.GetProperty("name").GetString());
            Assert.Equal(
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(executableBytes)).ToLowerInvariant(),
                asset.GetProperty("executableSha256").GetString());
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void ValidatePowerForgeToolManifestStaging_rejects_tool_archive_renaming()
    {
        var spec = new PowerForgeReleaseSpec
        {
            Outputs = new PowerForgeReleaseOutputsOptions
            {
                PowerForgeToolManifestPath = "PowerForge-tool-manifest.json",
                Staging = new PowerForgeReleaseStagingOptions
                {
                    ToolsNameTemplate = "renamed-{FileName}"
                }
            }
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            PowerForgeReleaseService.ValidatePowerForgeToolManifestStaging(spec));

        Assert.Contains("exact published tool archive", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Execute_DotNetPublishToolOutputExcludedSkipsConsumerToolManifest()
    {
        var root = CreateSandbox();
        try
        {
            var lockManifest = Path.Combine(root, "PowerForge-tool-manifest.json");
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages must not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools must not run."),
                runTools: _ => throw new InvalidOperationException("Legacy tools must not run."),
                loadDotNetToolsSpec: (_, configPath) => (new DotNetPublishSpec(), configPath),
                planDotNetTools: (_, _, _, _) => new DotNetPublishPlan
                {
                    ProjectRoot = root,
                    Targets =
                    [
                        new DotNetPublishTargetPlan
                        {
                            Name = "PowerForge",
                            Version = "3.0.110",
                            Combinations =
                            [
                                new DotNetPublishTargetCombination
                                {
                                    Framework = "net10.0",
                                    Runtime = "osx-arm64",
                                    Style = DotNetPublishStyle.PortableCompat
                                }
                            ]
                        }
                    ]
                },
                runDotNetTools: _ => new DotNetPublishResult { Succeeded = true },
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub must not run."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Outputs = new PowerForgeReleaseOutputsOptions { PowerForgeToolManifestPath = lockManifest },
                    Tools = new PowerForgeToolReleaseSpec { DotNetPublish = new DotNetPublishSpec() }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    ToolsOnly = true,
                    ReleaseVersion = "3.0.110",
                    SkipToolOutputs = [PowerForgeReleaseToolOutputKind.Tool]
                });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.False(File.Exists(lockManifest));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData("PowerForgeWeb", "SingleContained")]
    [InlineData("PowerForge", "SingleFx")]
    public void Execute_NonStandaloneToolSelectionSkipsConsumerToolManifest(
        string targetName,
        string flavorName)
    {
        var root = CreateSandbox();
        try
        {
            var flavor = Enum.Parse<PowerForgeToolReleaseFlavor>(flavorName);
            var zip = Path.Combine(root, $"{targetName}-3.0.110.zip");
            var lockManifest = Path.Combine(root, "PowerForge-tool-manifest.json");
            File.WriteAllText(zip, "web tool");
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages must not run."),
                planTools: (_, _, _) => new PowerForgeToolReleasePlan
                {
                    ProjectRoot = root,
                    Targets =
                    [
                        new PowerForgeToolReleaseTargetPlan
                        {
                            Name = targetName,
                            Version = "3.0.110",
                            Combinations =
                            [
                                new PowerForgeToolReleaseCombinationPlan
                                {
                                    Runtime = "osx-arm64",
                                    Framework = "net10.0",
                                    Flavor = flavor,
                                    ZipPath = zip
                                }
                            ]
                        }
                    ]
                },
                runTools: _ => new PowerForgeToolReleaseResult
                {
                    Success = true,
                    Artefacts =
                    [
                        new PowerForgeToolReleaseArtifactResult
                        {
                            Target = targetName,
                            Version = "3.0.110",
                            Runtime = "osx-arm64",
                            Framework = "net10.0",
                            Flavor = flavor,
                            ExecutablePath = Path.Combine(root, targetName),
                            ZipPath = zip
                        }
                    ]
                },
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub must not run."));

            var result = service.Execute(
                new PowerForgeReleaseSpec
                {
                    Outputs = new PowerForgeReleaseOutputsOptions { PowerForgeToolManifestPath = lockManifest },
                    Tools = new PowerForgeToolReleaseSpec()
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    ToolsOnly = true,
                    ReleaseVersion = "3.0.110"
                });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.False(File.Exists(lockManifest));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData("SingleContained", true)]
    [InlineData("Portable", true)]
    [InlineData("PortableCompat", true)]
    [InlineData("PortableSize", true)]
    [InlineData("AotSpeed", true)]
    [InlineData("AotSize", true)]
    [InlineData("SingleFx", false)]
    [InlineData("Fx", false)]
    [InlineData("FrameworkDependent", false)]
    [InlineData(null, false)]
    public void ToolManifest_accepts_only_self_contained_styles(string? style, bool expected)
        => Assert.Equal(expected, PowerForgeReleaseService.IsStandalonePowerForgeArtifactStyle(style));

    [Theory]
    [InlineData("win-x64", true)]
    [InlineData("win-arm64", true)]
    [InlineData("linux-x64", true)]
    [InlineData("linux-arm64", true)]
    [InlineData("osx-x64", true)]
    [InlineData("osx-arm64", true)]
    [InlineData("linux-musl-x64", false)]
    [InlineData("win-x86", false)]
    [InlineData(null, false)]
    public void ToolManifest_accepts_only_installer_supported_runtimes(string? runtime, bool expected)
        => Assert.Equal(expected, PowerForgeReleaseService.IsSupportedPowerForgeToolManifestRuntime(runtime));

    [Theory]
    [InlineData("v{Version}", true)]
    [InlineData("release-{Repository}-{Version}", true)]
    [InlineData("v{Version}-{Date}", false)]
    [InlineData("v{Version}-{UtcDateTime}", false)]
    [InlineData("v{Version}-{Timestamp}", false)]
    [InlineData("v{Version}-{UtcTimestamp}", false)]
    public void ToolManifest_requires_a_deterministic_release_tag(string template, bool expected)
        => Assert.Equal(expected, PowerForgeReleaseService.IsDeterministicPowerForgeToolManifestTagTemplate(template));

    [Theory]
    [InlineData("v3.0.110", true)]
    [InlineData("PowerForge-v3.0.110", true)]
    [InlineData("PowerForge/v3.0.110", false)]
    [InlineData("PowerForge v3.0.110", false)]
    [InlineData("", false)]
    public void ToolManifest_accepts_only_installer_safe_release_tags(string? releaseTag, bool expected)
        => Assert.Equal(expected, PowerForgeReleaseService.IsSupportedPowerForgeToolManifestReleaseTag(releaseTag));

    [Theory]
    [InlineData("EvotecIT/PSPublishModule", true)]
    [InlineData("evotec.it/PowerForge_repo", true)]
    [InlineData("EvotecIT/Power Forge", false)]
    [InlineData("EvotecIT/PowerForge/CLI", false)]
    [InlineData("", false)]
    public void ToolManifest_accepts_only_installer_safe_repositories(string? repository, bool expected)
        => Assert.Equal(expected, PowerForgeReleaseService.IsSupportedPowerForgeToolManifestRepository(repository));

    [Theory]
    [InlineData("PowerForge-3.0.110-win-x64.zip", true)]
    [InlineData("PowerForge 3.0.110 win-x64.zip", false)]
    [InlineData("PowerForge-3.0.110-win-x64.tar.gz", false)]
    [InlineData("../PowerForge.zip", false)]
    [InlineData("", false)]
    public void ToolManifest_accepts_only_installer_safe_archive_names(string? archiveName, bool expected)
        => Assert.Equal(expected, PowerForgeReleaseService.IsSupportedPowerForgeToolManifestArchiveName(archiveName));

    [Fact]
    public void ToolManifest_normalizes_exact_commit_and_rejects_symbolic_commitish()
    {
        Assert.Equal(
            "abcdef0123456789abcdef0123456789abcdef01",
            PowerForgeReleaseService.NormalizePowerForgeToolManifestCommit(
                "  ABCDEF0123456789ABCDEF0123456789ABCDEF01  "));
        Assert.Null(PowerForgeReleaseService.NormalizePowerForgeToolManifestCommit(" "));
        Assert.Throws<InvalidOperationException>(
            () => PowerForgeReleaseService.NormalizePowerForgeToolManifestCommit("main"));
        Assert.Throws<InvalidOperationException>(
            () => PowerForgeReleaseService.NormalizePowerForgeToolManifestCommit("abcdef0"));
    }

    private static PowerForgeReleaseService CreateService(
        Func<PowerForgeReleaseRequest, PowerForgeToolReleasePlan> planTools)
        => new(
            new NullLogger(),
            executePackages: (_, _, _) => throw new InvalidOperationException("Packages must not run."),
            planTools: (_, _, request) => planTools(request),
            runTools: _ => throw new InvalidOperationException("Tools must not run in a plan."),
            publishGitHubRelease: _ => throw new InvalidOperationException("GitHub must not run in a plan."));
}
