using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ControlledBuildInputs_RejectTaskOutputPropertyFeedingTaskInput()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectDirectory = Directory.CreateDirectory(
                Path.Combine(root, "src", "a", "b", "c", "d")).FullName;
            File.WriteAllText(Path.Combine(projectDirectory, "App.proj"), """
                <Project>
                  <Target Name="Build">
                    <CombinePath BasePath="$(MSBuildProjectDirectory)" Paths="../..">
                      <Output TaskParameter="CombinedPaths" PropertyName="FirstPath" />
                    </CombinePath>
                    <CombinePath BasePath="$(FirstPath)" Paths="../..">
                      <Output TaskParameter="CombinedPaths" PropertyName="SecondPath" />
                    </CombinePath>
                    <CombinePath BasePath="$(SecondPath)" Paths="../..">
                      <Output TaskParameter="CombinedPaths" PropertyName="ExternalPath" />
                    </CombinePath>
                    <Copy SourceFiles="$(ExternalPath)/payload.bin"
                          DestinationFiles="$(MSBuildProjectDirectory)/payload.bin" />
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
    public void ControlledBuildInputs_RejectTaskOutputItemFeedingTaskInput()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectDirectory = Directory.CreateDirectory(
                Path.Combine(root, "src", "a", "b", "c", "d")).FullName;
            File.WriteAllText(Path.Combine(projectDirectory, "App.proj"), """
                <Project>
                  <Target Name="Build">
                    <CombinePath BasePath="$(MSBuildProjectDirectory)" Paths="../..">
                      <Output TaskParameter="CombinedPaths" ItemName="FirstPath" />
                    </CombinePath>
                    <CombinePath BasePath="@(FirstPath)" Paths="../..">
                      <Output TaskParameter="CombinedPaths" ItemName="SecondPath" />
                    </CombinePath>
                    <CombinePath BasePath="@(SecondPath)" Paths="../..">
                      <Output TaskParameter="CombinedPaths" ItemName="ExternalPath" />
                    </CombinePath>
                    <Copy SourceFiles="@(ExternalPath)"
                          DestinationFiles="$(MSBuildProjectDirectory)/payload.bin" />
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

    [WindowsFact]
    public void ReadSourceProvenance_UsesAttestedGitForIgnoredInputChecks()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string shimDirectory = Directory.CreateTempSubdirectory().FullName;
        string? originalPath = Environment.GetEnvironmentVariable("PATH");
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string projectPath = Path.Combine(root, "App.csproj");
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
                  </PropertyGroup>
                  <ItemGroup>
                    <Compile Include="ignored/Untracked.cs" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(root, ".gitignore"), "ignored/\nbin/\nobj/\n");
            Directory.CreateDirectory(Path.Combine(root, "ignored"));
            File.WriteAllText(
                Path.Combine(root, "ignored", "Untracked.cs"),
                "public static class Untracked { }");
            RunDotNet(root, $"restore \"{projectPath}\" --use-lock-file --nologo");
            RunGit(root, "add App.csproj .gitignore packages.lock.json");
            RunGit(root, "commit -m \"approved source\"");

            string systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
            File.Copy(
                Path.Combine(systemDirectory, "where.exe"),
                Path.Combine(shimDirectory, "git.exe"));
            Environment.SetEnvironmentVariable(
                "PATH",
                shimDirectory + Path.PathSeparator + originalPath);

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [projectPath],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
            Assert.Contains(
                provenance.DirtyReasons,
                reason => reason.Contains("untrusted evaluated build input(s)", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath);
            DeleteTestRepository(root);
            DeleteTestRepository(shimDirectory);
        }
    }
}
