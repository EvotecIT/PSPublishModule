using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ReadSourceProvenance_DoesNotApplyPropertyFunctionRemoveToSurvivingReference()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Other/Other.csproj" />
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="A=1;B=2" />
                    <ProjectReference Remove="$([System.IO.Path]::GetFullPath('$(MSBuildProjectDirectory)/../Other/Other.csproj'))" />
                  </ItemGroup>
                </Project>
                """,
            libraryProjectXml: ConditionedLibraryProject,
            repositoryFiles: SelectedInput,
            mutatedPath: "inputs/Selected.cs",
            appFiles: new Dictionary<string, string>
            {
                ["../Other/Other.csproj"] = "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>"
            });

        AssertSelectedInputIsDirty(provenance);
        Assert.DoesNotContain(
            provenance.DirtyReasons,
            reason => reason.Contains("MSBuild input evaluation failed", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadSourceProvenance_ReplaysTransientTargetItemListAtAssignmentTime()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="Flavor=Fallback" />
                  </ItemGroup>
                  <Target Name="UpdateReference" BeforeTargets="ResolveReferences">
                    <ItemGroup>
                      <ChildProjects Include="../Library/Library.csproj" />
                      <ProjectReference Update="@(ChildProjects)"
                                        AdditionalProperties="A=1;B=2" />
                      <ChildProjects Remove="../Library/Library.csproj" />
                    </ItemGroup>
                  </Target>
                </Project>
                """,
            libraryProjectXml: ConditionedLibraryProject,
            repositoryFiles: SelectedInput,
            mutatedPath: "inputs/Selected.cs");

        AssertSelectedInputIsDirty(provenance);
    }

    [Fact]
    public void ReadSourceProvenance_DoesNotCreateCartesianDuplicateReferenceContext()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj" AdditionalProperties="A=1" />
                    <ProjectReference Include="../Library/Library.csproj" AdditionalProperties="B=2" />
                  </ItemGroup>
                </Project>
                """,
            libraryProjectXml: ConditionedLibraryProject,
            repositoryFiles: SelectedInput,
            mutatedPath: "inputs/Selected.cs");

        Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
    }

    [Fact]
    public void ReadSourceProvenance_CapturesStaticPropertyAtReferenceDeclaration()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <ReferenceProperties>A=1%3BB%3D2</ReferenceProperties>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="$(ReferenceProperties)" />
                  </ItemGroup>
                  <PropertyGroup><ReferenceProperties>A=1;B=2</ReferenceProperties></PropertyGroup>
                </Project>
                """,
            libraryProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup Condition="'$(A)' == '1;B=2'">
                    <Compile Include="../../inputs/Selected.cs" />
                  </ItemGroup>
                </Project>
                """,
            repositoryFiles: SelectedInput,
            mutatedPath: "inputs/Selected.cs");

        AssertSelectedInputIsDirty(provenance);
    }

    [Fact]
    public void ReadSourceProvenance_ReplaysScheduledReferenceWithoutTargetFramework()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="Flavor=Fallback" />
                  </ItemGroup>
                  <Target Name="UpdateReference" BeforeTargets="ResolveReferences">
                    <ItemGroup>
                      <ProjectReference Update="../Library/Library.csproj"
                                        AdditionalProperties="A=1;B=2" />
                    </ItemGroup>
                  </Target>
                </Project>
                """,
            libraryProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup Condition="'$(A)' == '1' and '$(B)' == '2'">
                    <Compile Include="../../inputs/Selected.cs" />
                  </ItemGroup>
                </Project>
                """,
            repositoryFiles: SelectedInput,
            mutatedPath: "inputs/Selected.cs",
            buildFramework: string.Empty);

        AssertSelectedInputIsDirty(provenance);
    }

    [Fact]
    public void ReadSourceProvenance_RejectsFabricatedGeneratedOutputRecord()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "Library")).FullName;
            string assetsDirectory = Directory.CreateDirectory(Path.Combine(root, "assets")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string libraryProject = Path.Combine(libraryDirectory, "Library.csproj");
            string payloadPath = Path.Combine(assetsDirectory, "Payload.dll");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      ReferenceOutputAssembly="false"
                                      OutputItemType="EmbeddedResource" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(libraryProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <OutDir>$(MSBuildProjectDirectory)/../../assets/</OutDir>
                    <TargetPath>$(OutDir)Payload.dll</TargetPath>
                  </PropertyGroup>
                  <Target Name="Build" Returns="$(TargetPath)" />
                  <Target Name="GetTargetPath" Returns="$(TargetPath)" />
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { }");
            File.WriteAllText(payloadPath, "untrusted payload");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\nassets/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            string recordDirectory = Directory.CreateDirectory(
                Path.Combine(libraryDirectory, "obj", "Release", "net8.0")).FullName;
            File.WriteAllText(
                Path.Combine(recordDirectory, "Library.csproj.FileListAbsolute.txt"),
                payloadPath);

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
            Assert.Contains(
                provenance.DirtyReasons,
                reason => reason.Contains("Payload.dll", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}
