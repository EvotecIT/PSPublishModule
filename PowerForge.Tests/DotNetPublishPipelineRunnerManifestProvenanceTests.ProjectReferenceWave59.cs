using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Theory]
    [InlineData("destination-link")]
    [InlineData("destination-directory-link/output.bin")]
    public void ControlledBuildInputs_RejectCopyDestinationReparsePoint(string destination)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "payload.bin"), "controlled");
            try
            {
                if (destination.Contains('/', StringComparison.Ordinal))
                {
                    Directory.CreateSymbolicLink(
                        Path.Combine(root, "destination-directory-link"),
                        externalRoot);
                }
                else
                {
                    string externalPath = Path.Combine(externalRoot, "output.bin");
                    File.WriteAllText(externalPath, "external");
                    File.CreateSymbolicLink(Path.Combine(root, destination), externalPath);
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, $"<Project><Target Name=\"Build\"><Copy SourceFiles=\"payload.bin\" DestinationFiles=\"{destination}\" /></Target></Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, Path.Combine(root, "payload.bin")]));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectUnmodeledSdkTask()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, "<Project><Target Name=\"Build\"><CreateAppHost AppHostSourcePath=\"payload.bin\" AppHostDestinationPath=\"output.bin\" /></Target></Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root, [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectCertificateStoreSigningTask()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string signingTarget = Path.Combine(root, "payload.bin");
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(signingTarget, "controlled");
            File.WriteAllText(projectPath, "<Project><Target Name=\"Build\"><SignFile SigningTarget=\"payload.bin\" CertificateThumbprint=\"001122\" /></Target></Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, signingTarget]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData("Csc", "Resources")]
    [InlineData("AL", "EmbedResources")]
    [InlineData("Vbc", "LinkResources")]
    [InlineData("Fsc", "Resources")]
    public void ControlledBuildInputs_RejectCompilerResourceMetadataReparsePoint(
        string taskName,
        string attributeName)
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
            File.WriteAllText(projectPath, $"<Project><Target Name=\"Build\"><{taskName} {attributeName}=\"payload-link,Payload\" /></Target></Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root, [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectCompilerReferenceAliasOperand()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, "<Project><Target Name=\"Build\"><Csc References=\"global=payload.dll\" /></Target></Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root, [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectComputedImportPath()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, """
                <Project>
                  <PropertyGroup Condition="'$(USERPROFILE)' != ''">
                    <ExtraTargets>evil.targets</ExtraTargets>
                  </PropertyGroup>
                  <Import Project="$(ExtraTargets)" />
                </Project>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root, [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectEnvironmentDependentImportCondition()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, """
                <Project>
                  <PropertyGroup Condition="'$(USERPROFILE)' != ''">
                    <EnableOptional>true</EnableOptional>
                  </PropertyGroup>
                  <Import Project="$(MSBuildThisFileDirectory)optional.targets" Condition="'$(EnableOptional)' == 'true'" />
                </Project>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root, [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledGitAttributes_RejectRepositoryLocalInfoAttributes()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string infoDirectory = Directory.CreateDirectory(Path.Combine(root, "info")).FullName;
            File.WriteAllText(Path.Combine(infoDirectory, "attributes"), "*.txt text eol=crlf");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledGitAttributeSources(root));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}
