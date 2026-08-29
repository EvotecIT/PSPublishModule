using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Theory]
    [InlineData("ZipDirectory", "SourceDirectory")]
    [InlineData("Copy", "SourceFolders")]
    public void ControlledBuildInputs_RejectDirectoryTaskInputDescendantReparsePoint(
        string taskName,
        string attributeName)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string sourceDirectory = Directory.CreateDirectory(Path.Combine(root, "payloads")).FullName;
            string externalPath = Path.Combine(externalRoot, "payload.bin");
            File.WriteAllText(externalPath, "external");
            try
            {
                File.CreateSymbolicLink(Path.Combine(sourceDirectory, "payload-link.bin"), externalPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, $"<Project><Target Name=\"Build\"><{taskName} {attributeName}=\"payloads\" /></Target></Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root, [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Theory]
    [InlineData("Csc")]
    [InlineData("Vbc")]
    [InlineData("Fsc")]
    public void ControlledBuildInputs_RejectCompilerLibrarySearchDescendantReparsePoint(string taskName)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "libs")).FullName;
            string externalPath = Path.Combine(externalRoot, "Payload.dll");
            File.WriteAllText(externalPath, "external");
            try
            {
                File.CreateSymbolicLink(Path.Combine(libraryDirectory, "Payload.dll"), externalPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, $"<Project><Target Name=\"Build\"><{taskName} AdditionalLibPaths=\"libs\" References=\"Payload.dll\" /></Target></Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root, [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ControlledBuildInputs_AcceptControlledCompilerLibrarySearchDirectory()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "libs")).FullName;
            File.WriteAllText(Path.Combine(libraryDirectory, "Payload.dll"), "controlled");
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, "<Project><Target Name=\"Build\"><Csc AdditionalLibPaths=\"libs\" References=\"Payload.dll\" /></Target></Project>");

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root, [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectSdkCompilerLibrarySearchDescendantReparsePoint()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "libs")).FullName;
            string externalPath = Path.Combine(externalRoot, "Payload.dll");
            File.WriteAllText(externalPath, "external");
            try
            {
                File.CreateSymbolicLink(Path.Combine(libraryDirectory, "Payload.dll"), externalPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, "<Project><PropertyGroup><AdditionalLibPaths>libs</AdditionalLibPaths></PropertyGroup></Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root, [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ControlledBuildInputs_AcceptControlledSdkCompilerLibrarySearchDirectory()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "libs")).FullName;
            File.WriteAllText(Path.Combine(libraryDirectory, "Payload.dll"), "controlled");
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, "<Project><PropertyGroup><AdditionalLibPaths>libs</AdditionalLibPaths></PropertyGroup></Project>");

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root, [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectCompilerResponseLibrarySearchDescendantReparsePoint()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "libs")).FullName;
            string externalPath = Path.Combine(externalRoot, "Payload.dll");
            File.WriteAllText(externalPath, "external");
            try
            {
                File.CreateSymbolicLink(Path.Combine(libraryDirectory, "Payload.dll"), externalPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }
            string responsePath = Path.Combine(root, "compiler.rsp");
            File.WriteAllLines(responsePath, ["/lib:libs", "/reference:Payload.dll"]);
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, "<Project><Target Name=\"Build\"><Csc ResponseFiles=\"compiler.rsp\" /></Target></Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, responsePath]));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Theory]
    [InlineData("<PropertyGroup><OtherFlags>--out:payload-link</OtherFlags></PropertyGroup>")]
    [InlineData("<PropertyGroup><FscOtherFlags>--doc:payload-link</FscOtherFlags></PropertyGroup>")]
    [InlineData("<PropertyGroup><DotnetFscCompilerPath>payload-link</DotnetFscCompilerPath></PropertyGroup>")]
    [InlineData("<ItemGroup><FscCompilerTools Include=\"payload-link\" /></ItemGroup>")]
    [InlineData("<Target Name=\"Build\"><Fsc OtherFlags=\"--out:payload-link\" /></Target>")]
    public void ControlledBuildInputs_RejectFreeFormCompilerAndToolOverrides(string body)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, $"<Project>{body}</Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root, [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData("GenerateApplicationManifest", "OutputManifest")]
    [InlineData("GenerateDeploymentManifest", "OutputManifest")]
    [InlineData("GenerateTrustInfo", "TrustInfoFile")]
    [InlineData("UpdateManifest", "OutputManifest")]
    [InlineData("GenerateBindingRedirects", "OutputAppConfigFile")]
    [InlineData("AddToWin32Manifest", "ManifestPath")]
    [InlineData("GenerateResource", "StronglyTypedFileName")]
    [InlineData("WinMDExp", "OutputWindowsMetadataFile")]
    public void ControlledBuildInputs_RejectManifestAndResourceOutputReparsePoint(
        string taskName,
        string attributeName)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string externalPath = Path.Combine(externalRoot, "output.bin");
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
            File.WriteAllText(projectPath, $"<Project><Target Name=\"Build\"><{taskName} {attributeName}=\"payload-link\" /></Target></Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root, [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }
}
