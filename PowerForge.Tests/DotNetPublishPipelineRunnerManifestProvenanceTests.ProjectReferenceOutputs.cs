using System.Security.Cryptography;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ReadSourceProvenance_IgnoresLegacyAnalyzerProjectReferenceAssemblyOutput()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;
            string generatorDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "Generator")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string generatorProject = Path.Combine(generatorDirectory, "Generator.csproj");
            File.WriteAllText(appProject, """
                <Project ToolsVersion="Current" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
                  <PropertyGroup>
                    <Configuration>Release</Configuration>
                    <Platform>AnyCPU</Platform>
                    <ProjectGuid>{50E079C5-E34F-4D24-B020-7095B94549C7}</ProjectGuid>
                    <OutputType>Library</OutputType>
                    <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
                    <AssemblyName>App</AssemblyName>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Generator/Generator.csproj">
                      <AdditionalProperties>TargetFramework=net472</AdditionalProperties>
                      <OutputItemType>Analyzer</OutputItemType>
                      <ReferenceOutputAssembly>false</ReferenceOutputAssembly>
                    </ProjectReference>
                  </ItemGroup>
                  <Import Project="$(MSBuildToolsPath)/Microsoft.CSharp.targets" />
                </Project>
                """);
            File.WriteAllText(
                generatorProject,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net472</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(generatorDirectory, "Generator.cs"), "public static class Generator { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            RunDotNet(root, $"restore \"{generatorProject}\" --use-lock-file --nologo");
            RunGit(root, "add src/Generator/packages.lock.json");
            RunGit(root, "commit -m \"lock approved dependencies\"");
            RunDotNet(root, $"build \"{generatorProject}\" -c Release --no-restore --nologo");
            string generatorOutput = Path.Combine(
                generatorDirectory,
                "bin",
                "Release",
                "net472",
                "Generator.dll");
            string outputSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(generatorOutput)));
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release",
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
                                Framework = "net472",
                                Style = DotNetPublishStyle.FrameworkDependent
                            }
                        ]
                    }
                ]
            };

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release",
                    buildPlan: plan);

            Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
            Assert.Empty(provenance.DirtyPaths);
            Assert.Equal(outputSha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(generatorOutput))));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                foreach (FileInfo file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories))
                    file.Attributes = FileAttributes.Normal;
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ReadSourceProvenance_IgnoresEmbeddedProjectReferenceAssemblyOutput()
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
                    <ProjectReference
                      Include="../Library/Library.csproj"
                      AdditionalProperties="TargetFramework=netstandard2.0"
                      ReferenceOutputAssembly="false"
                      OutputItemType="EmbeddedResource"
                      LogicalName="App.Payloads.Library.dll" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(libraryProject, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFrameworks>netstandard2.0;net8.0</TargetFrameworks></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { private static void Main() { } }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add src/*/packages.lock.json");
            RunGit(root, "commit -m \"lock approved dependencies\"");
            RunDotNet(root, $"build \"{libraryProject}\" -c Release -f netstandard2.0 --no-restore --nologo");
            string libraryOutput = Path.Combine(libraryDirectory, "bin", "Release", "netstandard2.0", "Library.dll");
            string outputSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(libraryOutput)));
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release",
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
                                Framework = "net8.0",
                                Style = DotNetPublishStyle.FrameworkDependent
                            }
                        ]
                    }
                ]
            };

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release",
                    buildPlan: plan);

            Assert.False(provenance.Dirty);
            Assert.Empty(provenance.DirtyPaths);
            Assert.Equal(outputSha256, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(libraryOutput))));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                foreach (FileInfo file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories))
                    file.Attributes = FileAttributes.Normal;
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ReadSourceProvenance_TracksEmbeddedDllThatCopiesProjectReferenceMetadata()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "Library")).FullName;
            string inputDirectory = Directory.CreateDirectory(Path.Combine(root, "inputs")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string libraryProject = Path.Combine(libraryDirectory, "Library.csproj");
            string releaseInput = Path.Combine(inputDirectory, "release-input.dll");
            File.WriteAllText(appProject, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference
                      Include="../Library/Library.csproj"
                      AdditionalProperties="TargetFramework=netstandard2.0"
                      ReferenceOutputAssembly="false"
                      OutputItemType="EmbeddedResource"
                      LogicalName="App.Payloads.Library.dll" />
                    <EmbeddedResource Include="../../inputs/release-input.dll">
                      <ReferenceSourceTarget>ProjectReference</ReferenceSourceTarget>
                      <MSBuildSourceProjectFile>{libraryProject}</MSBuildSourceProjectFile>
                      <AdditionalProperties>TargetFramework=netstandard2.0</AdditionalProperties>
                      <ReferenceOutputAssembly>false</ReferenceOutputAssembly>
                      <OutputItemType>EmbeddedResource</OutputItemType>
                      <LogicalName>App.Payloads.Library.dll</LogicalName>
                    </EmbeddedResource>
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(libraryProject, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFrameworks>netstandard2.0;net8.0</TargetFrameworks></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { private static void Main() { } }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllBytes(releaseInput, [0x01, 0x02, 0x03]);
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            RunDotNet(root, $"build \"{libraryProject}\" -c Release -f netstandard2.0 --no-restore --nologo");
            File.WriteAllBytes(releaseInput, [0x04, 0x05, 0x06]);

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
            Assert.Contains(
                provenance.DirtyPaths,
                path => path.Replace('\\', '/').EndsWith("inputs/release-input.dll", StringComparison.Ordinal));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                foreach (FileInfo file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories))
                    file.Attributes = FileAttributes.Normal;
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
