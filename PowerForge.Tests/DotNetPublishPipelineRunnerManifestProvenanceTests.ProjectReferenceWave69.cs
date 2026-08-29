using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Theory]
    [InlineData("LD_PRELOAD")]
    [InlineData("LD_LIBRARY_PATH")]
    [InlineData("LD_AUDIT")]
    [InlineData("DYLD_INSERT_LIBRARIES")]
    [InlineData("DYLD_LIBRARY_PATH")]
    [InlineData("DYLD_FRAMEWORK_PATH")]
    [InlineData("LIBPATH")]
    [InlineData("SHLIB_PATH")]
    public void ControlledBuildEnvironment_RejectsRequestedNativeLoaderInjection(string variableName)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string controlledRoot = Directory.CreateDirectory(Path.Combine(root, "controlled")).FullName;
        try
        {
            Assert.False(DotNetPublishPipelineRunner.TryCreateControlledBuildEnvironment(
                new Dictionary<string, string?> { [variableName] = "payload" },
                root,
                controlledRoot,
                out _));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData("Csc")]
    [InlineData("Vbc")]
    [InlineData("Fsc")]
    public void ControlledBuildInputs_RejectDirectCompilerAnalyzersWithOtherwiseControlledPaths(
        string taskName)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            string sourcePath = Path.Combine(root, "Program.cs");
            string analyzerPath = Path.Combine(root, "analyzer.dll");
            File.WriteAllText(
                projectPath,
                $"<Project><Target Name=\"Build\"><{taskName} Sources=\"Program.cs\" Analyzers=\"analyzer.dll\" OutputAssembly=\"bin/App.dll\" /></Target></Project>");
            File.WriteAllText(sourcePath, "internal static class Program { }");
            File.WriteAllText(analyzerPath, "contained analyzer");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, sourcePath, analyzerPath],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData("COMFileReference")]
    [InlineData("COMReference")]
    [InlineData("NativeReference")]
    public void ReadSourceProvenance_RejectsAmbientReferenceResolutionItem(string itemName)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string projectPath = Path.Combine(root, "Sample.csproj");
            string item = itemName.Equals("COMReference", StringComparison.Ordinal)
                ? """
                    <COMReference Include="Ambient.TypeLibrary">
                      <Guid>{00020430-0000-0000-C000-000000000046}</Guid>
                      <VersionMajor>2</VersionMajor>
                      <VersionMinor>0</VersionMinor>
                    </COMReference>
                    """
                : $"<{itemName} Include=\"contained-reference.bin\" />";
            File.WriteAllText(Path.Combine(root, "contained-reference.bin"), "contained reference");
            File.WriteAllText(projectPath, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    {item}
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(root, "Program.cs"), "internal static class Program { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [projectPath],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}
