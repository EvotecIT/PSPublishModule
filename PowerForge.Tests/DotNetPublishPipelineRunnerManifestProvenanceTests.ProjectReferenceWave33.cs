using System.Reflection.PortableExecutable;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ReadSourceProvenance_RejectsGeneratedOutputWithModifiedImportAddressTable()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadTamperedGeneratedOutputFixture(path =>
        {
            byte[] image = File.ReadAllBytes(path);
            using var reader = new PEReader(new MemoryStream(image, writable: false));
            DirectoryEntry directory = reader.PEHeaders.PEHeader?.ImportAddressTableDirectory ??
                                       throw new InvalidDataException("Missing PE header.");
            Assert.True(directory.Size > 0);
            int offset = MapTestRvaToFileOffset(reader.PEHeaders, directory.RelativeVirtualAddress);
            image[offset] ^= 0x5A;
            File.WriteAllBytes(path, image);
        });

        Assert.True(provenance.Dirty);
        Assert.Contains(
            provenance.DirtyReasons,
            reason => reason.Contains("Library.dll", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ReadSourceProvenance_RejectsGeneratedOutputCopiedFromIgnoredBuildInput()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "Library")).FullName;
            string cacheDirectory = Directory.CreateDirectory(Path.Combine(libraryDirectory, ".cache")).FullName;
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
            File.WriteAllText(libraryProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <Target Name="ReplaceOutputFromIgnoredInput"
                          AfterTargets="Rebuild"
                          Condition="Exists('$(MSBuildProjectDirectory)/.cache/payload.dll')">
                    <Copy SourceFiles="$(MSBuildProjectDirectory)/.cache/payload.dll"
                          DestinationFiles="$(TargetPath)" />
                  </Target>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n.cache/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            RunDotNet(root, $"build \"{libraryProject}\" -c Release --no-restore --nologo");
            string libraryOutput = Path.Combine(libraryDirectory, "bin", "Release", "net8.0", "Library.dll");
            string ignoredPayload = Path.Combine(cacheDirectory, "payload.dll");
            File.Copy(libraryOutput, ignoredPayload);
            File.AppendAllText(ignoredPayload, "ignored payload overlay");
            RunDotNet(root, $"build \"{libraryProject}\" -c Release --no-restore --nologo -t:Rebuild");
            Assert.Equal(File.ReadAllBytes(ignoredPayload), File.ReadAllBytes(libraryOutput));

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

    private static int MapTestRvaToFileOffset(PEHeaders headers, int relativeVirtualAddress)
    {
        foreach (SectionHeader section in headers.SectionHeaders)
        {
            int sectionSize = Math.Max(section.VirtualSize, section.SizeOfRawData);
            if (relativeVirtualAddress >= section.VirtualAddress &&
                relativeVirtualAddress < section.VirtualAddress + sectionSize)
            {
                return section.PointerToRawData + relativeVirtualAddress - section.VirtualAddress;
            }
        }
        throw new InvalidDataException("The PE directory is outside every section.");
    }
}
