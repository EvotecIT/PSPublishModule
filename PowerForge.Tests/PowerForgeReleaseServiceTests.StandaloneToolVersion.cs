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

    private static PowerForgeReleaseService CreateService(
        Func<PowerForgeReleaseRequest, PowerForgeToolReleasePlan> planTools)
        => new(
            new NullLogger(),
            executePackages: (_, _, _) => throw new InvalidOperationException("Packages must not run."),
            planTools: (_, _, request) => planTools(request),
            runTools: _ => throw new InvalidOperationException("Tools must not run in a plan."),
            publishGitHubRelease: _ => throw new InvalidOperationException("GitHub must not run in a plan."));
}
