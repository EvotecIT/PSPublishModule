using System.Reflection;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ControlledBuildInputs_IgnoreAssignmentsInsideInactiveTarget()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(
                projectPath,
                """
                <Project>
                  <Target Name="CopyNative" Condition="'$(IsCertNoobProject)' == 'true' and '$(TargetFramework)' == 'net472'">
                    <PropertyGroup>
                      <NativeRid>win-x64</NativeRid>
                    </PropertyGroup>
                    <ItemGroup>
                      <NativeFile Include="$(OutDir)native.dll" Condition="Exists('$(NativeRid)/native.dll')" />
                    </ItemGroup>
                    <Copy SourceFiles="@(NativeFile)" DestinationFiles="$(OutDir)native.dll" />
                  </Target>
                </Project>
                """);

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath],
                [projectPath],
                evaluatedGlobalProperties: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["IsCertNoobProject"] = "false",
                    ["TargetFramework"] = "net10.0-windows"
                }));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectImplicitLastTaskResultActivation()
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
                  <Target Name="Check" BeforeTargets="CopyNative">
                    <Message Text="completed" />
                  </Target>
                  <Target Name="CopyNative" Condition="'$(MSBuildLastTaskResult)' == 'true'">
                    <PropertyGroup><NativeRid>win-x64</NativeRid></PropertyGroup>
                    <ItemGroup>
                      <NativeFile Include="payload-link.txt" Condition="Exists('payload-link.txt')" />
                    </ItemGroup>
                    <Copy SourceFiles="@(NativeFile)" DestinationFolder="output" />
                  </Target>
                </Project>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath],
                [projectPath],
                evaluatedGlobalProperties: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["MSBuildLastTaskResult"] = "false"
                }));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Theory]
    [InlineData("<Project><Target Name='BuildOnlySettings'><PropertyGroup><BuildingProject>true</BuildingProject></PropertyGroup></Target></Project>")]
    [InlineData("<Project TreatAsLocalProperty='BuildingProject' />")]
    [InlineData("<Project><Target Name='BuildOnlySettings'><Message Text='done'><Output TaskParameter='Text' PropertyName='BuildingProject' /></Message></Target></Project>")]
    public void ImmutableTargetGuardProperties_ExcludeExternalMutations(string importedProject)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string importPath = Path.Combine(root, "Imported.targets");
            File.WriteAllText(importPath, importedProject);
            MethodInfo method = typeof(DotNetPublishPipelineRunner).GetMethod(
                "ReadImmutableTargetGuardProperties",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            var globals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["BuildingProject"] = "false",
                ["TargetFramework"] = "net10.0-windows"
            };

            var immutable = (IReadOnlyDictionary<string, string>)method.Invoke(
                null,
                [globals, new[] { importPath }])!;
            Assert.False(immutable.ContainsKey("BuildingProject"));
            Assert.Equal("net10.0-windows", immutable["TargetFramework"]);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ImmutableTargetGuardProperties_FailClosedForDynamicOutputName()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string importPath = Path.Combine(root, "Imported.targets");
            File.WriteAllText(
                importPath,
                "<Project><Target Name='Change'><Message Text='done'><Output TaskParameter='Text' PropertyName='$(AssignedProperty)' /></Message></Target></Project>");
            MethodInfo method = typeof(DotNetPublishPipelineRunner).GetMethod(
                "ReadImmutableTargetGuardProperties",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            var globals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["TargetFramework"] = "net10.0-windows"
            };

            var immutable = (IReadOnlyDictionary<string, string>)method.Invoke(
                null,
                [globals, new[] { importPath }])!;
            Assert.Empty(immutable);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

}
