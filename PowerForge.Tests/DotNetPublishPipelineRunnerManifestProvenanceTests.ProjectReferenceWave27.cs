using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ReadSourceProvenance_TracksUnprovenProjectReferenceAssemblyItem()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "Library")).FullName;
            string payloadDirectory = Directory.CreateDirectory(Path.Combine(root, "payload")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string libraryProject = Path.Combine(libraryDirectory, "Library.csproj");
            string payloadPath = Path.Combine(payloadDirectory, "Payload.dll");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj" />
                  </ItemGroup>
                  <Target Name="InjectReferencePath" AfterTargets="ResolveAssemblyReferences">
                    <ItemGroup>
                      <ReferencePath Include="../../payload/Payload.dll"
                                     ReferenceSourceTarget="ProjectReference"
                                     MSBuildSourceProjectFile="$(MSBuildProjectDirectory)/../Library/Library.csproj" />
                    </ItemGroup>
                  </Target>
                </Project>
                """);
            File.WriteAllText(
                libraryProject,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { }");
            File.WriteAllText(payloadPath, "unproven assembly payload");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\npayload/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            RunDotNet(root, $"build \"{libraryProject}\" -c Release --no-restore --nologo");

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
            Assert.True(
                provenance.DirtyReasons.Any(reason =>
                    reason.Contains("Payload.dll", StringComparison.OrdinalIgnoreCase)),
                string.Join(Environment.NewLine, provenance.DirtyReasons));
            Assert.DoesNotContain(
                provenance.DirtyReasons,
                reason => reason.Contains("Library.dll", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ReadSourceProvenance_AppliesFinalItemDefinitionDefaultsToAllReferences()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemDefinitionGroup>
                    <ProjectReference><AdditionalProperties>A=1;B=one</AdditionalProperties></ProjectReference>
                  </ItemDefinitionGroup>
                  <ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup>
                  <ItemDefinitionGroup>
                    <ProjectReference><AdditionalProperties>A=1;B=two</AdditionalProperties></ProjectReference>
                  </ItemDefinitionGroup>
                  <ItemGroup><ProjectReference Include="../Other/Other.csproj" /></ItemGroup>
                </Project>
                """,
            libraryProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup Condition="'$(A)' == '1' and '$(B)' == 'two'">
                    <Compile Include="../../inputs/Selected.cs" />
                  </ItemGroup>
                </Project>
                """,
            repositoryFiles: SelectedInput,
            mutatedPath: "inputs/Selected.cs",
            appFiles: new Dictionary<string, string>
            {
                ["../Other/Other.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>"
            });

        AssertSelectedInputIsDirty(provenance);
    }
}
