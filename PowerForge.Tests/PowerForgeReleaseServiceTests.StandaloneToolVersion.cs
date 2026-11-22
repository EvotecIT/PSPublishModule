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
                            TokenEnvName = "PATH",
                            TagTemplate = "{Target}-v{Version}",
                            ReleaseNameTemplate = "{Target} {Version}"
                        }
                    },
                    GitHub = new PowerForgeReleaseGitHubOptions
                    {
                        Publish = true,
                        Owner = "EvotecIT",
                        Repository = "PSPublishModule",
                        TokenEnvName = "PATH",
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

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, false, false, true)]
    public void Execute_StagesOnlyValidPowerForgeArchives(
        bool includeNativeArchive, bool missingImplementation, bool publishToolGitHub, bool moduleOwnsPackages)
    {
        static string HashBytes(byte[] bytes) =>
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

        var root = CreateSandbox();
        try
        {
            var runtime = includeNativeArchive ? "linux-x64" : "osx-arm64";
            var zip = Path.Combine(root, $"PowerForge-3.0.110-net10.0-{runtime}-SingleContained.zip");
            var nativeZip = Path.Combine(root, $"PowerForgeNative-3.0.110-{runtime}.zip");
            var installer = Path.Combine(root, "Install-PowerForgeTool.ps1");
            var lockManifest = Path.Combine(root, "PowerForge-tool-manifest.json");
            var executableBytes = System.Text.Encoding.UTF8.GetBytes("verified PowerForge executable");
            var nativeBytes = System.Text.Encoding.UTF8.GetBytes("native PowerForge launcher");
            var companionBytes = System.Text.Encoding.UTF8.GetBytes("managed companion");
            var implementationBytes = System.Text.Encoding.UTF8.GetBytes("managed implementation");
            using (var archive = System.IO.Compression.ZipFile.Open(zip, System.IO.Compression.ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("PowerForge");
                using var stream = entry.Open();
                stream.Write(executableBytes, 0, executableBytes.Length);
            }
            if (includeNativeArchive)
            {
                using var archive = System.IO.Compression.ZipFile.Open(nativeZip, System.IO.Compression.ZipArchiveMode.Create);
                using (var launcher = archive.CreateEntry("powerforge").Open())
                    launcher.Write(nativeBytes);
                using (var companion = archive.CreateEntry("full/powerforge-full").Open())
                    companion.Write(companionBytes);
                if (!missingImplementation)
                {
                    using var implementation = archive.CreateEntry("full/PowerForge.Cli.dll").Open();
                    implementation.Write(implementationBytes);
                }
            }
            File.WriteAllText(installer, "param()");
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
                                    Runtime = runtime,
                                    Framework = "net10.0",
                                    Flavor = PowerForgeToolReleaseFlavor.SingleContained,
                                    ZipPath = zip
                                }
                            ]
                        }
                    ]
                },
                runTools: plan => missingImplementation
                    ? throw new InvalidOperationException("The malformed archive must be rejected before building.")
                    : new PowerForgeToolReleaseResult
                {
                    Success = true,
                    Artefacts =
                    [
                        new PowerForgeToolReleaseArtifactResult
                        {
                            Target = "PowerForge",
                            Version = Assert.Single(plan.Targets).Version,
                            OutputName = "PowerForge",
                            Runtime = runtime,
                            Framework = "net10.0",
                            Flavor = PowerForgeToolReleaseFlavor.SingleContained,
                            OutputPath = root,
                            ExecutablePath = Path.Combine(root, "PowerForge"),
                            ZipPath = zip
                        }
                    ]
                },
                publishGitHubRelease: _ => throw new InvalidOperationException("GitHub must not run."));

            var spec = new PowerForgeReleaseSpec
            {
                Outputs = new PowerForgeReleaseOutputsOptions
                {
                    PowerForgeToolManifestPath = lockManifest,
                    AdditionalAssetPaths = [installer],
                    NativePowerForgeArchivePaths = includeNativeArchive ? new() { [runtime] = nativeZip } : new()
                },
                Tools = new PowerForgeToolReleaseSpec(),
                Module = moduleOwnsPackages ? new PowerForgeModuleReleaseOptions { IncludesPackages = true } : null,
                GitHub = new PowerForgeReleaseGitHubOptions
                {
                    Owner = "EvotecIT",
                    Repository = "PSPublishModule",
                    TagTemplate = "v{Version}"
                }
            };
            var request = new PowerForgeReleaseRequest
            {
                ConfigPath = Path.Combine(root, "release.json"),
                ToolsOnly = !moduleOwnsPackages,
                PublishToolGitHub = publishToolGitHub,
                ReleaseVersion = moduleOwnsPackages ? null : "3.0.110",
                ModuleRunMode = ConfigurationGateMode.Build
            };

            if (moduleOwnsPackages)
            {
                var error = Assert.Throws<ArgumentException>(() => service.Execute(spec, request));
                Assert.Contains("shared version resolved before building", error.Message, StringComparison.Ordinal);
                Assert.False(File.Exists(lockManifest));
                return;
            }

            if (publishToolGitHub)
            {
                var error = Assert.Throws<ArgumentException>(() => service.Execute(spec, request));
                Assert.Contains("build without publishing", error.Message, StringComparison.Ordinal);
                Assert.False(File.Exists(lockManifest));
                return;
            }

            if (missingImplementation)
            {
                var error = Assert.Throws<InvalidOperationException>(() => service.Execute(spec, request));
                Assert.Contains("full/PowerForge.Cli.dll", error.Message, StringComparison.Ordinal);
                Assert.False(File.Exists(lockManifest));
                return;
            }

            var result = service.Execute(spec, request);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Contains(lockManifest, result.ReleaseAssets);
            Assert.Contains(installer, result.ReleaseAssets);
            var manifestJson = File.ReadAllText(lockManifest);
            var schemaPath = Path.Combine(PowerForgeCliTestHost.FindRepositoryRoot(), "Schemas", "powerforge.tool.schema.json");
            var schema = Json.Schema.JsonSchema.FromText(File.ReadAllText(schemaPath));
            var manifestNode = System.Text.Json.Nodes.JsonNode.Parse(manifestJson)!;
            Assert.True(schema.Evaluate(manifestNode).IsValid);
            using var document = System.Text.Json.JsonDocument.Parse(manifestJson);
            var json = document.RootElement;
            Assert.Equal(2, json.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("3.0.110", json.GetProperty("version").GetString());
            Assert.Equal("v3.0.110", json.GetProperty("releaseTag").GetString());
            Assert.False(json.TryGetProperty("commit", out _));
            var asset = json.GetProperty("assets").GetProperty(runtime);
            var selectedZip = includeNativeArchive ? nativeZip : zip;
            Assert.Contains(selectedZip, result.ReleaseAssets);
            Assert.Equal(Path.GetFileName(selectedZip), asset.GetProperty("name").GetString());
            Assert.Equal(
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(selectedZip))).ToLowerInvariant(),
                asset.GetProperty("sha256").GetString());
            Assert.Equal(
                Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(includeNativeArchive ? nativeBytes : executableBytes)).ToLowerInvariant(),
                asset.GetProperty("executableSha256").GetString());
            if (includeNativeArchive)
            {
                Assert.Empty(Assert.Single(result.ToolPlan!.Targets).Combinations);
                Assert.DoesNotContain(zip, result.ReleaseAssets);
                Assert.Equal("powerforge", asset.GetProperty("executableName").GetString());
                var launcherDigest = HashBytes(companionBytes);
                var implementationDigest = HashBytes(implementationBytes);
                var payload = $"full/PowerForge.Cli.dll\0{implementationDigest}\0full/powerforge-full\0{launcherDigest}\0";
                Assert.Equal(HashBytes(System.Text.Encoding.UTF8.GetBytes(payload)),
                    asset.GetProperty("companionPayloadSha256").GetString());

                var nativeAsset = manifestNode["assets"]![runtime]!.AsObject();
                nativeAsset["executableName"] = "powerforge.exe";
                Assert.False(schema.Evaluate(manifestNode).IsValid);
                nativeAsset["executableName"] = "powerforge";
                nativeAsset.Remove("companionPayloadSha256");
                Assert.False(schema.Evaluate(manifestNode).IsValid);
            }
            else
            {
                Assert.Single(Assert.Single(result.ToolPlan!.Targets).Combinations);
                Assert.False(asset.TryGetProperty("executableName", out _));
                Assert.False(asset.TryGetProperty("companionPayloadSha256", out _));
            }
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
                    UseControlledSourceProvenance = true,
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
    public void ValidatePowerForgeToolManifestStaging_allows_optional_commit_provenance()
    {
        var spec = new PowerForgeReleaseSpec
        {
            Outputs = new PowerForgeReleaseOutputsOptions
            {
                PowerForgeToolManifestPath = "PowerForge-tool-manifest.json"
            },
            GitHub = new PowerForgeReleaseGitHubOptions
            {
                Owner = "EvotecIT",
                Repository = "PSPublishModule"
            }
        };

        PowerForgeReleaseService.ValidatePowerForgeToolManifestStaging(spec);
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
                    UseControlledSourceProvenance = true,
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
