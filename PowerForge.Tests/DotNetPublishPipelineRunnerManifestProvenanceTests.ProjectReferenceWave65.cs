using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ControlledBuildInputs_RejectAspNetCompilerTargetPathReparsePoint()
    {
        AssertRejectsTaskOutputReparsePoint(
            "<AspNetCompiler PhysicalPath=\"site\" TargetPath=\"output-link\" />",
            "site");
    }

    [Theory]
    [InlineData("OutputPDBFile")]
    [InlineData("OutputDocumentationFile")]
    public void ControlledBuildInputs_RejectWinMdExpSiblingOutputReparsePoint(string attributeName)
    {
        AssertRejectsTaskOutputReparsePoint(
            $"<WinMDExp WinMDModule=\"input.winmd\" OutputWindowsMetadataFile=\"output.winmd\" {attributeName}=\"output-link/result.bin\" />",
            null,
            "input.winmd");
    }

    [Theory]
    [InlineData("template")]
    [InlineData("evidence")]
    public void ControlledBuildInputs_RejectAlResponseFileExternalInput(string switchName)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string externalPath = Path.Combine(externalRoot, "payload.dll");
            string linkPath = Path.Combine(root, "payload-link.dll");
            File.WriteAllText(externalPath, "external");
            try
            {
                File.CreateSymbolicLink(linkPath, externalPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }
            string responsePath = Path.Combine(root, "linker.rsp");
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(responsePath, $"/{switchName}:payload-link.dll");
            File.WriteAllText(
                projectPath,
                "<Project><Target Name=\"Build\"><AL ResponseFiles=\"linker.rsp\" OutputAssembly=\"output.dll\" /></Target></Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, responsePath],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    private static void AssertRejectsTaskOutputReparsePoint(
        string taskXml,
        string? inputDirectory = null,
        string? inputFile = null)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            if (inputDirectory is not null)
                Directory.CreateDirectory(Path.Combine(root, inputDirectory));
            if (inputFile is not null)
                File.WriteAllText(Path.Combine(root, inputFile), "controlled");
            try
            {
                Directory.CreateSymbolicLink(Path.Combine(root, "output-link"), externalRoot);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(
                projectPath,
                $"<Project><Target Name=\"Build\">{taskXml}</Target></Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }
}
