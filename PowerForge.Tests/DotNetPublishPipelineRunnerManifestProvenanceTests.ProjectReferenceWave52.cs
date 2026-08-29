using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Theory]
    [InlineData("AL")]
    [InlineData("Csc")]
    [InlineData("Fsc")]
    [InlineData("Vbc")]
    public void ControlledBuildInputs_RejectCompilerResponseFileReparsePoint(string taskName)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string externalPath = Path.Combine(externalRoot, "payload.rsp");
            string linkPath = Path.Combine(root, "payload-link.rsp");
            File.WriteAllText(externalPath, "-define:EXTERNAL");
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
                  <Target Name="Compile"><{taskName} ResponseFiles="payload-link.rsp" /></Target>
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
    public void ControlledBuildInputs_AcceptContainedCompilerResponseFile()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            string responsePath = Path.Combine(root, "compiler.rsp");
            File.WriteAllText(projectPath, """
                <Project>
                  <Target Name="Compile"><Csc ResponseFiles="compiler.rsp" /></Target>
                </Project>
                """);
            File.WriteAllText(responsePath, "-define:CONTROLLED");

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, responsePath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectRecursiveCompilerResponseFile()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            string responsePath = Path.Combine(root, "compiler.txt");
            File.WriteAllText(projectPath, """
                <Project>
                  <Target Name="Compile"><Csc ResponseFiles="compiler.txt" /></Target>
                </Project>
                """);
            File.WriteAllText(responsePath, "@nested.rsp");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, responsePath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData("DOTNET_STARTUP_HOOKS=tools/hook.dll")]
    [InlineData("CORECLR_ENABLE_PROFILING=1")]
    [InlineData("MSBUILDENABLEALLPROPERTYFUNCTIONS=1")]
    [InlineData("@(CompilerEnvironment)")]
    public void ControlledBuildInputs_RejectTaskRuntimeEnvironmentOverride(string environmentVariables)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "App.proj"), $"""
                <Project>
                  <Target Name="Compile" BeforeTargets="Build">
                    <Csc EnvironmentVariables="{environmentVariables}" />
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
    public void ControlledBuildInputs_AcceptTaskEnvironmentWithoutInjection()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "App.proj"), """
                <Project>
                  <Target Name="Compile">
                    <Csc EnvironmentVariables="PRODUCT_MODE=controlled" />
                  </Target>
                </Project>
                """);

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ReadSourceProvenance_RejectsPackageCompilerResponseFileContent()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string packageRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string buildDirectory = Directory.CreateDirectory(Path.Combine(packageRoot, "build")).FullName;
            string feedDirectory = Directory.CreateDirectory(Path.Combine(packageRoot, "feed")).FullName;
            string packageProject = Path.Combine(packageRoot, "Unsafe.Response.csproj");
            File.WriteAllText(packageProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <PackageId>Unsafe.CompilerResponse</PackageId>
                    <Version>1.0.0</Version>
                    <IncludeBuildOutput>false</IncludeBuildOutput>
                  </PropertyGroup>
                  <ItemGroup>
                    <None Include="build/Unsafe.CompilerResponse.targets" Pack="true" PackagePath="build/Unsafe.CompilerResponse.targets" />
                    <None Include="build/compiler.txt" Pack="true" PackagePath="build/compiler.txt" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(buildDirectory, "Unsafe.CompilerResponse.targets"),
                "<Project><Target Name=\"CompileFixture\"><Csc ResponseFiles=\"$(MSBuildThisFileDirectory)compiler.txt\" /></Target></Project>");
            File.WriteAllText(Path.Combine(buildDirectory, "compiler.txt"), "@nested.rsp");
            RunDotNet(root, $"pack \"{packageProject}\" -c Release -o \"{feedDirectory}\" --nologo");
            File.WriteAllText(Path.Combine(root, "NuGet.Config"), $"""
                <configuration><packageSources><clear /><add key="local" value="{feedDirectory}" /></packageSources></configuration>
                """);

            (string appProject, string libraryProject, _) = CreateWave40EmbeddedProjectFixture(
                root,
                "<PackageReference Include=\"Unsafe.CompilerResponse\" Version=\"1.0.0\" PrivateAssets=\"all\" />");
            RunDotNet(root, $"build \"{libraryProject}\" -c Release --no-restore --nologo");

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
            Assert.Contains(provenance.DirtyReasons, reason =>
                reason.Contains("untrusted evaluated build input", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(packageRoot);
        }
    }
}
