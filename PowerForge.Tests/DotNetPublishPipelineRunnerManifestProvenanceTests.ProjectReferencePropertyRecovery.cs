using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ReadSourceProvenance_ExpandsCompositeProjectReferencePropertyExpressions()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="$(CommonProperties);C=3" />
                  </ItemGroup>
                </Project>
                """,
            libraryProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup Condition="'$(A)' == '1' and '$(B)' == '2' and '$(C)' == '3'">
                    <Compile Include="../../inputs/Selected.cs" />
                  </ItemGroup>
                </Project>
                """,
            repositoryFiles: new Dictionary<string, string>
            {
                ["Directory.Build.props"] = """
                    <Project>
                      <PropertyGroup><CommonProperties>A=1;B=2</CommonProperties></PropertyGroup>
                    </Project>
                    """,
                ["inputs/Selected.cs"] = "public static class SelectedInput { public const int Value = 1; }"
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
    public void ReadSourceProvenance_UsesOnlyTheActiveConditionedComputedProjectReferenceDeclaration()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <ActiveProject>../Library/Library.csproj</ActiveProject>
                    <InactiveProject>../Library/Library.csproj</InactiveProject>
                    <ReferenceMode>Escaped</ReferenceMode>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="$(ActiveProject)"
                                      Condition="'$(ReferenceMode)' == 'Escaped' and ('$(Configuration)' == 'Release' or '$(Configuration)' == 'Debug')"
                                      AdditionalProperties="Flavor=A%3BB%3DC" />
                    <ProjectReference Include="$(InactiveProject)"
                                      Condition="'$(ReferenceMode)' != 'Escaped' or '$(Configuration)' == 'Never'"
                                      AdditionalProperties="Flavor=A;B=C" />
                  </ItemGroup>
                </Project>
                """,
            libraryProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'A;B=C'">
                    <Compile Include="../../inputs/Active.cs" />
                  </ItemGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'A' and '$(B)' == 'C'">
                    <Compile Include="../../inputs/Inactive.cs" />
                  </ItemGroup>
                </Project>
                """,
            repositoryFiles: new Dictionary<string, string>
            {
                ["inputs/Active.cs"] = "public static class ActiveInput { public const int Value = 1; }",
                ["inputs/Inactive.cs"] = "public static class InactiveInput { public const int Value = 1; }"
            },
            mutatedPath: "inputs/Inactive.cs");

        Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
        Assert.DoesNotContain(
            provenance.DirtyPaths,
            path => path.Replace('\\', '/').EndsWith("inputs/Inactive.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadSourceProvenance_AllowsEmptySegmentsInProjectReferencePropertyTables()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="A=1;;B=2;" />
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
                ["inputs/Selected.cs"] = "public static class SelectedInput { public const int Value = 1; }"
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
    public void ReadSourceProvenance_RecoversProjectReferenceItemDefinitionMetadata()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemDefinitionGroup>
                    <ProjectReference>
                      <AdditionalProperties>A=1;B=2</AdditionalProperties>
                    </ProjectReference>
                  </ItemDefinitionGroup>
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
                ["inputs/Selected.cs"] = "public static class SelectedInput { public const int Value = 1; }"
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
    public void ReadSourceProvenance_ExpandsEvaluatedProjectPropertiesInMetadataTables()
    {
        const string environmentName = "POWERFORGE_PR797_TEST_FLAVOR";
        string? previousValue = Environment.GetEnvironmentVariable(environmentName);
        try
        {
            Environment.SetEnvironmentVariable(environmentName, "EnvironmentValue");
            DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
                appProjectXml: """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                      <ItemGroup>
                        <ProjectReference Include="../Library/Library.csproj"
                                          AdditionalProperties="ConfigurationValue=$(Configuration);ProjectValue=$(MSBuildProjectName);EnvironmentValue=$(POWERFORGE_PR797_TEST_FLAVOR)" />
                      </ItemGroup>
                    </Project>
                    """,
                libraryProjectXml: """
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                      <ItemGroup Condition="'$(ConfigurationValue)' == 'Release' and '$(ProjectValue)' == 'App' and '$(EnvironmentValue)' == 'EnvironmentValue'">
                        <Compile Include="../../inputs/Selected.cs" />
                      </ItemGroup>
                    </Project>
                    """,
                repositoryFiles: new Dictionary<string, string>
                {
                    ["inputs/Selected.cs"] = "public static class SelectedInput { public const int Value = 1; }"
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
        finally
        {
            Environment.SetEnvironmentVariable(environmentName, previousValue);
        }
    }

    [Fact]
    public void ReadSourceProvenance_HonorsFirstMatchingChooseBranch()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <Choose>
                    <When Condition="'$(Configuration)' == 'Release'">
                      <ItemGroup>
                        <ProjectReference Include="../Library/Library.csproj"
                                          AdditionalProperties="Flavor=A%3BB%3DC" />
                      </ItemGroup>
                    </When>
                    <When Condition="'$(Configuration)' == 'Release'">
                      <ItemGroup>
                        <ProjectReference Include="../Library/Library.csproj"
                                          AdditionalProperties="Flavor=A;B=C" />
                      </ItemGroup>
                    </When>
                  </Choose>
                </Project>
                """,
            libraryProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'A;B=C'">
                    <Compile Include="../../inputs/Active.cs" />
                  </ItemGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'A' and '$(B)' == 'C'">
                    <Compile Include="../../inputs/Inactive.cs" />
                  </ItemGroup>
                </Project>
                """,
            repositoryFiles: new Dictionary<string, string>
            {
                ["inputs/Active.cs"] = "public static class ActiveInput { public const int Value = 1; }",
                ["inputs/Inactive.cs"] = "public static class InactiveInput { public const int Value = 1; }"
            },
            mutatedPath: "inputs/Inactive.cs");

        Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
        Assert.DoesNotContain(
            provenance.DirtyPaths,
            path => path.Replace('\\', '/').EndsWith("inputs/Inactive.cs", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadSourceProvenance_KeepsItemMetadataConditionsEligibleWhenUnresolved()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj">
                      <AdditionalProperties Condition="'%(Filename)' == 'Library'">A=1;B=2</AdditionalProperties>
                    </ProjectReference>
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
                ["inputs/Selected.cs"] = "public static class SelectedInput { public const int Value = 1; }"
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
    public void ReadSourceProvenance_ExpandsFileScopedPropertiesFromTheMetadataDeclaration()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <Import Project="../../build/Reference.props" />
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj" />
                  </ItemGroup>
                </Project>
                """,
            libraryProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup Condition="'$(Mode)' == 'Signed' and Exists('$(Root)Reference.props')">
                    <Compile Include="../../inputs/Selected.cs" />
                  </ItemGroup>
                </Project>
                """,
            repositoryFiles: new Dictionary<string, string>
            {
                ["build/Reference.props"] = """
                    <Project>
                      <ItemDefinitionGroup>
                        <ProjectReference>
                          <AdditionalProperties>Root=$(MSBuildThisFileDirectory);Mode=Signed</AdditionalProperties>
                        </ProjectReference>
                      </ItemDefinitionGroup>
                    </Project>
                    """,
                ["inputs/Selected.cs"] = "public static class SelectedInput { public const int Value = 1; }"
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
    public void ReadSourceProvenance_UsesTheLastEvaluatedProjectReferenceMetadataUpdate()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="Flavor=A%3BB%3DC" />
                  </ItemGroup>
                  <Import Project="../../build/Reference.targets" />
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
                ["build/Reference.targets"] = """
                    <Project>
                      <ItemGroup>
                        <ProjectReference Update="../src/Library/Library.csproj"
                                          AdditionalProperties="Flavor=A;B=C" />
                      </ItemGroup>
                    </Project>
                    """,
                ["inputs/Stale.cs"] = "public static class StaleInput { public const int Value = 1; }",
                ["inputs/Selected.cs"] = "public static class SelectedInput { public const int Value = 1; }"
            },
            mutatedPath: "inputs/Stale.cs");

        Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
        Assert.DoesNotContain(
            provenance.DirtyPaths,
            path => path.Replace('\\', '/').EndsWith("inputs/Stale.cs", StringComparison.Ordinal));
    }

    private static DotNetPublishPipelineRunner.SourceProvenance ReadProjectReferencePropertyRecoveryFixture(
        string appProjectXml,
        string libraryProjectXml,
        IReadOnlyDictionary<string, string> repositoryFiles,
        string mutatedPath,
        IReadOnlyDictionary<string, string>? appFiles = null,
        IReadOnlyDictionary<string, string>? buildProperties = null,
        string? buildFramework = null)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "Library")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            File.WriteAllText(appProject, appProjectXml);
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.csproj"), libraryProjectXml);
            File.WriteAllText(
                Path.Combine(appDirectory, "Program.cs"),
                "internal static class Program { private static void Main() { } }");
            File.WriteAllText(
                Path.Combine(libraryDirectory, "Library.cs"),
                "public static class Library { }");
            foreach (KeyValuePair<string, string> file in appFiles ??
                     new Dictionary<string, string>())
            {
                string path = Path.Combine(appDirectory, file.Key.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, file.Value);
            }
            foreach (KeyValuePair<string, string> file in repositoryFiles)
            {
                string path = Path.Combine(root, file.Key.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, file.Value);
            }
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            string libraryProject = Path.Combine(libraryDirectory, "Library.csproj");
            if (!File.Exists(Path.Combine(libraryDirectory, "obj", "project.assets.json")))
                RunDotNet(root, $"restore \"{libraryProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            string mutatedFile = Path.Combine(root, mutatedPath.Replace('/', Path.DirectorySeparatorChar));
            File.AppendAllText(mutatedFile, Environment.NewLine + "// changed");

            if (buildProperties is null && buildFramework is null)
            {
                return DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");
            }

            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Targets =
                [
                    new DotNetPublishTargetPlan
                    {
                        Name = "App",
                        ProjectPath = appProject,
                        Combinations =
                        [
                            new DotNetPublishTargetCombination
                            {
                                Framework = buildFramework ?? string.Empty,
                                Style = DotNetPublishStyle.FrameworkDependent
                            }
                        ]
                    }
                ]
            };
            foreach (KeyValuePair<string, string> property in buildProperties ??
                     new Dictionary<string, string>())
            {
                plan.MsBuildProperties[property.Key] = property.Value;
            }
            return DotNetPublishPipelineRunner.ReadSourceProvenance(root, buildPlan: plan);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}
