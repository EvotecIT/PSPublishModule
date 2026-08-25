using PowerForge;
using Xunit;

namespace PowerForge.Tests;
public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ControlledBuildInputs_RejectPropertyResolvedTaskInputReparsePoint()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string externalPath = Path.Combine(externalRoot, "payload.bin");
            string linkPath = Path.Combine(root, "payload-link");
            File.WriteAllText(externalPath, "uncontrolled");
            try
            {
                File.CreateSymbolicLink(linkPath, externalPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }

            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, """
                <Project>
                  <PropertyGroup>
                    <PayloadPath>$(IntermediatePayloadPath)</PayloadPath>
                    <IntermediatePayloadPath>payload-link</IntermediatePayloadPath>
                  </PropertyGroup>
                  <Target Name="Build">
                    <Copy SourceFiles="$(PayloadPath)" DestinationFiles="output.bin" />
                  </Target>
                </Project>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectItemResolvedTaskInputReparsePoint()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string externalPath = Path.Combine(externalRoot, "payload.bin");
            string linkPath = Path.Combine(root, "payload-link");
            File.WriteAllText(externalPath, "uncontrolled");
            try
            {
                File.CreateSymbolicLink(linkPath, externalPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }

            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, """
                <Project>
                  <ItemGroup>
                    <Payload Include="payload-link" />
                  </ItemGroup>
                  <Target Name="Build">
                    <Copy SourceFiles="@(Payload)" DestinationFiles="output.bin" />
                  </Target>
                </Project>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ControlledBuildInputs_AcceptContainedIndirectTaskInputs()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "property-payload.bin"), "controlled");
            File.WriteAllText(Path.Combine(root, "item-payload.bin"), "controlled");
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, """
                <Project>
                  <PropertyGroup>
                    <PayloadPath>property-payload.bin</PayloadPath>
                  </PropertyGroup>
                  <ItemGroup>
                    <Payload Include="item-payload.bin" />
                  </ItemGroup>
                  <Target Name="Build">
                    <Copy SourceFiles="$(PayloadPath);@(Payload)" DestinationFolder="output" />
                  </Target>
                </Project>
                """);

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_GlobalPropertyOverridesProjectProperty()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string externalPath = Path.Combine(externalRoot, "payload.bin");
            string linkPath = Path.Combine(root, "payload-link");
            File.WriteAllText(externalPath, "uncontrolled");
            try
            {
                File.CreateSymbolicLink(linkPath, externalPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }

            string controlledPath = Path.Combine(root, "controlled.bin");
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(controlledPath, "controlled");
            File.WriteAllText(projectPath, """
                <Project>
                  <PropertyGroup><PayloadPath>payload-link</PayloadPath></PropertyGroup>
                  <Target Name="Build">
                    <Copy SourceFiles="$(PayloadPath)" DestinationFiles="output.bin" />
                  </Target>
                </Project>
                """);

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, controlledPath],
                [projectPath],
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PayloadPath"] = controlledPath
                }));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectUncontrolledGlobalPropertyOverride()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string externalPath = Path.Combine(externalRoot, "payload.bin");
            string linkPath = Path.Combine(root, "payload-link");
            File.WriteAllText(externalPath, "uncontrolled");
            try
            {
                File.CreateSymbolicLink(linkPath, externalPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }

            string controlledPath = Path.Combine(root, "controlled.bin");
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(controlledPath, "controlled");
            File.WriteAllText(projectPath, """
                <Project>
                  <PropertyGroup><PayloadPath>controlled.bin</PayloadPath></PropertyGroup>
                  <Target Name="Build">
                    <Copy SourceFiles="$(PayloadPath)" DestinationFiles="output.bin" />
                  </Target>
                </Project>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, controlledPath],
                [projectPath],
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PayloadPath"] = linkPath
                }));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectImportedPropertyResolvedTaskInputReparsePoint()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string externalPath = Path.Combine(externalRoot, "payload.bin");
            string linkPath = Path.Combine(root, "payload-link");
            File.WriteAllText(externalPath, "uncontrolled");
            try
            {
                File.CreateSymbolicLink(linkPath, externalPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }

            string projectPath = Path.Combine(root, "App.proj");
            string targetsPath = Path.Combine(root, "Payload.targets");
            File.WriteAllText(projectPath, """
                <Project>
                  <PropertyGroup><PayloadPath>payload-link</PayloadPath></PropertyGroup>
                  <Import Project="Payload.targets" />
                </Project>
                """);
            File.WriteAllText(targetsPath, """
                <Project>
                  <Target Name="Build">
                    <Copy SourceFiles="$(PayloadPath)" DestinationFiles="output.bin" />
                  </Target>
                </Project>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, targetsPath]));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectMetadataResolvedTaskInputReparsePoint()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string externalPath = Path.Combine(externalRoot, "payload.bin");
            string linkPath = Path.Combine(root, "payload-link");
            File.WriteAllText(externalPath, "uncontrolled");
            try
            {
                File.CreateSymbolicLink(linkPath, externalPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }

            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, """
                <Project>
                  <ItemGroup>
                    <Payload Include="placeholder">
                      <SourcePath>payload-link</SourcePath>
                    </Payload>
                  </ItemGroup>
                  <Target Name="Build">
                    <Copy SourceFiles="@(Payload->'%(SourcePath)')" DestinationFiles="output.bin" />
                  </Target>
                </Project>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectUnresolvedTaskInputProperty()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, """
                <Project>
                  <Target Name="Build">
                    <Copy SourceFiles="$(PayloadPath)" DestinationFiles="output.bin" />
                  </Target>
                </Project>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectImportedThisFileRelativeTaskInputReparsePoint()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string buildRoot = Directory.CreateDirectory(Path.Combine(root, "build")).FullName;
            string externalPath = Path.Combine(externalRoot, "payload.bin");
            string linkPath = Path.Combine(buildRoot, "payload-link");
            File.WriteAllText(externalPath, "uncontrolled");
            try
            {
                File.CreateSymbolicLink(linkPath, externalPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }

            string projectPath = Path.Combine(root, "App.proj");
            string propsPath = Path.Combine(buildRoot, "Payload.props");
            File.WriteAllText(projectPath, """
                <Project>
                  <Import Project="build/Payload.props" />
                  <Target Name="Build">
                    <Copy SourceFiles="$(PayloadPath)" DestinationFiles="output.bin" />
                  </Target>
                </Project>
                """);
            File.WriteAllText(propsPath, """
                <Project>
                  <PropertyGroup>
                    <PayloadPath>$(MSBuildThisFileDirectory)payload-link</PayloadPath>
                  </PropertyGroup>
                </Project>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, propsPath]));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectStaticItemCollidingWithReadLinesOutput()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string externalPath = Path.Combine(externalRoot, "payload.bin");
            string linkPath = Path.Combine(root, "payload-link");
            File.WriteAllText(externalPath, "uncontrolled");
            try
            {
                File.CreateSymbolicLink(linkPath, externalPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }

            string projectPath = Path.Combine(root, "App.proj");
            string pathList = Path.Combine(root, "payload-paths.txt");
            File.WriteAllText(pathList, "controlled.bin");
            File.WriteAllText(Path.Combine(root, "controlled.bin"), "controlled");
            File.WriteAllText(projectPath, """
                <Project>
                  <ItemGroup>
                    <Payload Include="payload-link" />
                  </ItemGroup>
                  <Target Name="Build">
                    <ReadLinesFromFile File="$(MSBuildThisFileDirectory)payload-paths.txt">
                      <Output TaskParameter="Lines" ItemName="Payload" />
                    </ReadLinesFromFile>
                    <Copy SourceFiles="@(Payload)" DestinationFolder="output" />
                  </Target>
                </Project>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ReadSourceProvenance_RejectsNestedSourceHiddenByEqualsNamedCleanFilter()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string leafRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(leafRoot, "init");
            RunGit(leafRoot, "config user.name \"PowerForge Tests\"");
            RunGit(leafRoot, "config user.email \"powerforge-tests@example.invalid\"");
            const string approved = "public static class Leaf { public const int Value = 1; }";
            string leafSource = Path.Combine(leafRoot, "Leaf.cs");
            File.WriteAllText(leafSource, approved);
            File.WriteAllText(Path.Combine(leafRoot, ".gitattributes"), "Leaf.cs filter=foo=bar\n");
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
                  <ItemGroup><Compile Include="../Leaf/Leaf.cs" Link="Leaf.cs" /></ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n.cache/\n");
            RunGit(root, $"-c protocol.file.allow=always submodule add \"{leafRoot.Replace('\\', '/')}\" src/Leaf");
            File.AppendAllText(Path.Combine(root, ".gitmodules"), "\n\tignore = all\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");

            string cacheDirectory = Directory.CreateDirectory(Path.Combine(root, ".cache")).FullName;
            string approvedPath = Path.Combine(cacheDirectory, "approved.cs");
            File.WriteAllText(approvedPath, approved);
            string nestedRoot = Path.Combine(root, "src", "Leaf");
            RunGit(nestedRoot, $"config \"filter.foo=bar.clean\" \"cat '{approvedPath.Replace('\\', '/')}'\"");
            File.WriteAllText(
                Path.Combine(nestedRoot, "Leaf.cs"),
                "public static class Leaf { public const int Value = 2; }");
            Assert.Equal(string.Empty, RunGit(nestedRoot, "status --porcelain").Trim());

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(leafRoot);
        }
    }
}
