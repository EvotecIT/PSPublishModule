using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ReadSourceProvenance_RejectsGeneratedOutputWithAuthenticodeCertificateTable()
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
            File.WriteAllText(
                libraryProject,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            RunDotNet(root, $"build \"{libraryProject}\" -c Release --no-restore --nologo");
            string libraryOutput = Path.Combine(libraryDirectory, "bin", "Release", "net8.0", "Library.dll");
            AddCertificateTable(libraryOutput);

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
    public void ReadSourceProvenance_UsesTaskWideRemovalsFromResolveReferencesTime()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup>
                  <Target Name="LateRemoval" AfterTargets="ResolveReferences">
                    <PropertyGroup>
                      <_GlobalPropertiesToRemoveFromProjectReferences>Flavor</_GlobalPropertiesToRemoveFromProjectReferences>
                    </PropertyGroup>
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
            },
            buildFramework: "net8.0");

        AssertSelectedInputIsDirty(provenance);
    }

    [Fact]
    public void ReadSourceProvenance_RecoversProjectReferenceFromTaskOutput()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <Target Name="ProduceReference" Returns="@(ProducedReference)">
                    <ItemGroup>
                      <ProducedReference Include="../Library/Library.csproj"
                                         AdditionalProperties="A=1;B=2" />
                    </ItemGroup>
                  </Target>
                  <Target Name="AddReference" BeforeTargets="AssignProjectConfiguration">
                    <CallTarget Targets="ProduceReference">
                      <Output TaskParameter="TargetOutputs" ItemName="ProjectReference" />
                    </CallTarget>
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
            repositoryFiles: SelectedInput,
            mutatedPath: "inputs/Selected.cs");

        AssertSelectedInputIsDirty(provenance);
    }

    [Fact]
    public void ReadSourceProvenance_PreservesDuplicateReferenceRemovalContexts()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj" UndefineProperties="Flavor" />
                    <ProjectReference Include="../Library/Library.csproj" UndefineProperties="Mode" />
                  </ItemGroup>
                  <Target Name="ReplayReferences" BeforeTargets="AssignProjectConfiguration">
                    <ItemGroup><ProjectReference Update="../Library/Library.csproj" /></ItemGroup>
                  </Target>
                </Project>
                """,
            libraryProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'Inherited' or '$(Mode)' == 'Inherited'">
                    <Compile Include="../../inputs/Selected.cs" />
                  </ItemGroup>
                </Project>
                """,
            repositoryFiles: SelectedInput,
            mutatedPath: "inputs/Selected.cs",
            buildProperties: new Dictionary<string, string>
            {
                ["Flavor"] = "Inherited",
                ["Mode"] = "Inherited"
            },
            buildFramework: "net8.0");

        AssertSelectedInputIsDirty(provenance);
    }

    [Fact]
    public void ReadSourceProvenance_RestoresRequestedConditionalTargetFramework()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework Condition="'$(TargetFramework)' == ''">net8.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup>
                </Project>
                """,
            libraryProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFrameworks>net8.0;net10.0</TargetFrameworks></PropertyGroup>
                  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
                    <Compile Include="../../inputs/Selected.cs" />
                  </ItemGroup>
                </Project>
                """,
            repositoryFiles: SelectedInput,
            mutatedPath: "inputs/Selected.cs",
            buildFramework: "net10.0");

        AssertSelectedInputIsDirty(provenance);
    }

    private static void AddCertificateTable(string path)
    {
        byte[] image = File.ReadAllBytes(path);
        int peHeaderStart;
        PEMagic magic;
        using (var stream = File.OpenRead(path))
        using (var reader = new PEReader(stream))
        {
            peHeaderStart = reader.PEHeaders.PEHeaderStartOffset;
            magic = reader.PEHeaders.PEHeader?.Magic ?? throw new InvalidDataException("Missing PE header.");
        }

        int certificateOffset = (image.Length + 7) & ~7;
        Array.Resize(ref image, certificateOffset + 8);
        BinaryPrimitives.WriteInt32LittleEndian(image.AsSpan(certificateOffset, 4), 8);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(certificateOffset + 4, 2), 0x0200);
        BinaryPrimitives.WriteUInt16LittleEndian(image.AsSpan(certificateOffset + 6, 2), 0x0002);

        int dataDirectoryOffset = peHeaderStart + (magic == PEMagic.PE32Plus ? 112 : 96);
        int certificateDirectoryOffset = dataDirectoryOffset + (4 * 8);
        BinaryPrimitives.WriteInt32LittleEndian(
            image.AsSpan(certificateDirectoryOffset, 4),
            certificateOffset);
        BinaryPrimitives.WriteInt32LittleEndian(
            image.AsSpan(certificateDirectoryOffset + 4, 4),
            8);
        File.WriteAllBytes(path, image);
    }
}
