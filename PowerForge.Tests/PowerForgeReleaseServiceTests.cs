using System.Text;

namespace PowerForge.Tests;

public sealed class PowerForgeReleaseServiceTests
{
    [Fact]
    public void ToolReleasePlan_AppliesOverridesAcrossSelectedTarget()
    {
        var root = CreateSandbox();
        try
        {
            var projectPath = Path.Combine(root, "PowerForge.Cli.csproj");
            File.WriteAllText(projectPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Version>1.2.3</Version>
  </PropertyGroup>
</Project>
""", new UTF8Encoding(false));

            var service = new PowerForgeToolReleaseService(new NullLogger());
            var plan = service.Plan(
                new PowerForgeToolReleaseSpec
                {
                    ProjectRoot = ".",
                    Targets = new[]
                    {
                        new PowerForgeToolReleaseTarget
                        {
                            Name = "PowerForge",
                            ProjectPath = "PowerForge.Cli.csproj",
                            OutputName = "PowerForge",
                            Frameworks = new[] { "net10.0" },
                            Runtimes = new[] { "win-x64", "linux-x64" },
                            Flavor = PowerForgeToolReleaseFlavor.SingleContained
                        }
                    }
                },
                Path.Combine(root, "release.json"),
                new PowerForgeReleaseRequest
                {
                    Targets = new[] { "PowerForge" },
                    Runtimes = new[] { "osx-arm64" },
                    Frameworks = new[] { "net8.0" },
                    Flavors = new[] { PowerForgeToolReleaseFlavor.SingleFx }
                });

            var target = Assert.Single(plan.Targets);
            var combination = Assert.Single(target.Combinations);
            Assert.Equal("1.2.3", target.Version);
            Assert.Equal("osx-arm64", combination.Runtime);
            Assert.Equal("net8.0", combination.Framework);
            Assert.Equal(PowerForgeToolReleaseFlavor.SingleFx, combination.Flavor);
            Assert.Contains("PowerForge", combination.OutputPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Fact]
    public void Execute_GroupsToolAssetsIntoSingleGitHubReleasePerTarget()
    {
        var zipA = Path.GetTempFileName();
        var zipB = Path.GetTempFileName();
        try
        {
            var publishCalls = new List<GitHubReleasePublishRequest>();
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages should not run."),
                planTools: (_, _, _) => new PowerForgeToolReleasePlan
                {
                    ProjectRoot = Path.GetTempPath(),
                    Configuration = "Release",
                    Targets = new[]
                    {
                        new PowerForgeToolReleaseTargetPlan
                        {
                            Name = "PowerForge",
                            ProjectPath = "PowerForge.Cli.csproj",
                            OutputName = "PowerForge",
                            Version = "1.2.3",
                            ArtifactRootPath = Path.GetTempPath(),
                            Combinations = new[]
                            {
                                new PowerForgeToolReleaseCombinationPlan
                                {
                                    Runtime = "win-x64",
                                    Framework = "net10.0",
                                    Flavor = PowerForgeToolReleaseFlavor.SingleContained,
                                    OutputPath = Path.GetTempPath(),
                                    ZipPath = zipA
                                }
                            }
                        }
                    }
                },
                runTools: _ => new PowerForgeToolReleaseResult
                {
                    Success = true,
                    Artefacts = new[]
                    {
                        new PowerForgeToolReleaseArtifactResult
                        {
                            Target = "PowerForge",
                            Version = "1.2.3",
                            OutputName = "PowerForge",
                            Runtime = "win-x64",
                            Framework = "net10.0",
                            Flavor = PowerForgeToolReleaseFlavor.SingleContained,
                            OutputPath = Path.GetTempPath(),
                            ExecutablePath = Path.Combine(Path.GetTempPath(), "PowerForge.exe"),
                            ZipPath = zipA
                        },
                        new PowerForgeToolReleaseArtifactResult
                        {
                            Target = "PowerForge",
                            Version = "1.2.3",
                            OutputName = "PowerForge",
                            Runtime = "linux-x64",
                            Framework = "net10.0",
                            Flavor = PowerForgeToolReleaseFlavor.SingleContained,
                            OutputPath = Path.GetTempPath(),
                            ExecutablePath = Path.Combine(Path.GetTempPath(), "PowerForge"),
                            ZipPath = zipB
                        }
                    }
                },
                publishGitHubRelease: request =>
                {
                    publishCalls.Add(request);
                    return new GitHubReleasePublishResult
                    {
                        Succeeded = true,
                        HtmlUrl = "https://example.test/release",
                        ReusedExistingRelease = true
                    };
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
                    }
                },
                new PowerForgeReleaseRequest
                {
                    ConfigPath = Path.Combine(Path.GetTempPath(), "release.json"),
                    ToolsOnly = true
                });

            Assert.True(result.Success);
            var publish = Assert.Single(publishCalls);
            Assert.Equal("PowerForge-v1.2.3", publish.TagName);
            Assert.Equal("PowerForge 1.2.3", publish.ReleaseName);
            Assert.Equal(2, publish.AssetFilePaths.Count);

            var release = Assert.Single(result.ToolGitHubReleases);
            Assert.True(release.Success);
            Assert.Equal(2, release.AssetPaths.Length);
            Assert.True(release.ReusedExistingRelease);
        }
        finally
        {
            TryDelete(zipA);
            TryDelete(zipB);
        }
    }

    private static string CreateSandbox()
    {
        var path = Path.Combine(Path.GetTempPath(), "PowerForge.ReleaseTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void TryDelete(string path)
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
        else if (File.Exists(path))
            File.Delete(path);
    }
}
