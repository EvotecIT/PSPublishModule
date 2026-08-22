using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ReadSourceProvenance_ExpandsProjectReferenceIncludeFromEvaluatedItemList()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ChildProjects Include="../Library/Library.csproj" />
                    <ProjectReference Include="@(ChildProjects)"
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
    public void ReadSourceProvenance_PreservesImmutableGlobalPropertyDuringTargetReplay()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="Flavor=A%3BB%3DC" />
                  </ItemGroup>
                  <Target Name="ChangeConfiguration" BeforeTargets="ResolveReferences">
                    <PropertyGroup><Configuration>Debug</Configuration></PropertyGroup>
                    <ItemGroup Condition="'$(Configuration)' == 'Debug'">
                      <ProjectReference Update="../Library/Library.csproj"
                                        AdditionalProperties="Flavor=A;B=C" />
                    </ItemGroup>
                  </Target>
                </Project>
                """,
            libraryProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'A;B=C'">
                    <Compile Include="../../inputs/Selected.cs" />
                  </ItemGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'A' and '$(B)' == 'C'">
                    <Compile Include="../../inputs/Inactive.cs" />
                  </ItemGroup>
                </Project>
                """,
            repositoryFiles: new Dictionary<string, string>
            {
                ["inputs/Selected.cs"] = "public static class SelectedInput { }",
                ["inputs/Inactive.cs"] = "public static class InactiveInput { }"
            },
            mutatedPath: "inputs/Selected.cs");

        AssertSelectedInputIsDirty(provenance);
    }

    [Fact]
    public void ReadSourceProvenance_ReplaysGlobalPropertyDeclaredAsLocal()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk" TreatAsLocalProperty="Configuration">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="Flavor=A%3BB%3DC" />
                  </ItemGroup>
                  <Target Name="ChangeConfiguration" BeforeTargets="ResolveReferences">
                    <PropertyGroup><Configuration>Debug</Configuration></PropertyGroup>
                    <ItemGroup Condition="'$(Configuration)' == 'Debug'">
                      <ProjectReference Update="../Library/Library.csproj"
                                        AdditionalProperties="Flavor=A;B=C" />
                    </ItemGroup>
                  </Target>
                </Project>
                """,
            libraryProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'A;B=C'">
                    <Compile Include="../../inputs/Selected.cs" />
                  </ItemGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'A' and '$(B)' == 'C'">
                    <Compile Include="../../inputs/Local.cs" />
                  </ItemGroup>
                </Project>
                """,
            repositoryFiles: new Dictionary<string, string>
            {
                ["inputs/Selected.cs"] = "public static class SelectedInput { }",
                ["inputs/Local.cs"] = "public static class LocalInput { }"
            },
            mutatedPath: "inputs/Local.cs");

        Assert.True(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
        Assert.Contains(
            provenance.DirtyPaths,
            path => path.Replace('\\', '/').EndsWith("inputs/Local.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadSourceProvenance_ExpandsFileScopedPropertiesInImportedDeclarationCondition()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <Import Project="../../build/References.props" />
                </Project>
                """,
            libraryProjectXml: ConditionedLibraryProject,
            repositoryFiles: new Dictionary<string, string>
            {
                ["build/References.props"] = """
                    <Project>
                      <PropertyGroup>
                        <ExpectedReferenceDirectory>$(MSBuildThisFileDirectory)</ExpectedReferenceDirectory>
                      </PropertyGroup>
                      <ItemGroup Condition="'$(MSBuildThisFileDirectory)' == '$(ExpectedReferenceDirectory)'">
                        <ProjectReference Include="../Library/Library.csproj"
                                          AdditionalProperties="A=1;B=2" />
                      </ItemGroup>
                    </Project>
                    """,
                ["inputs/Selected.cs"] = "public static class SelectedInput { }"
            },
            mutatedPath: "inputs/Selected.cs");

        AssertSelectedInputIsDirty(provenance);
    }

    private const string ConditionedLibraryProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
          <ItemGroup Condition="'$(A)' == '1' and '$(B)' == '2'">
            <Compile Include="../../inputs/Selected.cs" />
          </ItemGroup>
        </Project>
        """;

    private static readonly IReadOnlyDictionary<string, string> SelectedInput =
        new Dictionary<string, string>
        {
            ["inputs/Selected.cs"] = "public static class SelectedInput { }"
        };

    private static void AssertSelectedInputIsDirty(DotNetPublishPipelineRunner.SourceProvenance provenance)
    {
        Assert.True(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
        Assert.True(
            provenance.DirtyPaths.Any(path =>
                path.Replace('\\', '/').EndsWith("inputs/Selected.cs", StringComparison.Ordinal)),
            string.Join(Environment.NewLine, provenance.DirtyReasons));
    }
}
