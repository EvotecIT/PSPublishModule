using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ReadSourceProvenance_RecoversMetadataFromTargetBeforeResolveProjectReferences()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj" />
                  </ItemGroup>
                  <Target Name="SetReferenceProperties" BeforeTargets="ResolveProjectReferences">
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
            repositoryFiles: new Dictionary<string, string>
            {
                ["inputs/Selected.cs"] = "public static class SelectedInput { }"
            },
            mutatedPath: "inputs/Selected.cs");

        Assert.True(provenance.Dirty);
        Assert.Contains(
            provenance.DirtyPaths,
            path => path.Replace('\\', '/').EndsWith("inputs/Selected.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(
            provenance.DirtyReasons,
            reason => reason.Contains("MSBuild input evaluation failed", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadSourceProvenance_RecoversMetadataFromResolveReferencesDependency()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <CustomAfterMicrosoftCommonTargets>$(MSBuildProjectDirectory)/../../build/Reference.targets</CustomAfterMicrosoftCommonTargets>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj" />
                  </ItemGroup>
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
                ["build/Reference.targets"] = """
                    <Project>
                      <PropertyGroup>
                        <ResolveReferencesDependsOn>SetReferenceProperties;$(ResolveReferencesDependsOn)</ResolveReferencesDependsOn>
                      </PropertyGroup>
                      <Target Name="SetReferenceProperties">
                        <ItemGroup>
                          <ProjectReference Update="../src/Library/Library.csproj"
                                            AdditionalProperties="A=1;B=2" />
                        </ItemGroup>
                      </Target>
                    </Project>
                    """,
                ["inputs/Selected.cs"] = "public static class SelectedInput { }"
            },
            mutatedPath: "inputs/Selected.cs");

        Assert.True(provenance.Dirty);
        Assert.Contains(
            provenance.DirtyPaths,
            path => path.Replace('\\', '/').EndsWith("inputs/Selected.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(
            provenance.DirtyReasons,
            reason => reason.Contains("MSBuild input evaluation failed", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadSourceProvenance_ExcludesMetadataFromExcludedProjectReferenceInclude()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/*.csproj"
                                      Exclude="../Library/Library.csproj"
                                      AdditionalProperties="Flavor=A;B=C" />
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="Flavor=A%3BB%3DC" />
                  </ItemGroup>
                </Project>
                """,
            libraryProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'A;B=C'">
                    <Compile Include="../../inputs/Selected.cs" />
                  </ItemGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'A' and '$(B)' == 'C'">
                    <Compile Include="../../inputs/Excluded.cs" />
                  </ItemGroup>
                </Project>
                """,
            repositoryFiles: new Dictionary<string, string>
            {
                ["inputs/Selected.cs"] = "public static class SelectedInput { }",
                ["inputs/Excluded.cs"] = "public static class ExcludedInput { }"
            },
            mutatedPath: "inputs/Excluded.cs");

        Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
    }

    [Fact]
    public void ReadSourceProvenance_UsesConditionsFromReferencedPropertyDefinitions()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <Mode>Escaped</Mode>
                    <Common Condition="'$(Mode)' == 'Escaped'">Flavor=A%3BB%3DC</Common>
                    <Common Condition="'$(Mode)' != 'Escaped'">Flavor=A;B=C</Common>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="$(Common)" />
                  </ItemGroup>
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
            mutatedPath: "inputs/Inactive.cs");

        Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
    }

    [Fact]
    public void ReadSourceProvenance_ExpandsPropertiesAssignedInsideExecutedTargets()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj" />
                  </ItemGroup>
                  <Target Name="SetReferenceProperties" BeforeTargets="ResolveReferences">
                    <PropertyGroup>
                      <ReferenceProperties>A=1;B=2</ReferenceProperties>
                    </PropertyGroup>
                    <ItemGroup>
                      <ProjectReference Update="../Library/Library.csproj"
                                        AdditionalProperties="$(ReferenceProperties)" />
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
            repositoryFiles: new Dictionary<string, string>
            {
                ["inputs/Selected.cs"] = "public static class SelectedInput { }"
            },
            mutatedPath: "inputs/Selected.cs");

        Assert.True(provenance.Dirty);
        Assert.Contains(
            provenance.DirtyPaths,
            path => path.Replace('\\', '/').EndsWith("inputs/Selected.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(
            provenance.DirtyReasons,
            reason => reason.Contains("MSBuild input evaluation failed", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadSourceProvenance_ExpandsPropertiesAssignedByDependencyTargets()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj" />
                  </ItemGroup>
                  <Target Name="SetReferenceProperties">
                    <PropertyGroup>
                      <ReferenceProperties>A=1;B=2</ReferenceProperties>
                    </PropertyGroup>
                  </Target>
                  <Target Name="UpdateReferenceProperties"
                          BeforeTargets="ResolveReferences"
                          DependsOnTargets="SetReferenceProperties">
                    <ItemGroup>
                      <ProjectReference Update="../Library/Library.csproj"
                                        AdditionalProperties="$(ReferenceProperties)" />
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
            repositoryFiles: new Dictionary<string, string>
            {
                ["inputs/Selected.cs"] = "public static class SelectedInput { }"
            },
            mutatedPath: "inputs/Selected.cs");

        Assert.True(provenance.Dirty);
        Assert.Contains(
            provenance.DirtyPaths,
            path => path.Replace('\\', '/').EndsWith("inputs/Selected.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(
            provenance.DirtyReasons,
            reason => reason.Contains("MSBuild input evaluation failed", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadSourceProvenance_IgnoresOverriddenTargetDefinitions()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="Flavor=A%3BB%3DC" />
                  </ItemGroup>
                  <Import Project="../../build/Dead.targets" />
                  <Target Name="SetReferenceProperties" BeforeTargets="ResolveReferences" />
                </Project>
                """,
            libraryProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'A;B=C'">
                    <Compile Include="../../inputs/Selected.cs" />
                  </ItemGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'A' and '$(B)' == 'C'">
                    <Compile Include="../../inputs/Dead.cs" />
                  </ItemGroup>
                </Project>
                """,
            repositoryFiles: new Dictionary<string, string>
            {
                ["build/Dead.targets"] = """
                    <Project>
                      <Target Name="SetReferenceProperties" BeforeTargets="ResolveReferences">
                        <ItemGroup>
                          <ProjectReference Update="../src/Library/Library.csproj"
                                            AdditionalProperties="Flavor=A;B=C" />
                        </ItemGroup>
                      </Target>
                    </Project>
                    """,
                ["inputs/Selected.cs"] = "public static class SelectedInput { }",
                ["inputs/Dead.cs"] = "public static class DeadInput { }"
            },
            mutatedPath: "inputs/Dead.cs");

        Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
    }

    [Fact]
    public void ReadSourceProvenance_MatchesRecursiveProjectReferenceGlobWithNoIntermediateDirectory()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/**/Library.csproj"
                                      AdditionalProperties="A=1;B=2" />
                  </ItemGroup>
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
        Assert.DoesNotContain(
            provenance.DirtyReasons,
            reason => reason.Contains("MSBuild input evaluation failed", StringComparison.Ordinal));
    }
}
