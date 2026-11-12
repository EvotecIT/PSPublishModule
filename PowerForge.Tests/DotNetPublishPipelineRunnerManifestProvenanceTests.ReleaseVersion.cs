using System.Text.Json;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests {
    [Fact]
    public void ReleaseManifest_UsesEffectiveMsBuildVersionInsteadOfRawProjectVersion() {
        var root = Directory.CreateTempSubdirectory("pf-effective-release-version-");
        try {
            string output = Path.Combine(root.FullName, "published");
            string project = Path.Combine(root.FullName, "App.csproj");
            File.WriteAllText(project, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net10.0</TargetFramework><Version>4.0.2</Version></PropertyGroup></Project>");
            var plan = new DotNetPublishPlan {
                ProjectRoot = root.FullName, Configuration = "Release", Restore = false,
                MsBuildProperties = new(StringComparer.OrdinalIgnoreCase) { ["Version"] = "4.1.0" },
                Targets = [new DotNetPublishTargetPlan {
                    Name = "App", ProjectPath = project, Version = "4.0.2",
                    Publish = new DotNetPublishPublishOptions {
                        Framework = "net10.0", Runtimes = ["linux-x64"], Style = DotNetPublishStyle.PortableCompat,
                        OutputPath = output, Zip = true
                    },
                    Combinations = [new() { Framework = "net10.0", Runtime = "linux-x64", Style = DotNetPublishStyle.PortableCompat }]
                }],
                Steps = [new DotNetPublishStep {
                    Kind = DotNetPublishStepKind.Publish, TargetName = "App", Framework = "net10.0",
                    Runtime = "linux-x64", Style = DotNetPublishStyle.PortableCompat
                }]
            };
            int publishes = 0;
            var tools = new DotNetPublishPipelineRunner(new NullLogger(), new RecordingProcessRunner(request => {
                Assert.Contains("/p:Version=4.1.0", request.Arguments);
                publishes++;
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "App"), "unsigned external-process fixture");
                return new ProcessRunResult(0, "", "", "dotnet", TimeSpan.Zero, timedOut: false);
            }));
            var service = new PowerForgeReleaseService(new NullLogger(),
                executePackages: (_, _, _) => throw new InvalidOperationException("No packages configured."),
                planTools: (_, _, _) => throw new InvalidOperationException("No legacy tools configured."),
                runTools: _ => throw new InvalidOperationException("No legacy tools configured."),
                loadDotNetToolsSpec: (_, path) => (new DotNetPublishSpec(), path),
                planDotNetTools: (_, _, _, _) => plan,
                runDotNetTools: value => tools.Run(value, progress: null),
                publishGitHubRelease: _ => throw new InvalidOperationException("No remote publication configured."));
            var result = service.Execute(new PowerForgeReleaseSpec {
                Tools = new PowerForgeToolReleaseSpec { DotNetPublish = new DotNetPublishSpec() },
                Outputs = new PowerForgeReleaseOutputsOptions { ManifestJsonPath = "release-manifest.json" }
            }, new PowerForgeReleaseRequest { ConfigPath = Path.Combine(root.FullName, "release.json"), ToolsOnly = true });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(1, publishes);
            Assert.Equal("4.1.0", Assert.Single(result.ReleaseAssetEntries).Version);
            using var manifest = JsonDocument.Parse(File.ReadAllText(result.ReleaseManifestPath!));
            Assert.Equal("4.1.0", manifest.RootElement.GetProperty("assetEntries")[0].GetProperty("Version").GetString());
        }
        finally { root.Delete(recursive: true); }
    }
}
