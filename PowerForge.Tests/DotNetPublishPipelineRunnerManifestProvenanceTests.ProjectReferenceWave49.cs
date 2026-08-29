using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ControlledBuildInputs_RejectTaskLevelToolOverride()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "App.proj"), """
                <Project>
                  <Target Name="Build">
                    <Csc Sources="Program.cs" ToolPath="tools" ToolExe="compiler-shim.exe" />
                  </Target>
                </Project>
                """);
            File.WriteAllText(Path.Combine(root, "Program.cs"), "internal static class Program { }");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectEscapingResxFileReference()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string externalPath = Path.Combine(externalRoot, "payload.bin");
            File.WriteAllText(externalPath, "uncontrolled");
            string relativePath = Path.GetRelativePath(root, externalPath).Replace('\\', '/');
            File.WriteAllText(Path.Combine(root, "Resources.resx"), $$"""
                <root>
                  <data name="Payload" type="System.Resources.ResXFileRef, System.Windows.Forms">
                    <value>{{relativePath}};System.Byte[], mscorlib</value>
                  </data>
                </root>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ControlledBuildInputs_AcceptContainedResxFileReference()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "payload.bin"), "controlled");
            File.WriteAllText(Path.Combine(root, "Resources.resx"), """
                <root>
                  <data name="Payload" type="System.Resources.ResXFileRef, System.Windows.Forms">
                    <value>payload.bin;System.Byte[], mscorlib</value>
                  </data>
                </root>
                """);

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectValueProducingPropertyFunctionInCondition()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "payload.bin"), "controlled");
            File.WriteAllText(Path.Combine(root, "App.proj"), """
                <Project>
                  <Target Name="Build">
                    <Copy SourceFiles="payload.bin"
                          DestinationFiles="output.bin"
                          Condition="'$([MSBuild]::GetRegistryValue(`HKEY_CURRENT_USER\\Software\\PowerForge`, `Payload`, ``))' != ''" />
                  </Target>
                </Project>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectLiteralTaskInputReparsePointOutsideSelectedInputs()
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
                  <Target Name="Build">
                    <Copy SourceFiles="payload-link" DestinationFiles="output.bin" />
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
    public void ControlledBuildInputs_RejectTaskLoadedReparsePointOutsideSelectedInputs()
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
            string dataPath = Path.Combine(root, "payload-path.txt");
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(dataPath, "payload-link");
            File.WriteAllText(projectPath, """
                <Project>
                  <Target Name="Build">
                    <ReadLinesFromFile File="payload-path.txt">
                      <Output TaskParameter="Lines" ItemName="PayloadPath" />
                    </ReadLinesFromFile>
                    <Copy SourceFiles="@(PayloadPath)" DestinationFiles="output.bin" />
                  </Target>
                </Project>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, dataPath]));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }
}
