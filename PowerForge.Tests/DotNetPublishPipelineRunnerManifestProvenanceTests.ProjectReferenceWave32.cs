using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ReadSourceProvenance_RejectsGeneratedOutputWithModifiedSectionHeader()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadTamperedGeneratedOutputFixture(path =>
        {
            byte[] image = File.ReadAllBytes(path);
            using var reader = new PEReader(new MemoryStream(image, writable: false));
            int sectionHeadersStart = reader.PEHeaders.PEHeaderStartOffset +
                                      reader.PEHeaders.CoffHeader.SizeOfOptionalHeader;
            int characteristicsOffset = sectionHeadersStart + 36;
            uint characteristics = BinaryPrimitives.ReadUInt32LittleEndian(
                image.AsSpan(characteristicsOffset, sizeof(uint)));
            BinaryPrimitives.WriteUInt32LittleEndian(
                image.AsSpan(characteristicsOffset, sizeof(uint)),
                characteristics ^ 0x80000000u);
            File.WriteAllBytes(path, image);
        });

        Assert.True(provenance.Dirty);
        Assert.Contains(
            provenance.DirtyReasons,
            reason => reason.Contains("Library.dll", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReadSourceProvenance_RejectsGeneratedOutputWithModifiedEmbeddedDebugPayload()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadTamperedGeneratedOutputFixture(
            path =>
            {
                byte[] image = File.ReadAllBytes(path);
                using var reader = new PEReader(new MemoryStream(image, writable: false));
                DebugDirectoryEntry embedded = reader.ReadDebugDirectory().Single(entry =>
                    entry.Type == DebugDirectoryEntryType.EmbeddedPortablePdb);
                image[embedded.DataPointer + embedded.DataSize - 1] ^= 0x5A;
                File.WriteAllBytes(path, image);
            },
            embeddedDebugInformation: true);

        Assert.True(provenance.Dirty);
        Assert.Contains(
            provenance.DirtyReasons,
            reason => reason.Contains("Library.dll", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReadSourceProvenance_IsolatesSourceMutationFromControlledChildBuild()
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
            string trackedPath = Path.Combine(libraryDirectory, "Library.cs");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      ReferenceOutputAssembly="false"
                                      OutputItemType="EmbeddedResource" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(libraryProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <Target Name="MutateTrackedSourceAfterControlledBuild"
                          AfterTargets="Rebuild"
                          Condition="$([System.String]::Copy('$(OutDir)').Contains('powerforge-provenance-build-'))">
                    <WriteLinesToFile File="$(MSBuildProjectDirectory)/Library.cs"
                                      Lines="public static class Library { public const int Mutated = 1; }"
                                      Overwrite="true" />
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

            Assert.Equal("public static class Library { }", File.ReadAllText(trackedPath));
            Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
            Assert.DoesNotContain(
                provenance.DirtyReasons,
                reason => reason.Contains("Git status changed", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ReadSourceProvenance_FailsClosedForUnresolvedTaskWideRemoval()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup>
                  <Target Name="ConfigureTaskWideRemoval" BeforeTargets="ResolveReferences">
                    <ItemGroup><RemovalName Include="Flavor" /></ItemGroup>
                    <PropertyGroup>
                      <_GlobalPropertiesToRemoveFromProjectReferences>@(RemovalName)</_GlobalPropertiesToRemoveFromProjectReferences>
                    </PropertyGroup>
                  </Target>
                </Project>
                """,
            libraryProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup Condition="'$(Flavor)' == ''">
                    <Compile Include="../../inputs/Selected.cs" />
                  </ItemGroup>
                </Project>
                """,
            repositoryFiles: SelectedInput,
            mutatedPath: "inputs/Selected.cs",
            buildProperties: new Dictionary<string, string>
            {
                ["Flavor"] = "Inherited"
            },
            buildFramework: "net8.0");

        Assert.True(provenance.Dirty);
        Assert.Contains(
            provenance.DirtyReasons,
            reason => reason.Contains("MSBuild input evaluation failed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReadSourceProvenance_PreservesReferenceRemovedAfterResolution()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup>
                  <Target Name="RemoveResolvedReference" AfterTargets="ResolveReferences">
                    <ItemGroup>
                      <_MSBuildProjectReferenceExistent Remove="../Library/Library.csproj" />
                    </ItemGroup>
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
            mutatedPath: "inputs/Selected.cs");

        AssertSelectedInputIsDirty(provenance);
    }

    private static DotNetPublishPipelineRunner.SourceProvenance ReadTamperedGeneratedOutputFixture(
        Action<string> tamper,
        bool embeddedDebugInformation = false)
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
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      ReferenceOutputAssembly="false"
                                      OutputItemType="EmbeddedResource" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(libraryProject, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <DebugType>{(embeddedDebugInformation ? "embedded" : "portable")}</DebugType>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            RunDotNet(root, $"build \"{libraryProject}\" -c Release --no-restore --nologo");
            string libraryOutput = Path.Combine(libraryDirectory, "bin", "Release", "net8.0", "Library.dll");
            tamper(libraryOutput);

            return DotNetPublishPipelineRunner.ReadSourceProvenance(
                root,
                buildProjectPaths: [appProject],
                buildConfiguration: "Release");
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}
