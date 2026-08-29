using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ReadSourceProvenance_PreservesContextWhenComputedRemoveIdentityIsAmbiguous()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <PropertyGroup Condition="$([System.String]::Copy('active').Equals('active'))">
                    <RemoveTarget>../Other/Other.csproj</RemoveTarget>
                  </PropertyGroup>
                  <PropertyGroup Condition="$([System.String]::Copy('active').Equals('inactive'))">
                    <RemoveTarget>../Library/Library.csproj</RemoveTarget>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Other/Other.csproj" />
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="A=1;B=2" />
                    <ProjectReference Remove="$([System.String]::Copy('$(RemoveTarget)'))" />
                  </ItemGroup>
                </Project>
                """,
            libraryProjectXml: ConditionedLibraryProject,
            repositoryFiles: SelectedInput,
            mutatedPath: "inputs/Selected.cs",
            appFiles: new Dictionary<string, string>
            {
                ["../Other/Other.csproj"] =
                    "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>"
            });

        AssertSelectedInputIsDirty(provenance);
        Assert.DoesNotContain(
            provenance.DirtyReasons,
            reason => reason.Contains("MSBuild input evaluation failed", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadSourceProvenance_RejectsOutputBuiltThroughConfiguredSmudgeFilter()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "Library")).FullName;
            string cacheDirectory = Directory.CreateDirectory(Path.Combine(root, ".cache")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string libraryProject = Path.Combine(libraryDirectory, "Library.csproj");
            string librarySource = Path.Combine(libraryDirectory, "Library.cs");
            string placeholderPath = Path.Combine(cacheDirectory, "placeholder.cs");
            string payloadPath = Path.Combine(cacheDirectory, "payload.cs");
            File.WriteAllText(appProject, EmbeddedLibraryAppProject);
            File.WriteAllText(
                libraryProject,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { }");
            const string placeholder = "public static class Library { public const int Value = 1; }";
            const string payload = "public static class Library { public const int Value = 2; }";
            File.WriteAllText(librarySource, placeholder);
            File.WriteAllText(placeholderPath, placeholder);
            File.WriteAllText(payloadPath, payload);
            File.WriteAllText(Path.Combine(root, ".gitattributes"), "src/Library/Library.cs filter=payload\n");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n.cache/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");

            string gitPlaceholderPath = placeholderPath.Replace('\\', '/');
            string gitPayloadPath = payloadPath.Replace('\\', '/');
            RunGit(root, $"config filter.payload.clean \"cat '{gitPlaceholderPath}'\"");
            RunGit(root, $"config filter.payload.smudge \"cat '{gitPayloadPath}'\"");
            RunGit(root, "config filter.payload.required true");
            File.Delete(librarySource);
            RunGit(root, "checkout -- src/Library/Library.cs");
            Assert.Equal(payload, File.ReadAllText(librarySource));
            Assert.Equal(string.Empty, RunGit(root, "status --porcelain").Trim());

            RunDotNet(root, $"build \"{libraryProject}\" -c Release --no-restore --nologo");
            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
            Assert.Contains(
                provenance.DirtyReasons,
                reason => reason.Contains("Library.dll", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ReadSourceProvenance_RejectsGeneratedOutputInsideRecordedSubmodule()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string leafRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(leafRoot, "init");
            RunGit(leafRoot, "config user.name \"PowerForge Tests\"");
            RunGit(leafRoot, "config user.email \"powerforge-tests@example.invalid\"");
            File.WriteAllText(
                Path.Combine(leafRoot, "Leaf.csproj"),
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(leafRoot, "Leaf.cs"), "public static class Leaf { public const int Value = 1; }");
            File.WriteAllText(Path.Combine(leafRoot, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(
                leafRoot,
                "restore \"Leaf.csproj\" --use-lock-file --nologo -p:BaseIntermediateOutputPath=obj/");
            RunGit(leafRoot, "add .");
            RunGit(leafRoot, "commit -m \"approved leaf\"");

            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            RunGit(root, "config protocol.file.allow always");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Leaf/Leaf.csproj"
                                      ReferenceOutputAssembly="false"
                                      OutputItemType="EmbeddedResource" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunGit(
                root,
                $"-c protocol.file.allow=always submodule add \"{leafRoot.Replace('\\', '/')}\" src/Leaf");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            string submoduleRoot = Path.Combine(root, "src", "Leaf");
            string leafProject = Path.Combine(submoduleRoot, "Leaf.csproj");
            RunDotNet(root, $"build \"{leafProject}\" -c Release --no-restore --nologo");
            Assert.Equal(
                string.Empty,
                RunGit(submoduleRoot, "status --porcelain=v1 --untracked-files=all").Trim());
            Assert.Contains(
                RunGit(submoduleRoot, "rev-parse HEAD").Trim(),
                RunGit(root, "ls-files --stage -- src/Leaf"),
                StringComparison.OrdinalIgnoreCase);

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
            Assert.Contains(
                provenance.DirtyReasons,
                reason => reason.Contains("Leaf.dll", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(leafRoot);
        }
    }

    [Fact]
    public void ReadSourceProvenance_UsesBuildTargetForControlledProof()
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
            string libraryProject = Path.Combine(libraryDirectory, "Library.csproj");
            File.WriteAllText(appProject, EmbeddedLibraryAppProject);
            File.WriteAllText(libraryProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <Target Name="InjectCleanOnlySource" AfterTargets="Clean">
                    <WriteLinesToFile File="$(MSBuildProjectDirectory)/obj/clean-only.cs"
                                      Lines="public static class CleanOnly { public const int Value = 2; }"
                                      Overwrite="true" />
                    <ItemGroup><Compile Include="$(MSBuildProjectDirectory)/obj/clean-only.cs" /></ItemGroup>
                  </Target>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            RunDotNet(root, $"build \"{libraryProject}\" -c Release --no-restore --nologo");

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
            Assert.Empty(provenance.DirtyPaths);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    private const string EmbeddedLibraryAppProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
          <ItemGroup>
            <ProjectReference Include="../Library/Library.csproj"
                              ReferenceOutputAssembly="false"
                              OutputItemType="EmbeddedResource" />
          </ItemGroup>
        </Project>
        """;
}
