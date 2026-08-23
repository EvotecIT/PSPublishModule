using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ReadSourceProvenance_ReplaysItemListsCreatedByScheduledTargets()
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
    public void ReadSourceProvenance_ExpandsDependenciesUsingPropertiesSetByEarlierTargets()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk" InitialTargets="ConfigureReferenceGraph">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="Flavor=Fallback" />
                  </ItemGroup>
                  <Target Name="ConfigureReferenceGraph">
                    <PropertyGroup>
                      <ResolveReferencesDependsOn>UpdateReference;$(ResolveReferencesDependsOn)</ResolveReferencesDependsOn>
                    </PropertyGroup>
                  </Target>
                  <Target Name="UpdateReference">
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

    [Theory]
    [InlineData("UndefineProperties")]
    [InlineData("GlobalPropertiesToRemove")]
    public void ReadSourceProvenance_PreservesNoRemovalBranchForUncertainUpdates(
        string removalMetadata)
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj" />
                  </ItemGroup>
                  <Target Name="MaybeRemoveFlavor"
                          BeforeTargets="ResolveReferences"
                          Condition="Exists('missing.flag')">
                    <ItemGroup>
                      <ProjectReference Update="../Library/Library.csproj"
                                        {removalMetadata}="Flavor" />
                    </ItemGroup>
                  </Target>
                </Project>
                """,
            libraryProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'Parent'">
                    <Compile Include="../../inputs/Selected.cs" />
                  </ItemGroup>
                </Project>
                """,
            repositoryFiles: SelectedInput,
            mutatedPath: "inputs/Selected.cs",
            buildProperties: new Dictionary<string, string> { ["Flavor"] = "Parent" },
            buildFramework: "net8.0");

        AssertSelectedInputIsDirty(provenance);
    }

    [Fact]
    public void ReadSourceProvenance_ExpandsPropertyFunctionsInUpdateIdentities()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="Flavor=Fallback" />
                    <ProjectReference Update="$([System.IO.Path]::GetFullPath('../Library/Library.csproj'))"
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
    public void ReadSourceProvenance_ExpandsPropertyFunctionsInReferenceIdentities()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="$([System.IO.Path]::GetFullPath('../Library/Library.csproj'))"
                                      AdditionalProperties="A=1;B=2" />
                  </ItemGroup>
                </Project>
                """,
            libraryProjectXml: ConditionedLibraryProject,
            repositoryFiles: SelectedInput,
            mutatedPath: "inputs/Selected.cs");

        AssertSelectedInputIsDirty(provenance);
    }
}
