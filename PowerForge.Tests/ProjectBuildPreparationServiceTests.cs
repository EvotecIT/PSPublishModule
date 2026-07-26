using PowerForge;

namespace PowerForge.Tests;

public sealed class ProjectBuildPreparationServiceTests
{
    [Fact]
    public void Prepare_enables_full_run_when_no_actions_are_configured()
    {
        var service = new ProjectBuildPreparationService();

        var context = service.Prepare(
            new ProjectBuildConfiguration(),
            Directory.GetCurrentDirectory(),
            null,
            new ProjectBuildRequestedActions());

        Assert.True(context.UpdateVersions);
        Assert.True(context.Build);
        Assert.True(context.PublishNuget);
        Assert.True(context.PublishGitHub);
        Assert.True(context.HasWork);
        Assert.True(context.Spec.Pack);
        Assert.True(context.Spec.Publish);
    }

    [Fact]
    public void Prepare_propagates_package_version_alignment()
    {
        var service = new ProjectBuildPreparationService();

        var context = service.Prepare(
            new ProjectBuildConfiguration
            {
                ExpectedVersion = "2.0.X",
                AlignPackageVersions = true
            },
            Directory.GetCurrentDirectory(),
            null,
            new ProjectBuildRequestedActions());

        Assert.True(context.Spec.AlignPackageVersions);
    }

    [Fact]
    public void Prepare_propagates_internal_release_version_floor()
    {
        var service = new ProjectBuildPreparationService();

        var context = service.Prepare(
            new ProjectBuildConfiguration(),
            Directory.GetCurrentDirectory(),
            null,
            new ProjectBuildRequestedActions
            {
                ReleaseVersionFloor = "2.1.7",
                ReleaseVersionFloorProject = "Mailozaurr"
            });

        Assert.Equal("2.1.7", context.Spec.ReleaseVersionFloor);
        Assert.Equal("Mailozaurr", context.Spec.ReleaseVersionFloorProject);
    }

    [Fact]
    public void Prepare_honors_explicit_action_overrides_and_derives_default_output_paths()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-projectbuild-prepare-" + Guid.NewGuid().ToString("N")));

