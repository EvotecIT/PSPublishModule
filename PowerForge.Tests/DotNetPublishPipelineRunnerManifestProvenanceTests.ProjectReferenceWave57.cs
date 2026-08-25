using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Theory]
    [InlineData("Exists('payload-link')", "")]
    [InlineData("$(ProbeCondition)", "<PropertyGroup><ProbeCondition>Exists('payload-link')</ProbeCondition></PropertyGroup>")]
    public void ControlledBuildInputs_RejectExistsConditionReparsePoint(
        string condition,
        string propertyGroup)
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
            File.WriteAllText(projectPath, $"<Project>{propertyGroup}<Target Name=\"Build\" Condition=\"{condition}\"><WriteLinesToFile File=\"output.txt\" Lines=\"present\" /></Target></Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root, [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Theory]
    [InlineData("missing.flag")]
    [InlineData("contained.flag")]
    public void ControlledBuildInputs_AcceptControlledExistsCondition(string path)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, $"<Project><Target Name=\"Build\" Condition=\"Exists('{path}')\"><WriteLinesToFile File=\"output.txt\" Lines=\"present\" /></Target></Project>");
            if (path.Equals("contained.flag", StringComparison.Ordinal))
                File.WriteAllText(Path.Combine(root, path), "controlled");

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root, [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData("CscEnvironment")]
    [InlineData("VbcEnvironment")]
    [InlineData("AlEnvironment")]
    public void ControlledBuildInputs_RejectSdkCompilerRuntimeInjection(string propertyName)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, $"<Project><PropertyGroup><{propertyName}>DOTNET_STARTUP_HOOKS=tools/hook.dll</{propertyName}></PropertyGroup></Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root, [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData("AspNetCompiler")]
    [InlineData("LC")]
    [InlineData("RegisterAssembly")]
    [InlineData("SGen")]
    [InlineData("UnregisterAssembly")]
    public void ControlledBuildInputs_RejectCodeLoadingBuildTask(string taskName)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, $"<Project><Target Name=\"Build\"><{taskName} /></Target></Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root, [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ReadSourceProvenance_RejectsDependentUponReparsePoint()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string externalPath = Path.Combine(externalRoot, "External.cs");
            string linkPath = Path.Combine(root, "payload-link.cs");
            File.WriteAllText(externalPath, "namespace External.Payload; internal sealed class ResourceOwner { }");
            try
            {
                File.CreateSymbolicLink(linkPath, externalPath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }
            string projectPath = Path.Combine(root, "App.csproj");
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <Compile Remove="payload-link.cs" />
                    <EmbeddedResource Update="Resources.resx">
                      <DependentUpon>payload-link.cs</DependentUpon>
                    </EmbeddedResource>
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(root, "Program.cs"), "internal static class Program { }");
            File.WriteAllText(Path.Combine(root, "Resources.resx"), "<root />");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{projectPath}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            RunDotNet(root, $"build \"{projectPath}\" -c Release --no-restore --nologo");

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [projectPath],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ReadSourceProvenance_RejectsProjectReferenceOutputBelowSharedGeneratedRoot()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "Library")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string libraryProject = Path.Combine(libraryDirectory, "Library.csproj");
            const string outputProperties = "<BaseOutputPath>../../artifacts/bin/</BaseOutputPath><OutputPath>$(BaseOutputPath)$(MSBuildProjectName)/$(Configuration)/</OutputPath>";
            File.WriteAllText(appProject, $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework>{outputProperties}</PropertyGroup><ItemGroup><ProjectReference Include=\"../Library/Library.csproj\" /></ItemGroup></Project>");
            File.WriteAllText(libraryProject, $"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework>{outputProperties}</PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { private static int Value => Library.Value; }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { public const int Value = 1; }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "artifacts/\nbin/\nobj/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            RunDotNet(root, $"build \"{libraryProject}\" -c Release --no-restore --nologo");
            string libraryOutput = Path.Combine(root, "artifacts", "bin", "Library", "Release", "net8.0", "Library.dll");
            File.AppendAllText(libraryOutput, "unapproved overlay");

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
            Assert.Contains(provenance.DirtyReasons, reason =>
                reason.Contains("Library.dll", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}
