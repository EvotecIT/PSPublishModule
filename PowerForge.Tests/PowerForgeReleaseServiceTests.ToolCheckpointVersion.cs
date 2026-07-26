namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void PublishBuiltReleaseOutputs_UsesCheckpointedDotNetTargetVersion()
    {
        var root = CreateSandbox();
        try
        {
            var zipPath = Path.Combine(root, "PowerForge-win-x64.zip");
            File.WriteAllText(zipPath, "zip");
            GitHubReleasePublishRequest? captured = null;
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages must not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Tools must not replan."),
                runTools: _ => throw new InvalidOperationException("Tools must not rebuild."),
                publishGitHubRelease: request =>
                {
                    captured = request;
                    return new GitHubReleasePublishResult
                    {
                        Succeeded = true,
                        HtmlUrl = "https://example.test/release"
                    };
                });
            var spec = new PowerForgeReleaseSpec
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
                }
            };
            var missingProjectPath = Path.Combine(root, "deleted.csproj");
            var built = new PowerForgeReleaseResult
            {
                Success = true,
                DotNetToolPlan = new DotNetPublishPlan
                {
                    Targets =
                    [
                        new DotNetPublishTargetPlan
                        {
                            Name = "PowerForge",
                            ProjectPath = missingProjectPath,
                            Version = "1.2.3"
                        }
                    ]
                },
                DotNetTools = new DotNetPublishResult
                {
                    Succeeded = true,
                    Artefacts =
                    [
                        new DotNetPublishArtefactResult
                        {
                            Target = "PowerForge",
                            Runtime = "win-x64",
                            Framework = "net10.0",
                            Style = DotNetPublishStyle.PortableCompat,
                            ZipPath = zipPath
                        }
                    ]
                }
            };

            var result = service.PublishBuiltReleaseOutputs(
                spec,
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(root, "release.json"),
                    ToolsOnly = true,
                    PublishToolGitHub = true
                },
                built);

            Assert.True(result.Success);
            Assert.False(File.Exists(missingProjectPath));
            Assert.NotNull(captured);
            Assert.Equal("PowerForge-v1.2.3", captured!.TagName);
            Assert.Equal("PowerForge 1.2.3", captured.ReleaseName);
        }
        finally
        {
            TryDelete(root);
        }
    }
}
