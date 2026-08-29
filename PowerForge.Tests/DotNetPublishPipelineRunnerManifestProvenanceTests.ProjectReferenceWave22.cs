using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Theory]
    [InlineData("%(FullPath)")]
    [InlineData("%(Identity)")]
    [InlineData("%(RootDir)%(Directory)%(Filename)%(Extension)")]
    public void ReadSourceProvenance_ExpandsTransformedProjectReferenceItemList(string transform)
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ChildProjects Include="../Library/Library.csproj" />
                    <ProjectReference Include="@(ChildProjects->'{transform}')"
                                      AdditionalProperties="A=1;B=2" />
                  </ItemGroup>
                </Project>
                """,
            libraryProjectXml: ConditionedLibraryProject,
            repositoryFiles: SelectedInput,
            mutatedPath: "inputs/Selected.cs");

        AssertSelectedInputIsDirty(provenance);
    }

    [Fact]
    public void ReadSourceProvenance_ExpandsCapturedPropertyAtAssignmentTime()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk" TreatAsLocalProperty="Configuration">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <ReferenceProperties>Flavor=A%3BB%3DC</ReferenceProperties>
                    <Captured>$(ReferenceProperties)</Captured>
                    <ReferenceProperties>Flavor=A;B=C</ReferenceProperties>
                    <CapturedConfiguration>$(Configuration)</CapturedConfiguration>
                    <Configuration>Debug</Configuration>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="$(Captured);Mode=$(CapturedConfiguration)" />
                  </ItemGroup>
                </Project>
                """,
            libraryProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'A;B=C' and '$(Mode)' == 'Release'">
                    <Compile Include="../../inputs/Selected.cs" />
                  </ItemGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'A' and '$(B)' == 'C'">
                    <Compile Include="../../inputs/Late.cs" />
                  </ItemGroup>
                </Project>
                """,
            repositoryFiles: new Dictionary<string, string>
            {
                ["inputs/Selected.cs"] = "public static class SelectedInput { }",
                ["inputs/Late.cs"] = "public static class LateInput { }"
            },
            mutatedPath: "inputs/Selected.cs");

        AssertSelectedInputIsDirty(provenance);
    }

    [Fact]
    public void ReadSourceProvenance_ReplaysProjectReferenceMetadataFromInitialTarget()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk" InitialTargets="InitializeReference">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="Flavor=A%3BB%3DC" />
                  </ItemGroup>
                  <Target Name="InitializeReference" DependsOnTargets="PrepareReference" />
                  <Target Name="PrepareReference">
                    <ItemGroup>
                      <ProjectReference Update="../Library/Library.csproj"
                                        AdditionalProperties="A=1;B=2" />
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
    public void ReadSourceProvenance_PreservesResolvedContextAfterResolveReferencesHook()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="Flavor=A" />
                  </ItemGroup>
                  <Target Name="ChangeReferenceAfterResolution" AfterTargets="ResolveReferences">
                    <ItemGroup>
                      <ProjectReference Update="../Library/Library.csproj"
                                        AdditionalProperties="Flavor=B"
                                        UndefineProperties="Flavor" />
                    </ItemGroup>
                  </Target>
                </Project>
                """,
            libraryProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'A'">
                    <Compile Include="../../inputs/Selected.cs" />
                  </ItemGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'B'">
                    <Compile Include="../../inputs/Late.cs" />
                  </ItemGroup>
                </Project>
                """,
            repositoryFiles: new Dictionary<string, string>
            {
                ["inputs/Selected.cs"] = "public static class SelectedInput { }",
                ["inputs/Late.cs"] = "public static class LateInput { }"
            },
            mutatedPath: "inputs/Selected.cs");

        AssertSelectedInputIsDirty(provenance);
    }
}
