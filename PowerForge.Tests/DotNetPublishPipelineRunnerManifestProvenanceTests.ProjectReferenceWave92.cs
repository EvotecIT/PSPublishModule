using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void Run_NoBuildPublishReacquiresProvenanceAfterEachMatrixBuild()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string projectPath = Path.Combine(root, "App.csproj");
            File.WriteAllText(
                projectPath,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(root, "Program.cs"), "System.Console.WriteLine(\"matrix\");");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\nArtifacts/\n");
            RunDotNet(root, $"restore \"{projectPath}\" -r win-x64 --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");

            string revision = RunGit(root, "rev-parse HEAD").Trim();
            var target = new DotNetPublishTargetPlan
            {
                Name = "app",
                ProjectPath = projectPath,
                Publish = new DotNetPublishPublishOptions
                {
                    Framework = "net8.0",
                    Runtimes = ["win-x64"],
                    Style = DotNetPublishStyle.FrameworkDependent,
                    OutputPath = "Artifacts/{framework}",
                    UseStaging = false
                },
                Combinations =
                [
                    new DotNetPublishTargetCombination
                    {
                        Framework = "net8.0",
                        Runtime = "win-x64",
                        Style = DotNetPublishStyle.FrameworkDependent
                    },
                    new DotNetPublishTargetCombination
                    {
                        Framework = "net10.0",
                        Runtime = "win-x64",
                        Style = DotNetPublishStyle.FrameworkDependent
                    }
                ]
            };
            var runner = new DotNetPublishPipelineRunner(new NullLogger());
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release",
                SourceRevision = revision,
                Restore = true,
                Build = true,
                NoBuildInPublish = true,
                NoRestoreInPublish = true,
                Targets = [target],
                Steps =
                [
                    CreateCombinationStep(DotNetPublishStepKind.Build, "net8.0"),
                    CreateCombinationStep(DotNetPublishStepKind.Publish, "net8.0"),
                    CreateCombinationStep(DotNetPublishStepKind.Build, "net10.0"),
                    CreateCombinationStep(DotNetPublishStepKind.Publish, "net10.0")
                ]
            };

            DotNetPublishResult result = runner.Run(plan, progress: null);

            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.Equal(2, result.Artefacts.Length);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    private static DotNetPublishStep CreateCombinationStep(
        DotNetPublishStepKind kind,
        string framework)
        => new()
        {
            Key = $"{kind}:{framework}",
            Kind = kind,
            TargetName = "app",
            Framework = framework,
            Runtime = "win-x64",
            Style = DotNetPublishStyle.FrameworkDependent
        };
}
