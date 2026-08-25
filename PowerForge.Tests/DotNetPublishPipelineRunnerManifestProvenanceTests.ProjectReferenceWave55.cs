using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Theory]
    [InlineData("Unzip", "SourceFiles")]
    [InlineData("Move", "SourceFiles")]
    [InlineData("GetAssemblyIdentity", "AssemblyFiles")]
    [InlineData("GenerateBindingRedirects", "AppConfigFile")]
    [InlineData("ResolveKeySource", "KeyFile")]
    [InlineData("SignFile", "SigningTarget")]
    [InlineData("UpdateManifest", "InputManifest")]
    [InlineData("VerifyFileHash", "File")]
    [InlineData("WinMDExp", "WinMDModule")]
    [InlineData("XmlPoke", "XmlInputPath")]
    public void ControlledBuildInputs_RejectStandardTaskFileInputReparsePoint(
        string taskName,
        string attributeName)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string externalPath = Path.Combine(externalRoot, "payload.bin");
            string linkPath = Path.Combine(root, "payload-link.bin");
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
            File.WriteAllText(projectPath, $"""
                <Project>
                  <Target Name="Build">
                    <{taskName} {attributeName}="payload-link.bin" />
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
    public void ControlledBuildInputs_RejectImportedRelativeTaskInputReparsePointBesideProject()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;
            string buildDirectory = Directory.CreateDirectory(Path.Combine(root, "build")).FullName;
            string externalPath = Path.Combine(externalRoot, "payload.bin");
            string linkPath = Path.Combine(projectDirectory, "payload-link");
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

            string projectPath = Path.Combine(projectDirectory, "App.proj");
            string targetsPath = Path.Combine(buildDirectory, "Payload.targets");
            File.WriteAllText(projectPath, """
                <Project>
                  <Import Project="../../build/Payload.targets" />
                </Project>
                """);
            File.WriteAllText(targetsPath, """
                <Project>
                  <Target Name="Build">
                    <Copy SourceFiles="payload-link" DestinationFiles="output.bin" />
                  </Target>
                </Project>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, targetsPath],
                [projectPath, targetsPath],
                evaluatedGlobalProperties: null,
                taskInputBaseDirectory: projectDirectory));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ControlledBuildInputs_AcceptImportedRelativeTaskInputBesideProject()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;
            string buildDirectory = Directory.CreateDirectory(Path.Combine(root, "build")).FullName;
            string controlledPath = Path.Combine(projectDirectory, "payload.bin");
            string projectPath = Path.Combine(projectDirectory, "App.proj");
            string targetsPath = Path.Combine(buildDirectory, "Payload.targets");
            File.WriteAllText(controlledPath, "controlled");
            File.WriteAllText(projectPath, """
                <Project>
                  <Import Project="../../build/Payload.targets" />
                </Project>
                """);
            File.WriteAllText(targetsPath, """
                <Project>
                  <Target Name="Build">
                    <Copy SourceFiles="payload.bin" DestinationFiles="output.bin" />
                  </Target>
                </Project>
                """);

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, targetsPath, controlledPath],
                [projectPath, targetsPath],
                evaluatedGlobalProperties: null,
                taskInputBaseDirectory: projectDirectory));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}
