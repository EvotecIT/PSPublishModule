using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Theory]
    [InlineData("BeforeTargets")]
    [InlineData("DependsOnTargets")]
    public void ControlledBuildInputs_RejectPrerequisiteActivationOfEvaluatedInactiveTarget(
        string schedulingMode)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            string externalPath = Path.Combine(externalRoot, "payload.txt");
            string linkedPath = Path.Combine(root, "payload-link.txt");
            File.WriteAllText(externalPath, "untracked external payload");
            try
            {
                File.CreateSymbolicLink(linkedPath, externalPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }
            string buildDependencies = schedulingMode == "DependsOnTargets"
                ? " DependsOnTargets=\"ActivateExternalInput\""
                : string.Empty;
            string activationSchedule = schedulingMode == "BeforeTargets"
                ? " BeforeTargets=\"Build\""
                : string.Empty;
            File.WriteAllText(
                projectPath,
                $"""
                <Project>
                  <PropertyGroup><UseExternal>false</UseExternal></PropertyGroup>
                  <Target Name="ActivateExternalInput"{activationSchedule}>
                    <PropertyGroup><UseExternal>true</UseExternal></PropertyGroup>
                  </Target>
                  <Target Name="Build"{buildDependencies}
                          Condition="'$(UseExternal)' == 'true'">
                    <Copy SourceFiles="payload-link.txt" DestinationFolder="output" />
                  </Target>
                </Project>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath],
                [projectPath],
                evaluatedGlobalProperties: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["UseExternal"] = "false"
                }));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectInputActivatedWhenOmittedEnvironmentPropertyIsAbsent()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            string externalPath = Path.Combine(externalRoot, "payload.txt");
            string linkedPath = Path.Combine(root, "payload-link.txt");
            File.WriteAllText(externalPath, "untracked external payload");
            try
            {
                File.CreateSymbolicLink(linkedPath, externalPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }
            File.WriteAllText(
                projectPath,
                """
                <Project>
                  <Target Name="Build" Condition="'$(PRIVATE_BUILD_TOKEN)' == ''">
                    <Copy SourceFiles="payload-link.txt" DestinationFolder="output" />
                  </Target>
                </Project>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath],
                [projectPath],
                evaluatedGlobalProperties: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectImportedPrerequisiteActivationOfInactiveTarget()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            string targetsPath = Path.Combine(root, "BuildHooks.targets");
            string externalPath = Path.Combine(externalRoot, "payload.txt");
            string linkedPath = Path.Combine(root, "payload-link.txt");
            File.WriteAllText(externalPath, "untracked external payload");
            try
            {
                File.CreateSymbolicLink(linkedPath, externalPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }
            File.WriteAllText(
                targetsPath,
                """
                <Project>
                  <Target Name="ActivateExternalInput" BeforeTargets="Build">
                    <PropertyGroup><UseExternal>true</UseExternal></PropertyGroup>
                  </Target>
                </Project>
                """);
            File.WriteAllText(
                projectPath,
                """
                <Project>
                  <Import Project="BuildHooks.targets" />
                  <PropertyGroup><UseExternal>false</UseExternal></PropertyGroup>
                  <Target Name="Build" Condition="'$(UseExternal)' == 'true'">
                    <Copy SourceFiles="payload-link.txt" DestinationFolder="output" />
                  </Target>
                </Project>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, targetsPath],
                [projectPath, targetsPath],
                evaluatedGlobalProperties: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["UseExternal"] = "false"
                }));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }
}
