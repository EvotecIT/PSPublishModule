using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ReadSourceProvenance_RecoversProjectReferencePropertyFunctionMetadata()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="Flavor=$([System.String]::Copy('A'));Mode=Signed" />
                  </ItemGroup>
                </Project>
                """,
            libraryProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'A' and '$(Mode)' == 'Signed'">
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
    public void ReadSourceProvenance_RecoversProjectReferenceMetadataUpdatedBeforeResolveReferences()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj" />
                  </ItemGroup>
                  <Target Name="SetReferenceProperties" BeforeTargets="ResolveReferences">
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
    public void ReadSourceProvenance_PrefersExecutedTargetOverwriteWithDecodedMetadataCollision()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="Flavor=A%3BB%3DC" />
                  </ItemGroup>
                  <Target Name="SetReferenceProperties" BeforeTargets="ResolveReferences">
                    <ItemGroup>
                      <ProjectReference Update="../Library/Library.csproj"
                                        AdditionalProperties="Flavor=A;B=C" />
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
    public void ReadSourceProvenance_DoesNotTrustMetadataFromAnUnexecutedTarget()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="Flavor=A%3BB%3DC" />
                  </ItemGroup>
                  <Target Name="NeverRuns">
                    <ItemGroup>
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
                    <Compile Include="../../inputs/Stale.cs" />
                  </ItemGroup>
                </Project>
                """,
            repositoryFiles: new Dictionary<string, string>
            {
                ["inputs/Selected.cs"] = "public static class SelectedInput { }",
                ["inputs/Stale.cs"] = "public static class StaleInput { }"
            },
            mutatedPath: "inputs/Stale.cs");

        Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
    }
}
