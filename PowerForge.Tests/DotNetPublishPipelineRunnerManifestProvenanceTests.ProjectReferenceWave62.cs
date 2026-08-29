using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ControlledBuildInputs_RejectCompiledXslTransformAssembly()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            string inputPath = Path.Combine(root, "payload.xml");
            string transformPath = Path.Combine(root, "transform.dll");
            File.WriteAllText(projectPath, "<Project><Target Name=\"Build\"><XslTransformation XmlInputPaths=\"payload.xml\" XslCompiledDllPath=\"transform.dll\" OutputPaths=\"output.xml\" /></Target></Project>");
            File.WriteAllText(inputPath, "<payload />");
            File.WriteAllText(transformPath, "compiled transform fixture");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, inputPath, transformPath],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData("AssemblyTitle", "$([System.Environment]::MachineName)")]
    [InlineData("InformationalVersion", "$([System.DateTime]::UtcNow.Ticks)")]
    [InlineData("Description", "$([System.Guid]::NewGuid())")]
    [InlineData("Product", "$([System.Globalization.CultureInfo]::CurrentCulture.Name)")]
    [InlineData("Trademark", "$([System.Runtime.InteropServices.RuntimeInformation]::OSDescription)")]
    [InlineData("Copyright", "$([MSBuild]::GetRegistryValue('HKEY_CURRENT_USER\\Software\\PowerForge', 'Payload', ''))")]
    public void ControlledBuildInputs_RejectAmbientPropertyFunctionInSdkGeneratedMetadata(
        string propertyName,
        string value)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.csproj");
            File.WriteAllText(
                projectPath,
                $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework><{propertyName}>{value}</{propertyName}></PropertyGroup></Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root, [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_AcceptDeterministicPropertyFunctionInSdkGeneratedMetadata()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.csproj");
            File.WriteAllText(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework><AssemblyTitle>$([System.String]::Copy('controlled'))</AssemblyTitle></PropertyGroup></Project>");

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root, [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectMoveDestinationFolderReparsePoint()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string linkPath = Path.Combine(root, "payload-link");
            try
            {
                Directory.CreateSymbolicLink(linkPath, externalRoot);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }
            string sourcePath = Path.Combine(root, "payload.txt");
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(sourcePath, "controlled");
            File.WriteAllText(projectPath, "<Project><Target Name=\"Build\"><Move SourceFiles=\"payload.txt\" DestinationFolder=\"payload-link\" /></Target></Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, sourcePath],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectLatePublishItemReparsePoint()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string externalPath = Path.Combine(externalRoot, "payload.bin");
            File.WriteAllText(externalPath, "external");
            try
            {
                File.CreateSymbolicLink(Path.Combine(root, "payload-link"), externalPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, """
                <Project>
                  <Target Name="AddPayload" BeforeTargets="CopyFilesToPublishDirectory">
                    <ItemGroup>
                      <ResolvedFileToPublish Include="payload-link" RelativePath="payload.bin" />
                    </ItemGroup>
                  </Target>
                </Project>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root, [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ControlledBuildInputs_AcceptTrackedPublishItemInput()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string inputPath = Path.Combine(root, "payload.bin");
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(inputPath, "controlled");
            File.WriteAllText(projectPath, """
                <Project>
                  <Target Name="AddPayload" BeforeTargets="ComputeFilesToPublish">
                    <ItemGroup>
                      <ResolvedFileToPublish Include="payload.bin" RelativePath="data/payload.bin" />
                    </ItemGroup>
                  </Target>
                </Project>
                """);

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, inputPath],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData("../payload.bin")]
    [InlineData("..\\payload.bin")]
    [InlineData("%2e%2e/payload.bin")]
    [InlineData("C:\\payload.bin")]
    public void ControlledPublishRelativePath_RejectsEscapingDestination(string relativePath)
    {
        Assert.False(DotNetPublishPipelineRunner.IsControlledPublishRelativePath(relativePath));
    }

    [Fact]
    public void ReadSourceProvenance_RejectsIgnoredPublishTimeResolvedFileSymlink()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string projectPath = Path.Combine(root, "App.csproj");
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                  <Target Name="AddPublishPayload" BeforeTargets="ComputeFilesToPublish">
                    <ItemGroup>
                      <ResolvedFileToPublish Include="payload-link"
                                             RelativePath="payload.bin"
                                             CopyToPublishDirectory="Always" />
                    </ItemGroup>
                  </Target>
                </Project>
                """);
            File.WriteAllText(Path.Combine(root, "Class1.cs"), "public static class Class1 { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\npayload-link\n");
            RunDotNet(root, $"restore \"{projectPath}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");

            string externalPath = Path.Combine(externalRoot, "payload.bin");
            File.WriteAllText(externalPath, "external");
            try
            {
                File.CreateSymbolicLink(Path.Combine(root, "payload-link"), externalPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [projectPath],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
            Assert.Contains(provenance.DirtyReasons, reason =>
                reason.Contains("payload-link", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }
}
