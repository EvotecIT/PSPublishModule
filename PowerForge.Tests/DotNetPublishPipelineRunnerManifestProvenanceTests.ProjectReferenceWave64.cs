using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ReadSourceProvenance_NoBuildPublishDoesNotExecuteTargetsInSourceCheckout()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string projectPath = Path.Combine(root, "App.csproj");
            string markerPath = Path.Combine(root, "publish-target-ran.txt");
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                  <Target Name="ObservePublishInputs" BeforeTargets="ComputeFilesToPublish">
                    <WriteLinesToFile File="publish-target-ran.txt" Lines="ran" Overwrite="true" />
                  </Target>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(root, "Program.cs"),
                "internal static class Program { private static void Main() { } }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\npublish-target-ran.txt\n");
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

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, buildPlan: plan);

            Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
            Assert.False(File.Exists(markerPath), markerPath);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_AcceptSequentialPropertyExtension()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(Path.Combine(root, "a.txt"), "a");
            File.WriteAllText(Path.Combine(root, "b.txt"), "b");
            File.WriteAllText(projectPath, """
                <Project>
                  <PropertyGroup>
                    <Files>a.txt</Files>
                    <Files>$(Files);b.txt</Files>
                  </PropertyGroup>
                  <Target Name="Build">
                    <Copy SourceFiles="$(Files)" DestinationFolder="output" />
                  </Target>
                </Project>
                """);

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, Path.Combine(root, "a.txt"), Path.Combine(root, "b.txt")],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData("<data name=\"Payload\" type=\"PowerForge.Tests.ExecutableResource, PowerForge.Tests\"><value>payload</value></data>")]
    [InlineData("<data name=\"Payload\" mimetype=\"application/x-microsoft.net.object.binary.base64\"><value>AAEAAAD/////</value></data>")]
    [InlineData("<metadata name=\"Payload\" mimetype=\"application/x-microsoft.net.object.soap.base64\"><value>payload</value></metadata>")]
    [InlineData("<data name=\"Payload\" type=\"System.Resources.ResXFileRef, System.Windows.Forms\"><value>payload.bin;PowerForge.Tests.ExecutableResource, PowerForge.Tests</value></data>")]
    public void ControlledBuildInputs_RejectExecutableResourcePayload(string resourceEntry)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "payload.bin"), "controlled");
            File.WriteAllText(
                Path.Combine(root, "Resources.resx"),
                $"<root>{resourceEntry}</root>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}
