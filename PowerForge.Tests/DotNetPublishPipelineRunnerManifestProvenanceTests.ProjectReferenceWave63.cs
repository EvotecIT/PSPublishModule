using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Theory]
    [InlineData("obj/Release/net8.0/App.dll")]
    [InlineData("bin/Release/net8.0/App.deps.json")]
    [InlineData("bin/Release/net8.0/App.runtimeconfig.json")]
    public void ReadSourceProvenance_NoBuildPublishRejectsMutatedSdkGeneratedInput(
        string outputRelativePath)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string projectPath = Path.Combine(root, "App.csproj");
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(root, "Program.cs"),
                "internal static class Program { private static void Main() { } }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{projectPath}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            RunDotNet(root, $"build \"{projectPath}\" -c Release --no-restore --nologo");
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release",
                NoBuildInPublish = true,
                Targets =
                [
                    new DotNetPublishTargetPlan
                    {
                        Name = "App",
                        ProjectPath = projectPath,
                        Combinations =
                        [
                            new DotNetPublishTargetCombination
                            {
                                Framework = "net8.0",
                                Style = DotNetPublishStyle.FrameworkDependent
                            }
                        ]
                    }
                ]
            };

            DotNetPublishPipelineRunner.SourceProvenance clean =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, buildPlan: plan);
            Assert.False(clean.Dirty, string.Join(Environment.NewLine, clean.DirtyReasons));

            string outputPath = Path.Combine(
                root,
                outputRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(outputPath), outputPath);
            File.WriteAllText(outputPath, "mutated after build");

            DotNetPublishPipelineRunner.SourceProvenance mutated =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, buildPlan: plan);

            Assert.True(mutated.Dirty, string.Join(Environment.NewLine, mutated.DirtyReasons));
            Assert.Contains(mutated.DirtyReasons, reason =>
                reason.Contains(Path.GetFileName(outputPath), StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}
