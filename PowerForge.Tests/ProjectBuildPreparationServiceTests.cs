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
        }
        finally
        {
            Environment.SetEnvironmentVariable(publishEnv, null);
            Environment.SetEnvironmentVariable(gitHubEnv, null);
            try { root.Delete(recursive: true); } catch { }
        }
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
