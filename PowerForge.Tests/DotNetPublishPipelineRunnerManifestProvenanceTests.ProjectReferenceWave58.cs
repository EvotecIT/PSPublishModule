using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ControlledBuildInputs_RejectItemTransformReparsePoint()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string externalPath = Path.Combine(externalRoot, "payload.bin");
            string linkPath = Path.Combine(root, "payload-link");
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
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, """
                <Project>
                  <ItemGroup><Prefix Include="pay" /></ItemGroup>
                  <Target Name="Build">
                    <Copy SourceFiles="@(Prefix->'%(Identity)load-link')" DestinationFolder="output" />
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
    public void ControlledBuildInputs_RejectItemTransformExplicitMetadataReparsePoint()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string externalPath = Path.Combine(externalRoot, "payload.bin");
            string linkPath = Path.Combine(root, "payload-link");
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
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, """
                <Project>
                  <ItemGroup>
                    <Prefix Include="unused"><Stem>pay</Stem></Prefix>
                  </ItemGroup>
                  <Target Name="Build">
                    <Copy SourceFiles="@(Prefix->'%(Stem)load-link')" DestinationFolder="output" />
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
    public void ControlledBuildInputs_AcceptContainedItemTransformInput()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "payload.txt"), "controlled");
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, """
                <Project>
                  <ItemGroup><Prefix Include="payload" /></ItemGroup>
                  <Target Name="Build">
                    <Copy SourceFiles="@(Prefix->'%(Identity).txt')" DestinationFolder="output" />
                  </Target>
                </Project>
                """);

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root, [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectWellKnownMetadataTransformReparsePoint()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string externalPath = Path.Combine(externalRoot, "payload.bin");
            string linkPath = Path.Combine(root, "payload-link");
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
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, """
                <Project>
                  <ItemGroup><Prefix Include="payload.seed" /></ItemGroup>
                  <Target Name="Build">
                    <Copy SourceFiles="@(Prefix->'%(Filename)-link')" DestinationFolder="output" />
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
    public void ControlledBuildInputs_RejectItemTransformWithSurroundingTextReparsePoint()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string externalPath = Path.Combine(externalRoot, "payload.bin");
            string linkPath = Path.Combine(root, "payload-link");
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
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, """
                <Project>
                  <ItemGroup><Prefix Include="one;payload" /></ItemGroup>
                  <Target Name="Build">
                    <Copy SourceFiles="pre@(Prefix->'%(Identity)')-link" DestinationFolder="output" />
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
    public void ControlledBuildInputs_RejectItemTransformCustomSeparatorReparsePoint()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string externalPath = Path.Combine(externalRoot, "payload.bin");
            string linkPath = Path.Combine(root, "pay-load-link");
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
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, """
                <Project>
                  <ItemGroup><Part Include="pay;load;link" /></ItemGroup>
                  <Target Name="Build">
                    <Copy SourceFiles="@(Part->'%(Identity)', '-')" DestinationFolder="output" />
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
    public void ControlledBuildInputs_AcceptPropertyExpandedReadLinesFile()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "inputs.txt"), "payload.txt");
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, """
                <Project>
                  <PropertyGroup><ListFile>inputs.txt</ListFile></PropertyGroup>
                  <Target Name="Build">
                    <ReadLinesFromFile File="$(ListFile)">
                      <Output TaskParameter="Lines" ItemName="Inputs" />
                    </ReadLinesFromFile>
                  </Target>
                </Project>
                """);

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root, [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectPropertyExpandedReadLinesFileReparsePoint()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string externalPath = Path.Combine(externalRoot, "inputs.txt");
            string linkPath = Path.Combine(root, "inputs-link.txt");
            File.WriteAllText(externalPath, "payload.txt");
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
                  <PropertyGroup><ListFile>inputs-link.txt</ListFile></PropertyGroup>
                  <Target Name="Build"><ReadLinesFromFile File="$(ListFile)" /></Target>
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
}
