using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ReadSourceProvenance_UsesAuthoritativeMultiAssignmentGlobalDuringReplay()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="$(ReferenceProperties)" />
                  </ItemGroup>
                </Project>
                """,
            libraryProjectXml: ConditionedLibraryProject,
            repositoryFiles: SelectedInput,
            mutatedPath: "inputs/Selected.cs",
            buildProperties: new Dictionary<string, string>
            {
                ["ReferenceProperties"] = "A=1;B=2"
            },
            buildFramework: "net8.0");

        AssertSelectedInputIsDirty(provenance);
    }

    [Fact]
    public void ReadSourceProvenance_PreservesDuplicateItemListMetadataTransforms()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ChildProjects Include="placeholder"><ProjectPath>../Missing/Missing.csproj</ProjectPath></ChildProjects>
                    <ChildProjects Include="placeholder"><ProjectPath>../Library/Library.csproj</ProjectPath></ChildProjects>
                    <ProjectReference Include="@(ChildProjects->'%(ProjectPath)')"
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
    public void ReadSourceProvenance_ReplaysImportedInitialTargets()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <Import Project="ReferenceInitialization.targets" />
                  <Import Project="ReferenceFinalization.targets" />
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj" AdditionalProperties="Flavor=A" />
                  </ItemGroup>
                </Project>
                """,
            libraryProjectXml: ConditionedLibraryProject,
            repositoryFiles: SelectedInput,
            mutatedPath: "inputs/Selected.cs",
            appFiles: new Dictionary<string, string>
            {
                ["ReferenceInitialization.targets"] = """
                    <Project InitialTargets="PrepareReference">
                      <Target Name="PrepareReference">
                        <ItemGroup>
                          <ProjectReference Update="../Library/Library.csproj"
                                            AdditionalProperties="A=1" />
                        </ItemGroup>
                      </Target>
                    </Project>
                    """,
                ["ReferenceFinalization.targets"] = """
                    <Project InitialTargets="FinalizeReference">
                      <Target Name="FinalizeReference">
                        <ItemGroup>
                          <ProjectReference Update="../Library/Library.csproj"
                                            AdditionalProperties="%(ProjectReference.AdditionalProperties);B=2" />
                        </ItemGroup>
                      </Target>
                    </Project>
                    """
            });

        AssertSelectedInputIsDirty(provenance);
    }

    [Fact]
    public void ReadSourceProvenance_PreservesEffectivePropertyRemovalsWithoutTargetFramework()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj" UndefineProperties="Flavor" />
                  </ItemGroup>
                  <Target Name="TouchReference" BeforeTargets="ResolveReferences">
                    <ItemGroup><ProjectReference Update="../Library/Library.csproj" /></ItemGroup>
                  </Target>
                </Project>
                """,
            libraryProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup Condition="'$(Flavor)' == ''"><Compile Include="../../inputs/Selected.cs" /></ItemGroup>
                </Project>
                """,
            repositoryFiles: SelectedInput,
            mutatedPath: "inputs/Selected.cs",
            buildProperties: new Dictionary<string, string> { ["Flavor"] = "Parent" },
            buildFramework: string.Empty);

        AssertSelectedInputIsDirty(provenance);
    }

    [Fact]
    public void ReadSourceProvenance_ExpandsCurrentItemMetadataDuringUpdateReplay()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj" AdditionalProperties="A=1" />
                    <ProjectReference Update="../Library/Library.csproj"
                                      AdditionalProperties="%(ProjectReference.AdditionalProperties);B=2" />
                  </ItemGroup>
                </Project>
                """,
            libraryProjectXml: ConditionedLibraryProject,
            repositoryFiles: SelectedInput,
            mutatedPath: "inputs/Selected.cs");

        AssertSelectedInputIsDirty(provenance);
    }
}
