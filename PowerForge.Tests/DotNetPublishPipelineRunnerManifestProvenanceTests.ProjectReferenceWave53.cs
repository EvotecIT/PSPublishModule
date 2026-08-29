using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ControlledBuildInputs_RejectZipDirectoryReparsePoint()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string externalDirectory = Directory.CreateDirectory(Path.Combine(externalRoot, "payload")).FullName;
            File.WriteAllText(Path.Combine(externalDirectory, "payload.txt"), "external");
            string linkPath = Path.Combine(root, "payload-link");
            try
            {
                Directory.CreateSymbolicLink(linkPath, externalDirectory);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }

            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, """
                <Project>
                  <Target Name="Archive">
                    <ZipDirectory SourceDirectory="payload-link" DestinationFile="payload.zip" />
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
    public void ControlledBuildInputs_AcceptContainedZipDirectory()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string payloadDirectory = Directory.CreateDirectory(Path.Combine(root, "payload")).FullName;
            File.WriteAllText(Path.Combine(payloadDirectory, "payload.txt"), "controlled");
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, """
                <Project>
                  <Target Name="Archive">
                    <ZipDirectory SourceDirectory="payload" DestinationFile="payload.zip" />
                  </Target>
                </Project>
                """);

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData("ApplicationIcon")]
    [InlineData("Win32Icon")]
    [InlineData("CompilerResponseFile")]
    [InlineData("CodeAnalysisRuleSet")]
    [InlineData("KeyOriginatorFile")]
    [InlineData("CscEnvironment")]
    [InlineData("VbcEnvironment")]
    [InlineData("AlEnvironment")]
    public void ControlledBuildInputs_RejectValueProducingSdkTaskProperty(string propertyName)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.csproj");
            File.WriteAllText(projectPath, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <{propertyName}>$([MSBuild]::GetRegistryValue('HKEY_CURRENT_USER\Software\PowerForge', 'Payload', ''))</{propertyName}>
                  </PropertyGroup>
                </Project>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_AcceptUnrelatedValueProducingProperty()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.csproj");
            File.WriteAllText(projectPath, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <DisplayName>$([System.String]::Copy('controlled'))</DisplayName>
                  </PropertyGroup>
                </Project>
                """);

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectLocalAlToolOverride()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(
                Path.Combine(root, "Directory.Build.targets"),
                "<Project TreatAsLocalProperty=\"AlToolPath\" />");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildSafeguards_ClearAlToolOverrides()
    {
        var arguments = new List<string>();

        DotNetPublishPipelineRunner.AppendControlledProofSafeguards(
            arguments,
            "isolated.config",
            "isolated-source",
            "isolated.lock.json");

        Assert.Contains("-p:AlToolPath=", arguments);
        Assert.Contains("-p:AlToolExe=", arguments);
    }

    [Fact]
    public void ReadSourceProvenance_AcceptsGitFilterNameWithSpaces()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            (string appProject, string libraryProject, _) =
                CreateWave40EmbeddedProjectFixture(root, packageReferenceXml: null);
            RunGit(root, "config \"filter.company filter.clean\" cat");
            RunGit(root, "config \"filter.company filter.smudge\" cat");
            RunGit(root, "config \"filter.company filter.required\" false");
            RunDotNet(root, $"build \"{libraryProject}\" -c Release --no-restore --nologo");

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
            Assert.Empty(provenance.DirtyPaths);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}
