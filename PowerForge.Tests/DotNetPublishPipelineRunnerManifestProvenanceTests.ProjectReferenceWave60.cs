using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ControlledBuildInputs_RejectActualProjectPathOutputReparsePoint()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            string targetsPath = Path.Combine(root, "build.targets");
            string sourcePath = Path.Combine(root, "payload.bin");
            string externalPath = Path.Combine(externalRoot, "output.bin");
            File.WriteAllText(sourcePath, "controlled");
            File.WriteAllText(externalPath, "external");
            try
            {
                File.CreateSymbolicLink(projectPath + ".out", externalPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }
            File.WriteAllText(projectPath, "<Project><Import Project=\"build.targets\" /></Project>");
            File.WriteAllText(targetsPath, "<Project><Target Name=\"Build\"><Copy SourceFiles=\"payload.bin\" DestinationFiles=\"$(MSBuildProjectFullPath).out\" /></Target></Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, targetsPath, sourcePath],
                [projectPath, targetsPath],
                evaluatedGlobalProperties: null,
                taskInputBaseDirectory: root,
                controlledProjectPath: projectPath));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ControlledBuildInputs_AcceptExplicitProjectPathWithAdditionalProjectInputs()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            string importedPath = Path.Combine(root, "build.proj");
            string sourcePath = Path.Combine(root, "payload.bin");
            File.WriteAllText(sourcePath, "controlled");
            File.WriteAllText(projectPath, "<Project><Import Project=\"build.proj\" /></Project>");
            File.WriteAllText(importedPath, "<Project><Target Name=\"Build\"><Copy SourceFiles=\"payload.bin\" DestinationFiles=\"$(MSBuildProjectFullPath).out\" /></Target></Project>");

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, importedPath, sourcePath],
                [projectPath, importedPath],
                evaluatedGlobalProperties: null,
                taskInputBaseDirectory: root,
                controlledProjectPath: projectPath));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectXslTransformationOutputReparsePoint()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            string externalPath = Path.Combine(externalRoot, "output.xml");
            File.WriteAllText(Path.Combine(root, "payload.xml"), "<root />");
            File.WriteAllText(Path.Combine(root, "transform.xsl"), "<xsl:stylesheet xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\" version=\"1.0\" />");
            File.WriteAllText(externalPath, "external");
            try
            {
                File.CreateSymbolicLink(Path.Combine(root, "payload-link.xml"), externalPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }
            File.WriteAllText(projectPath, "<Project><Target Name=\"Build\"><XslTransformation XmlInputPaths=\"payload.xml\" XslInputPath=\"transform.xsl\" OutputPaths=\"payload-link.xml\" /></Target></Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root, [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Theory]
    [InlineData("/doc:payload-link")]
    [InlineData("/errorlog:payload-link,version=2.1")]
    [InlineData("/generatedfilesout:payload-link")]
    [InlineData("-o:payload-link")]
    [InlineData("/out:payload-link")]
    [InlineData("/pdb:payload-link")]
    [InlineData("/refout:payload-link")]
    [InlineData("--sig:payload-link")]
    [InlineData("/touchedfiles:payload-link")]
    [InlineData("--xml:payload-link")]
    public void ControlledBuildInputs_RejectCompilerResponseFileOutputReparsePoint(string outputSwitch)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            string responsePath = Path.Combine(root, "compiler.rsp");
            string externalPath = Path.Combine(externalRoot, "output.bin");
            File.WriteAllText(responsePath, outputSwitch);
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
    [InlineData("Csc", "DocumentationFile")]
    [InlineData("Csc", "ErrorLog")]
    [InlineData("Csc", "GeneratedFilesOutputPath")]
    [InlineData("Csc", "TouchedFilesPath")]
    [InlineData("Vbc", "DocumentationFile")]
    [InlineData("Fsc", "DocumentationFile")]
    public void ControlledBuildInputs_RejectCompilerTaskOutputReparsePoint(
        string taskName,
        string attributeName)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
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
            File.WriteAllText(projectPath, $"<Project><Target Name=\"Build\"><{taskName} {attributeName}=\"payload-link\" /></Target></Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root, [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectCallTargetDestinationOutsideScannedDocuments()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, "<Project><Target Name=\"Build\"><CallTarget Targets=\"Run\" /></Target></Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root, [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_AcceptCallTargetDestinationInScannedDocuments()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, "<Project><Target Name=\"Build\"><CallTarget Targets=\"ControlledTarget\" /></Target><Target Name=\"ControlledTarget\"><Message Text=\"controlled\" /></Target></Project>");

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root, [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}
