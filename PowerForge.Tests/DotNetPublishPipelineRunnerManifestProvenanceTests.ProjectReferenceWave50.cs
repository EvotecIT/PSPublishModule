using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ControlledBuildInputs_RejectXslTransformationReparsePointOutsideSelectedInputs()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string externalPath = Path.Combine(externalRoot, "payload.xml");
            string linkPath = Path.Combine(root, "payload-link.xml");
            File.WriteAllText(externalPath, "<payload />");
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
            string transformPath = Path.Combine(root, "transform.xslt");
            File.WriteAllText(projectPath, """
                <Project>
                  <Target Name="Transform">
                    <XslTransformation XmlInputPaths="payload-link.xml"
                                       XslInputPath="transform.xslt"
                                       OutputPaths="output.xml" />
                  </Target>
                </Project>
                """);
            File.WriteAllText(transformPath, """
                <xsl:stylesheet version="1.0" xmlns:xsl="http://www.w3.org/1999/XSL/Transform" />
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, transformPath],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ControlledBuildInputs_AcceptOrdinaryProjectShapedContent()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            string contentPath = Path.Combine(root, "fixture.xml");
            File.WriteAllText(projectPath, "<Project />");
            File.WriteAllText(contentPath, """
                <Project>
                  <Target Name="ContentOnly"><Exec Command="echo content" /></Target>
                </Project>
                """);

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, contentPath],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectImportedProjectShapedContentWithExecutableTask()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            string importedPath = Path.Combine(root, "payload.xml");
            File.WriteAllText(projectPath, "<Project><Import Project=\"payload.xml\" /></Project>");
            File.WriteAllText(importedPath, """
                <Project>
                  <Target Name="Imported" BeforeTargets="Build"><Exec Command="echo imported" /></Target>
                </Project>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, importedPath],
                [projectPath, importedPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ReadSourceProvenance_RejectsPackageTargetWithMissingLiteralXslInput()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string packageRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string buildDirectory = Directory.CreateDirectory(Path.Combine(packageRoot, "build")).FullName;
            string feedDirectory = Directory.CreateDirectory(Path.Combine(packageRoot, "feed")).FullName;
            string packageProject = Path.Combine(packageRoot, "Unsafe.Xsl.csproj");
            File.WriteAllText(packageProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <PackageId>Unsafe.Xsl</PackageId>
                    <Version>1.0.0</Version>
                    <IncludeBuildOutput>false</IncludeBuildOutput>
                  </PropertyGroup>
                  <ItemGroup>
                    <None Include="build/Unsafe.Xsl.targets" Pack="true" PackagePath="build/Unsafe.Xsl.targets" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(buildDirectory, "Unsafe.Xsl.targets"),
                """
                <Project>
                  <Target Name="UnscheduledTransform">
                    <XslTransformation XmlInputPaths="payload.xml"
                                       XslInputPath="transform.xslt"
                                       OutputPaths="output.xml" />
                  </Target>
                </Project>
                """);
            RunDotNet(root, $"pack \"{packageProject}\" -c Release -o \"{feedDirectory}\" --nologo");
            File.WriteAllText(Path.Combine(root, "NuGet.Config"), $"""
                <configuration><packageSources><clear /><add key="local" value="{feedDirectory}" /></packageSources></configuration>
                """);

            (string appProject, string libraryProject, _) = CreateWave40EmbeddedProjectFixture(
                root,
                "<PackageReference Include=\"Unsafe.Xsl\" Version=\"1.0.0\" PrivateAssets=\"all\" />");
            RunDotNet(root, $"build \"{libraryProject}\" -c Release --no-restore --nologo");

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
            DeleteTestRepository(packageRoot);
        }
    }
}
