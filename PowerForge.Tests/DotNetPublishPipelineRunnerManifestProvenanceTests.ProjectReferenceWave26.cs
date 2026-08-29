using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ReadSourceProvenance_PreservesContextWhenComputedUpdateIdentityIsAmbiguous()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Other/Other.csproj"
                                      AdditionalProperties="Flavor=A%3BB%3DC" />
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="Flavor=A;B=C" />
                  </ItemGroup>
                  <Target Name="UpdateReference" BeforeTargets="ResolveReferences">
                    <ItemGroup>
                      <ChildProjects Include="../Other/Other.csproj" />
                      <ProjectReference Update="@(ChildProjects)"
                                        AdditionalProperties="Flavor=A%3BB%3DC" />
                      <ChildProjects Remove="../Other/Other.csproj" />
                    </ItemGroup>
                  </Target>
                </Project>
                """,
            libraryProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'A' and '$(B)' == 'C'">
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

    [Fact]
    public void ReadSourceProvenance_PreservesContextWhenIncrementalTargetMayBeSkipped()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="Flavor=A;B=C" />
                  </ItemGroup>
                  <Target Name="UpdateReference"
                          BeforeTargets="ResolveReferences"
                          Inputs="marker.txt"
                          Outputs="marker.txt">
                    <ItemGroup>
                      <ProjectReference Update="../Library/Library.csproj"
                                        AdditionalProperties="Flavor=A%3BB%3DC" />
                    </ItemGroup>
                  </Target>
                </Project>
                """,
            libraryProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'A' and '$(B)' == 'C'">
                    <Compile Include="../../inputs/Selected.cs" />
                  </ItemGroup>
                </Project>
                """,
            repositoryFiles: SelectedInput,
            mutatedPath: "inputs/Selected.cs",
            appFiles: new Dictionary<string, string>
            {
                ["marker.txt"] = "current"
            });

        AssertSelectedInputIsDirty(provenance);
    }

    [Fact]
    public void ReadSourceProvenance_ExpandsPropertyFunctionsInScheduledTargetLists()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup>
                  <Target Name="UpdateReference">
                    <ItemGroup>
                      <ProjectReference Update="../Library/Library.csproj"
                                        AdditionalProperties="A=1;B=2" />
                    </ItemGroup>
                  </Target>
                  <Target Name="ScheduleReference"
                          BeforeTargets="ResolveReferences"
                          DependsOnTargets="$([System.String]::Concat('Update','Reference'))" />
                </Project>
                """,
            libraryProjectXml: ConditionedLibraryProject,
            repositoryFiles: SelectedInput,
            mutatedPath: "inputs/Selected.cs");

        AssertSelectedInputIsDirty(provenance);
    }

    [Fact]
    public void ReadSourceProvenance_ReplaysRemoveMetadataOnReferenceUpdate()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <Flavor>Inherited</Flavor>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      UndefineProperties="Flavor" />
                  </ItemGroup>
                  <Target Name="UpdateReference" BeforeTargets="ResolveReferences">
                    <ItemGroup>
                      <ProjectReference Update="../Library/Library.csproj"
                                        RemoveMetadata="UndefineProperties" />
                    </ItemGroup>
                  </Target>
                </Project>
                """,
            libraryProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'Inherited'">
                    <Compile Include="../../inputs/Selected.cs" />
                  </ItemGroup>
                </Project>
                """,
            repositoryFiles: SelectedInput,
            mutatedPath: "inputs/Selected.cs",
            buildProperties: new Dictionary<string, string>
            {
                ["Flavor"] = "Inherited"
            });

        AssertSelectedInputIsDirty(provenance);
    }
}
