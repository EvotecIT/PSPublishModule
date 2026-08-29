using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ReadSourceProvenance_DoesNotCrossApplyComputedProjectReferenceMetadata()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <FirstProject>../First/First.csproj</FirstProject>
                    <SecondProject>../Second/Second.csproj</SecondProject>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="$(FirstProject)"
                                      AdditionalProperties="Flavor=A%3BB%3DC" />
                    <ProjectReference Include="$(SecondProject)"
                                      AdditionalProperties="Flavor=A;B=C" />
                  </ItemGroup>
                </Project>
                """,
            libraryProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                </Project>
                """,
            repositoryFiles: new Dictionary<string, string>
            {
                ["src/First/First.csproj"] = """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                      <ItemGroup Condition="'$(Flavor)' == 'A' and '$(B)' == 'C'">
                        <Compile Include="../../inputs/StaleFirst.cs" />
                      </ItemGroup>
                    </Project>
                    """,
                ["src/First/First.cs"] = "public static class First { }",
                ["src/Second/Second.csproj"] = """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                      <ItemGroup Condition="'$(Flavor)' == 'A;B=C'">
                        <Compile Include="../../inputs/StaleSecond.cs" />
                      </ItemGroup>
                    </Project>
                    """,
                ["src/Second/Second.cs"] = "public static class Second { }",
                ["inputs/StaleFirst.cs"] = "public static class StaleFirstInput { }",
                ["inputs/StaleSecond.cs"] = "public static class StaleSecondInput { }"
            },
            mutatedPath: "inputs/StaleFirst.cs");

        Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
    }

    [Fact]
    public void ReadSourceProvenance_DropsMetadataFromRemovedProjectReferences()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="Flavor=A%3BB%3DC" />
                    <ProjectReference Remove="../Library/Library.csproj" />
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="Flavor=A;B=C" />
                  </ItemGroup>
                </Project>
                """,
            libraryProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'A;B=C'">
                    <Compile Include="../../inputs/Stale.cs" />
                  </ItemGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'A' and '$(B)' == 'C'">
                    <Compile Include="../../inputs/Selected.cs" />
                  </ItemGroup>
                </Project>
                """,
            repositoryFiles: new Dictionary<string, string>
            {
                ["inputs/Stale.cs"] = "public static class StaleInput { }",
                ["inputs/Selected.cs"] = "public static class SelectedInput { }"
            },
            mutatedPath: "inputs/Stale.cs");

        Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
    }

    [Fact]
    public void ReadSourceProvenance_EvaluatesProjectReferenceConditionsWithTheFinalPropertyState()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <ReferenceMode>Active</ReferenceMode>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      Condition="'$(ReferenceMode)' == 'Active'"
                                      AdditionalProperties="A=1;B=2" />
                  </ItemGroup>
                  <PropertyGroup><ReferenceMode>Inactive</ReferenceMode></PropertyGroup>
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
            repositoryFiles: new Dictionary<string, string>
            {
                ["inputs/Selected.cs"] = "public static class SelectedInput { }"
            },
            mutatedPath: "inputs/Selected.cs");

        Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
        Assert.DoesNotContain(
            provenance.DirtyPaths,
            path => path.Replace('\\', '/').EndsWith("inputs/Selected.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadSourceProvenance_UsesTheLastEvaluatedPropertyDefinition()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <ReferenceProperties>Flavor=A%3BB%3DC</ReferenceProperties>
                  </PropertyGroup>
                  <PropertyGroup><ReferenceProperties>Flavor=A;B=C</ReferenceProperties></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="$(ReferenceProperties)" />
                  </ItemGroup>
                </Project>
                """,
            libraryProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'A;B=C'">
                    <Compile Include="../../inputs/Stale.cs" />
                  </ItemGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'A' and '$(B)' == 'C'">
                    <Compile Include="../../inputs/Selected.cs" />
                  </ItemGroup>
                </Project>
                """,
            repositoryFiles: new Dictionary<string, string>
            {
                ["inputs/Stale.cs"] = "public static class StaleInput { }",
                ["inputs/Selected.cs"] = "public static class SelectedInput { }"
            },
            mutatedPath: "inputs/Stale.cs");

        Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
    }

    [Fact]
    public void ReadSourceProvenance_AppliesItemDefinitionsBeforeProjectReferenceItems()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj" />
                  </ItemGroup>
                  <ItemDefinitionGroup>
                    <ProjectReference>
                      <AdditionalProperties>A=1;B=2</AdditionalProperties>
                    </ProjectReference>
                  </ItemDefinitionGroup>
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
            repositoryFiles: new Dictionary<string, string>
            {
                ["inputs/Selected.cs"] = "public static class SelectedInput { }"
            },
            mutatedPath: "inputs/Selected.cs");

        Assert.True(provenance.Dirty);
        Assert.Contains(
            provenance.DirtyPaths,
            path => path.Replace('\\', '/').EndsWith("inputs/Selected.cs", StringComparison.Ordinal));
    }
}
