using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ReadSourceProvenance_AcceptsVerifiedSdkRuntimePacksForSingleFilePublish()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string runtime = OperatingSystem.IsWindows()
                ? "win-x64"
                : OperatingSystem.IsMacOS() ? "osx-x64" : "linux-x64";
            string targetFramework = OperatingSystem.IsWindows() ? "net10.0-windows" : "net10.0";
            string platformProperty = OperatingSystem.IsWindows()
                ? "<UseWindowsForms>true</UseWindowsForms>"
                : string.Empty;
            string projectPath = Path.Combine(root, "App.csproj");
            File.WriteAllText(projectPath, $$"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>{{targetFramework}}</TargetFramework>
                    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
                    {{platformProperty}}
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(root, "Program.cs"),
                "internal static class Program { private static void Main() { } }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{projectPath}\" -r {runtime} --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            RunDotNet(
                root,
                $"build \"{projectPath}\" -c Release -f {targetFramework} -r {runtime} --no-restore --nologo " +
                "-p:SelfContained=true -p:PublishSingleFile=true");

            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release",
                NoBuildInPublish = true,
                NoRestoreInPublish = true,
                Targets =
                [
                    new DotNetPublishTargetPlan
                    {
                        Name = "App",
                        ProjectPath = projectPath,
                        Publish = new DotNetPublishPublishOptions
                        {
                            Style = DotNetPublishStyle.Portable
                        },
                        Combinations =
                        [
                            new DotNetPublishTargetCombination
                            {
                                Framework = targetFramework,
                                Runtime = runtime,
                                Style = DotNetPublishStyle.Portable
                            }
                        ]
                    }
                ]
            };

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, buildPlan: plan);

            Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
            Assert.Empty(provenance.DirtyPaths);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}
