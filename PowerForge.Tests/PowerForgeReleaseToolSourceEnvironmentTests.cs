using System.Text.Json;

namespace PowerForge.Tests;

[Collection(ProcessEnvironmentCollection.Name)]
public sealed class PowerForgeReleaseToolSourceEnvironmentTests
{
    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void Execute_NonGitDotNetToolIgnoresAmbientPlanRevisionThroughDeferredPublication()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.ToolSourceTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var originalGitHubSha = Environment.GetEnvironmentVariable("GITHUB_SHA");
        try
        {
            const string ambientCommit = "0123456789abcdef0123456789abcdef01234567";
            Environment.SetEnvironmentVariable("GITHUB_SHA", ambientCommit);
            var project = Path.Combine(root, "Tool.csproj");
            File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Version>1.2.3</Version></PropertyGroup></Project>");
            var archive = Path.Combine(root, "tool.zip");
            var configPath = Path.Combine(root, "release.json");
            var publishSpec = new DotNetPublishSpec
            {
                DotNet = new DotNetPublishDotNetOptions { ProjectRoot = root, Restore = false, Build = false },
                Targets = [new DotNetPublishTarget
                {
                    Name = "Tool", ProjectPath = project,
                    Publish = new DotNetPublishPublishOptions
                    {
                        Framework = "net10.0", Runtimes = ["win-x64"],
                        Style = DotNetPublishStyle.PortableCompat, Zip = true
                    }
                }]
            };
            // Use the real normal planner to demonstrate that a full plan revision can
            // originate in the hosting workflow even though the selected input is not Git.
            var normalPlan = new DotNetPublishPipelineRunner(new NullLogger()).Plan(publishSpec, configPath);
            Assert.False(normalPlan.UseControlledSourceProvenance);
            Assert.Equal(ambientCommit, normalPlan.SourceRevision);
            Assert.Null(ReleaseSourceCommit.Capture(root));
            var published = new List<GitHubReleasePublishRequest>();
            var service = new PowerForgeReleaseService(
                new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("Packages must not run."),
                planTools: (_, _, _) => throw new InvalidOperationException("Legacy tools must not plan."),
                runTools: _ => throw new InvalidOperationException("Legacy tools must not build."),
                loadDotNetToolsSpec: (_, _) => (publishSpec, configPath),
                planDotNetTools: (_, _, _, _) => normalPlan,
                runDotNetTools: _ =>
                {
                    File.WriteAllText(archive, "built non-Git tool");
                    return new DotNetPublishResult
                    {
                        Succeeded = true,
                        Artefacts = [new DotNetPublishArtefactResult
                        {
                            Target = "Tool", Runtime = "win-x64", Framework = "net10.0",
                            Style = DotNetPublishStyle.PortableCompat, ZipPath = archive
                        }]
                    };
                },
                publishGitHubRelease: request =>
                {
                    published.Add(request);
                    return new GitHubReleasePublishResult { Succeeded = true };
                });
            var spec = new PowerForgeReleaseSpec
            {
                Tools = new PowerForgeToolReleaseSpec
                {
                    DotNetPublish = publishSpec,
                    GitHub = new PowerForgeToolReleaseGitHubOptions
                    {
                        Publish = true, Owner = "EvotecIT", Repository = "Sample", TokenEnvName = "PATH"
                    }
                }
            };
            var built = service.Execute(spec, new PowerForgeReleaseRequest { ConfigPath = configPath, ToolsOnly = true });
            Assert.True(built.Success, built.ErrorMessage);
            Assert.Null(built.ToolSourceCommitSha);
            Assert.Null(built.DotNetSourceCommitSha);
            Assert.Equal(ambientCommit, built.DotNetToolPlan!.SourceRevision);
            var immediate = Assert.Single(published);
            Assert.Null(immediate.Commitish);
            Assert.Null(immediate.ExpectedTagCommitSha);

            // The null captured value and informational revision must not acquire tag
            // authority after serializing, restoring and resuming a build checkpoint.
            var checkpoint = JsonSerializer.Deserialize<PowerForgeReleaseResult>(JsonSerializer.Serialize(built))!;
            Environment.SetEnvironmentVariable("GITHUB_SHA", new string('b', 40));
            var resumed = service.PublishBuiltReleaseOutputs(spec,
                new PowerForgeReleaseRequest { ConfigPath = configPath, ToolsOnly = true, PublishToolGitHub = true }, checkpoint);
            Assert.True(resumed.Success, resumed.ErrorMessage);
            Assert.Equal(2, published.Count);
            Assert.Null(published[1].Commitish);
            Assert.Null(published[1].ExpectedTagCommitSha);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_SHA", originalGitHubSha);
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