        try
        {
            var service = new ProjectBuildPreparationService();
            var config = new ProjectBuildConfiguration
            {
                RootPath = "repo",
                StagingPath = "artifacts",
                PublishNuget = true,
                PublishGitHub = true
            };

            var context = service.Prepare(
                config,
                root.FullName,
                null,
                new ProjectBuildRequestedActions
                {
                    PublishNuget = false,
                    PublishGitHub = true
                });

            Assert.False(context.PublishNuget);
            Assert.True(context.PublishGitHub);
            Assert.Equal(Path.Combine(root.FullName, "repo"), context.RootPath);
            Assert.Equal(Path.Combine(root.FullName, "repo", "artifacts"), context.StagingPath);
            Assert.Equal(Path.Combine(root.FullName, "repo", "artifacts", "packages"), context.OutputPath);
            Assert.Equal(Path.Combine(root.FullName, "repo", "artifacts", "releases"), context.ReleaseZipOutputPath);
            Assert.True(context.Spec.Pack);
            Assert.False(context.Spec.Publish);
            Assert.True(context.Spec.CreateReleaseZip);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Prepare_resolves_credentials_tokens_and_spec_settings()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-projectbuild-prepare-secret-" + Guid.NewGuid().ToString("N")));
        var publishEnv = "PF_TEST_PUBLISH_" + Guid.NewGuid().ToString("N");
        var gitHubEnv = "PF_TEST_GITHUB_" + Guid.NewGuid().ToString("N");

        try
        {
            var secretPath = Path.Combine(root.FullName, "nuget.secret");
            File.WriteAllText(secretPath, " secret-from-file ");
            Environment.SetEnvironmentVariable(publishEnv, "publish-from-env");
            Environment.SetEnvironmentVariable(gitHubEnv, "github-from-env");

            var service = new ProjectBuildPreparationService();
            var config = new ProjectBuildConfiguration
            {
                NugetCredentialUserName = "user",
                NugetCredentialSecretFilePath = secretPath,
                PublishApiKeyEnvName = publishEnv,
                GitHubAccessTokenEnvName = gitHubEnv,
                CertificateStore = "LocalMachine",
                Configuration = "Debug",
                PackStrategy = "MSBuild",
                IncludeSymbols = true,
                PublishNuget = true,
                PublishGitHub = true
            };

            var context = service.Prepare(
                config,
                root.FullName,
                "plan.json",
                new ProjectBuildRequestedActions());

            Assert.Equal("publish-from-env", context.PublishApiKey);
            Assert.Equal("github-from-env", context.GitHubToken);
            Assert.Equal(Path.Combine(root.FullName, "plan.json"), context.PlanOutputPath);
            Assert.NotNull(context.Spec.VersionSourceCredential);
            Assert.Equal("user", context.Spec.VersionSourceCredential!.UserName);
            Assert.Equal("secret-from-file", context.Spec.VersionSourceCredential.Secret);
            Assert.Equal(CertificateStoreLocation.LocalMachine, context.Spec.CertificateStore);
            Assert.Equal("Debug", context.Spec.Configuration);
            Assert.Equal(DotNetRepositoryPackStrategy.MSBuild, context.Spec.PackStrategy);
            Assert.True(context.Spec.IncludeSymbols);
        }
        finally
        {
            Environment.SetEnvironmentVariable(publishEnv, null);
            Environment.SetEnvironmentVariable(gitHubEnv, null);
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Prepare_enables_assembly_and_package_signing_by_default_when_certificate_is_configured()
    {
        var service = new ProjectBuildPreparationService();

        var context = service.Prepare(
            new ProjectBuildConfiguration
            {
                CertificateThumbprint = "ABC123",
                Build = true
            },
            Directory.GetCurrentDirectory(),
            null,
            new ProjectBuildRequestedActions());

        Assert.True(context.Spec.SignAssemblies);
        Assert.True(context.Spec.SignPackages);
    }

    [Fact]
    public void Prepare_honors_explicit_signing_opt_outs()
    {
        var service = new ProjectBuildPreparationService();

        var context = service.Prepare(
            new ProjectBuildConfiguration
            {
                CertificateThumbprint = "ABC123",
                SignAssemblies = false,
                SignPackages = false,
                Build = true
            },
            Directory.GetCurrentDirectory(),
            null,
            new ProjectBuildRequestedActions());

        Assert.False(context.Spec.SignAssemblies);
        Assert.False(context.Spec.SignPackages);
    }

    [Fact]
    public void Prepare_resolves_github_packages_feed_from_shared_settings()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-projectbuild-githubpackages-" + Guid.NewGuid().ToString("N")));
        var gitHubEnv = "PF_TEST_GITHUB_PACKAGES_" + Guid.NewGuid().ToString("N");

        try
        {
            Environment.SetEnvironmentVariable(gitHubEnv, "github-packages-token");

            var service = new ProjectBuildPreparationService();
            var config = new ProjectBuildConfiguration
            {
                UseGitHubPackages = true,
                GitHubPackagesOwner = "EvotecIT",
                GitHubAccessTokenEnvName = gitHubEnv,
                PublishNuget = true
            };

            var context = service.Prepare(
                config,
                root.FullName,
                null,
                new ProjectBuildRequestedActions());

            Assert.Equal("github-packages-token", context.PublishApiKey);
            Assert.Equal("github-packages-token", context.GitHubToken);
            Assert.Equal("https://nuget.pkg.github.com/EvotecIT/index.json", context.Spec.PublishSource);
            Assert.Equal(new[] { "https://nuget.pkg.github.com/EvotecIT/index.json" }, context.Spec.VersionSources);
            Assert.NotNull(context.Spec.VersionSourceCredential);
            Assert.Equal("EvotecIT", context.Spec.VersionSourceCredential!.UserName);
            Assert.Equal("github-packages-token", context.Spec.VersionSourceCredential.Secret);
        }
        finally
        {
            Environment.SetEnvironmentVariable(gitHubEnv, null);
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Prepare_uses_github_token_for_explicit_github_packages_publish_source()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-projectbuild-githubpackages-source-" + Guid.NewGuid().ToString("N")));
        var gitHubEnv = "PF_TEST_GITHUB_PACKAGES_SOURCE_" + Guid.NewGuid().ToString("N");

        try
        {
            Environment.SetEnvironmentVariable(gitHubEnv, "explicit-source-token");

            var service = new ProjectBuildPreparationService();
            var config = new ProjectBuildConfiguration
            {
                PublishSource = " https://nuget.pkg.github.com/EvotecIT/index.json ",
                GitHubAccessTokenEnvName = gitHubEnv,
                PublishNuget = true
            };

            var context = service.Prepare(
                config,
                root.FullName,
                null,
                new ProjectBuildRequestedActions());

            Assert.Equal("explicit-source-token", context.PublishApiKey);
            Assert.Equal("https://nuget.pkg.github.com/EvotecIT/index.json", context.Spec.PublishSource);
            var credential = Assert.Single(context.Spec.VersionSourceCredentials!);
            Assert.Equal("https://nuget.pkg.github.com/EvotecIT/index.json", credential.Key);
            Assert.Equal("EvotecIT", credential.Value.UserName);
            Assert.Equal("explicit-source-token", credential.Value.Secret);
        }
        finally
        {
            Environment.SetEnvironmentVariable(gitHubEnv, null);
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Prepare_derives_github_packages_owner_from_explicit_version_source()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-projectbuild-githubpackages-explicit-source-" + Guid.NewGuid().ToString("N")));
        var gitHubEnv = "PF_TEST_GITHUB_PACKAGES_EXPLICIT_" + Guid.NewGuid().ToString("N");

        try
        {
            Environment.SetEnvironmentVariable(gitHubEnv, "explicit-version-source-token");

            var service = new ProjectBuildPreparationService();
            var config = new ProjectBuildConfiguration
            {
                NugetSource = new[] { "https://nuget.pkg.github.com/EvotecIT/index.json" },
                GitHubAccessTokenEnvName = gitHubEnv
            };

            var context = service.Prepare(
                config,
                root.FullName,
                null,
                new ProjectBuildRequestedActions());

            Assert.NotNull(context.Spec.VersionSourceCredential);
            Assert.Equal("EvotecIT", context.Spec.VersionSourceCredential!.UserName);
            Assert.Equal("explicit-version-source-token", context.Spec.VersionSourceCredential.Secret);
            var scopedCredential = Assert.Single(context.Spec.VersionSourceCredentials!);
            Assert.Equal("https://nuget.pkg.github.com/EvotecIT/index.json", scopedCredential.Key);
            Assert.Equal("EvotecIT", scopedCredential.Value.UserName);
            Assert.Equal("explicit-version-source-token", scopedCredential.Value.Secret);
        }
        finally
        {
            Environment.SetEnvironmentVariable(gitHubEnv, null);
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Prepare_does_not_apply_github_packages_token_to_mixed_version_sources()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-projectbuild-githubpackages-mixed-source-" + Guid.NewGuid().ToString("N")));
        var gitHubEnv = "PF_TEST_GITHUB_PACKAGES_MIXED_" + Guid.NewGuid().ToString("N");

        try
        {
            Environment.SetEnvironmentVariable(gitHubEnv, "mixed-version-source-token");

            var service = new ProjectBuildPreparationService();
            var config = new ProjectBuildConfiguration
            {
                NugetSource = new[]
                {
                    "https://nuget.pkg.github.com/EvotecIT/index.json",
                    "https://api.nuget.org/v3/index.json"
                },
                GitHubAccessTokenEnvName = gitHubEnv
            };

            var context = service.Prepare(
                config,
                root.FullName,
                null,
                new ProjectBuildRequestedActions());

            Assert.Null(context.Spec.VersionSourceCredential);
            Assert.Equal(config.NugetSource, context.Spec.VersionSources);
            var scopedCredential = Assert.Single(context.Spec.VersionSourceCredentials!);
            Assert.Equal("https://nuget.pkg.github.com/EvotecIT/index.json", scopedCredential.Key);
            Assert.Equal("EvotecIT", scopedCredential.Value.UserName);
            Assert.Equal("mixed-version-source-token", scopedCredential.Value.Secret);
        }
        finally
        {
            Environment.SetEnvironmentVariable(gitHubEnv, null);
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Prepare_resolves_github_packages_credentials_from_version_track_sources()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-projectbuild-githubpackages-track-source-" + Guid.NewGuid().ToString("N")));
        var gitHubEnv = "PF_TEST_GITHUB_PACKAGES_TRACK_" + Guid.NewGuid().ToString("N");

        try
        {
            Environment.SetEnvironmentVariable(gitHubEnv, "track-source-token");

            var service = new ProjectBuildPreparationService();
            var config = new ProjectBuildConfiguration
            {
                GitHubAccessTokenEnvName = gitHubEnv,
                VersionTracks = new Dictionary<string, ProjectBuildVersionTrack>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PowerForge"] = new()
                    {
                        ExpectedVersion = "1.2.3",
                        AnchorProject = "PowerForge",
                        NugetSource = new[] { "https://nuget.pkg.github.com/EvotecIT/index.json" }
                    }
                }
            };

            var context = service.Prepare(
                config,
                root.FullName,
                null,
                new ProjectBuildRequestedActions());

            var scopedCredential = Assert.Single(context.Spec.VersionSourceCredentials!);
            Assert.Equal("https://nuget.pkg.github.com/EvotecIT/index.json", scopedCredential.Key);
            Assert.Equal("EvotecIT", scopedCredential.Value.UserName);
            Assert.Equal("track-source-token", scopedCredential.Value.Secret);
        }
        finally
        {
            Environment.SetEnvironmentVariable(gitHubEnv, null);
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Prepare_rejects_github_packages_mode_without_owner()
    {
        var service = new ProjectBuildPreparationService();

        var exception = Assert.Throws<InvalidOperationException>(() => service.Prepare(
            new ProjectBuildConfiguration
            {
                UseGitHubPackages = true
            },
            Directory.GetCurrentDirectory(),
            null,
            new ProjectBuildRequestedActions()));

        Assert.Contains("GitHubPackagesOwner", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, DotNetRepositoryPackStrategy.PerProject)]
    [InlineData("", DotNetRepositoryPackStrategy.PerProject)]
    [InlineData("   ", DotNetRepositoryPackStrategy.PerProject)]
    [InlineData("PerProject", DotNetRepositoryPackStrategy.PerProject)]
    [InlineData("perproject", DotNetRepositoryPackStrategy.PerProject)]
    [InlineData("MSBuild", DotNetRepositoryPackStrategy.MSBuild)]
    [InlineData("msbuild", DotNetRepositoryPackStrategy.MSBuild)]
    [InlineData("Batch", DotNetRepositoryPackStrategy.MSBuild)]
    [InlineData("Other", DotNetRepositoryPackStrategy.PerProject)]
    public void ParsePackStrategy_maps_known_values(string? value, DotNetRepositoryPackStrategy expected)
    {
        Assert.Equal(expected, ProjectBuildSupportService.ParsePackStrategy(value));
    }

    [Fact]
    public void Prepare_warns_when_pack_strategy_is_unknown()
    {
        var logger = new BufferedLogger();
        var service = new ProjectBuildPreparationService(logger);

        var context = service.Prepare(
            new ProjectBuildConfiguration
            {
                PackStrategy = "MSBuild2"
            },
            Directory.GetCurrentDirectory(),
            null,
            new ProjectBuildRequestedActions());

        Assert.Equal(DotNetRepositoryPackStrategy.PerProject, context.Spec.PackStrategy);
        Assert.Contains(logger.Entries, entry =>
            entry.Level == "warn" &&
            entry.Message.Contains("Unknown PackStrategy 'MSBuild2'", StringComparison.Ordinal));
    }

    [Fact]
    public void Prepare_can_resolve_to_no_work_when_all_actions_are_disabled()
    {
        var service = new ProjectBuildPreparationService();

        var context = service.Prepare(
            new ProjectBuildConfiguration
            {
                UpdateVersions = false,
                Build = false,
                PublishNuget = false,
                PublishGitHub = false
            },
            Directory.GetCurrentDirectory(),
            null,
            new ProjectBuildRequestedActions());

        Assert.False(context.HasWork);
        Assert.False(context.Spec.Pack);
        Assert.False(context.Spec.Publish);
    }

    [Fact]
    public void Prepare_resolves_version_tracks_from_anchor_package_version()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-projectbuild-trackresolve-" + Guid.NewGuid().ToString("N")));

        try
        {
            var feed = root.CreateSubdirectory("feed");
            File.WriteAllText(Path.Combine(feed.FullName, "PowerForge.1.0.4.nupkg"), string.Empty);

            var service = new ProjectBuildPreparationService();
            var config = new ProjectBuildConfiguration
            {
                NugetSource = new[] { feed.FullName },
                ExpectedVersionMapAsInclude = true,
                VersionTracks = new Dictionary<string, ProjectBuildVersionTrack>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PowerForge"] = new()
                    {
                        ExpectedVersion = "1.0.X",
                        AnchorProject = "PowerForge",
                        Projects = new[] { "PowerForge.Cli", "PowerForge.Web.Cli" }
                    }
                }
            };

            var context = service.Prepare(
                config,
                root.FullName,
                null,
                new ProjectBuildRequestedActions());

            Assert.NotNull(context.Spec.ExpectedVersionsByProject);
            Assert.Equal("1.0.5", context.Spec.ExpectedVersionsByProject!["PowerForge"]);
            Assert.Equal("1.0.5", context.Spec.ExpectedVersionsByProject["PowerForge.Cli"]);
            Assert.Equal("1.0.5", context.Spec.ExpectedVersionsByProject["PowerForge.Web.Cli"]);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Version_tracks_use_resolved_default_sources()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-projectbuild-track-resolved-source-" + Guid.NewGuid().ToString("N")));

        try
        {
            var feed = root.CreateSubdirectory("feed");
            File.WriteAllText(Path.Combine(feed.FullName, "PowerForge.1.0.4.nupkg"), string.Empty);

            var config = new ProjectBuildConfiguration
            {
                ExpectedVersionMapAsInclude = true,
                VersionTracks = new Dictionary<string, ProjectBuildVersionTrack>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PowerForge"] = new()
                    {
                        ExpectedVersion = "1.0.X",
                        AnchorProject = "PowerForge",
                        Projects = new[] { "PowerForge.Cli" }
                    }
                }
            };

            var map = new ProjectBuildVersionTrackService(new NullLogger())
                .ResolveExpectedVersionMap(config, new[] { feed.FullName }, credential: null);

            Assert.NotNull(map);
            Assert.Equal("1.0.5", map!["PowerForge"]);
            Assert.Equal("1.0.5", map["PowerForge.Cli"]);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Version_tracks_accept_exact_prerelease_versions_without_an_anchor_feed()
    {
        var config = new ProjectBuildConfiguration
        {
            ExpectedVersionMapAsInclude = true,
            VersionTracks = new Dictionary<string, ProjectBuildVersionTrack>(StringComparer.OrdinalIgnoreCase)
            {
                ["Preview"] = new()
                {
                    ExpectedVersion = "2.1.0-beta.1",
                    AnchorProject = "HtmlTinkerX"
                }
            }
        };

        var map = new ProjectBuildVersionTrackService(new NullLogger())
            .ResolveExpectedVersionMap(config, Array.Empty<string>(), credential: null);

        Assert.NotNull(map);
        Assert.Equal("2.1.0-beta.1", map!["HtmlTinkerX"]);
    }

    [Fact]
    public void Prepare_resolves_version_tracks_from_explicit_anchor_package_id()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-projectbuild-track-packageid-" + Guid.NewGuid().ToString("N")));

        try
        {
            var feed = root.CreateSubdirectory("feed");
            File.WriteAllText(Path.Combine(feed.FullName, "PowerForge.Anchor.1.0.4.nupkg"), string.Empty);

            var service = new ProjectBuildPreparationService();
            var config = new ProjectBuildConfiguration
            {
                NugetSource = new[] { feed.FullName },
                ExpectedVersionMapAsInclude = true,
                VersionTracks = new Dictionary<string, ProjectBuildVersionTrack>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PowerForge"] = new()
                    {
                        ExpectedVersion = "1.0.X",
                        AnchorProject = "PowerForge",
                        AnchorPackageId = "PowerForge.Anchor",
                        Projects = new[] { "PowerForge.Cli", "PowerForge.Web.Cli" }
                    }
                }
            };

            var context = service.Prepare(
                config,
                root.FullName,
                null,
                new ProjectBuildRequestedActions());

            Assert.NotNull(context.Spec.ExpectedVersionsByProject);
            Assert.Equal("1.0.5", context.Spec.ExpectedVersionsByProject!["PowerForge"]);
            Assert.Equal("1.0.5", context.Spec.ExpectedVersionsByProject["PowerForge.Cli"]);
            Assert.Equal("1.0.5", context.Spec.ExpectedVersionsByProject["PowerForge.Web.Cli"]);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Prepare_expected_version_map_overrides_track_entries()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-projectbuild-track-override-" + Guid.NewGuid().ToString("N")));

        try
        {
            var feed = root.CreateSubdirectory("feed");
            File.WriteAllText(Path.Combine(feed.FullName, "PowerForge.1.0.4.nupkg"), string.Empty);

            var service = new ProjectBuildPreparationService();
            var config = new ProjectBuildConfiguration
            {
                NugetSource = new[] { feed.FullName },
                ExpectedVersionMapAsInclude = true,
                ExpectedVersionMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PowerForge.Web.Cli"] = "1.0.9"
                },
                VersionTracks = new Dictionary<string, ProjectBuildVersionTrack>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PowerForge"] = new()
                    {
                        ExpectedVersion = "1.0.X",
                        AnchorProject = "PowerForge",
                        Projects = new[] { "PowerForge.Web.Cli" }
                    }
                }
            };

            var context = service.Prepare(
                config,
                root.FullName,
                null,
                new ProjectBuildRequestedActions());

            Assert.NotNull(context.Spec.ExpectedVersionsByProject);
            Assert.Equal("1.0.5", context.Spec.ExpectedVersionsByProject!["PowerForge"]);
            Assert.Equal("1.0.9", context.Spec.ExpectedVersionsByProject["PowerForge.Web.Cli"]);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Prepare_requires_anchor_project_when_anchor_package_id_is_used_with_explicit_projects()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-projectbuild-track-anchorvalidation-" + Guid.NewGuid().ToString("N")));

        try
        {
            var service = new ProjectBuildPreparationService();
            var config = new ProjectBuildConfiguration
            {
                VersionTracks = new Dictionary<string, ProjectBuildVersionTrack>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PowerForge"] = new()
                    {
                        ExpectedVersion = "1.0.X",
                        AnchorPackageId = "PowerForge.Anchor",
                        Projects = new[] { "PowerForge.Cli" }
                    }
                }
            };

            var exception = Assert.Throws<ArgumentException>(() => service.Prepare(
                config,
                root.FullName,
                null,
                new ProjectBuildRequestedActions()));

            Assert.Contains("AnchorProject", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
