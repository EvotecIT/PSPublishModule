using System.IO.Compression;
using System.Reflection;
using NuGet.Packaging;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Theory]
    [InlineData("Compile")]
    [InlineData("EmbeddedResource")]
    [InlineData("Analyzer")]
    [InlineData("Reference")]
    public void ControlledBuildInputs_RejectTargetTimeSdkBuildItemReparsePoint(string itemName)
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
            File.WriteAllText(projectPath, $"<Project><Target Name=\"Build\"><ItemGroup><{itemName} Include=\"payload-link\" /></ItemGroup></Target></Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectGenerateResourceWithoutSourceRelativeFileReferences()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string resourcesDirectory = Directory.CreateDirectory(Path.Combine(root, "Resources")).FullName;
            string resourcePath = Path.Combine(resourcesDirectory, "data.resx");
            string harmlessPath = Path.Combine(resourcesDirectory, "payload-link");
            File.WriteAllText(harmlessPath, "controlled");
            File.WriteAllText(
                resourcePath,
                "<root><data name=\"Payload\" type=\"System.Resources.ResXFileRef, System.Windows.Forms\"><value>payload-link;System.Byte[]</value></data></root>");
            string externalPath = Path.Combine(externalRoot, "payload.bin");
            File.WriteAllText(externalPath, "external");
            try
            {
                File.CreateSymbolicLink(Path.Combine(root, "payload-link"), externalPath);
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
                    <GenerateResource Sources="Resources/data.resx"
                                      UseSourcePath="false"
                                      OutputResources="obj/data.resources" />
                  </Target>
                </Project>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, resourcePath, harmlessPath],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ControlledBuildInputs_AcceptGenerateResourceWithoutResxFileReference()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string sourcePath = Path.Combine(root, "Resources.txt");
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(sourcePath, "Name=Value");
            File.WriteAllText(projectPath, "<Project><Target Name=\"Build\"><GenerateResource Sources=\"Resources.txt\" UseSourcePath=\"false\" OutputResources=\"obj/Resources.resources\" /></Target></Project>");

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, sourcePath],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData("<Target Name=\"Build\"><Csc KeyContainer=\"ReleaseKey\" /></Target>")]
    [InlineData("<PropertyGroup><KeyContainerName>ReleaseKey</KeyContainerName></PropertyGroup>")]
    public void ControlledBuildInputs_RejectAmbientCompilerKeyContainer(string projectBody)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(projectPath, $"<Project>{projectBody}</Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectCompilerResponseKeyContainer()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string responsePath = Path.Combine(root, "compiler.rsp");
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(responsePath, "/keycontainer:ReleaseKey");
            File.WriteAllText(
                projectPath,
                "<Project><Target Name=\"Build\"><Csc ResponseFiles=\"compiler.rsp\" /></Target></Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, responsePath],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildSafeguards_ClearSdkKeyContainer()
    {
        var arguments = new List<string>();

        DotNetPublishPipelineRunner.AppendControlledProofSafeguards(
            arguments,
            "isolated.config",
            "isolated-source",
            "isolated.lock.json");

        Assert.Contains("-p:KeyContainerName=", arguments);
    }

    [Fact]
    public void ReadSourceProvenance_AcceptsControlledPackageDirectoryTaskInput()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadPackageTargetProvenance(
            "Package.DirectoryInput",
            "<Project><Target Name=\"ArchivePayload\" BeforeTargets=\"CoreCompile\"><ZipDirectory SourceDirectory=\"$(MSBuildThisFileDirectory)payload\" DestinationFile=\"$(IntermediateOutputPath)payload.zip\" Overwrite=\"true\" /></Target></Project>",
            new Dictionary<string, string> { ["build/payload/payload.txt"] = "controlled" });

        Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
    }

    [Fact]
    public void VerifiedPackageArchive_UsesImmutableValidatedSnapshot()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string packagePath = Path.Combine(root, "Package.Snapshot.1.0.0.nupkg");
            string copiedPath = Path.Combine(root, "copied.nupkg");
            WriteTestPackage(packagePath, "approved");
            byte[] approvedBytes = File.ReadAllBytes(packagePath);
            string contentHash;
            using (FileStream packageStream = File.OpenRead(packagePath))
            using (var packageReader = new PackageArchiveReader(packageStream, leaveStreamOpen: false))
                contentHash = packageReader.GetContentHash(CancellationToken.None);

            Type archiveType = typeof(DotNetPublishPipelineRunner).GetNestedType(
                "VerifiedPackageArchive",
                BindingFlags.NonPublic)!;
            MethodInfo tryOpen = archiveType.GetMethod(
                "TryOpen",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            MethodInfo copyTo = archiveType.GetMethod(
                "CopyTo",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            using IDisposable archive = (IDisposable)tryOpen.Invoke(
                null,
                [packagePath, contentHash])!;

            WriteTestPackage(packagePath, "mutated");
            copyTo.Invoke(archive, [copiedPath]);

            Assert.Equal(approvedBytes, File.ReadAllBytes(copiedPath));
            Assert.NotEqual(approvedBytes, File.ReadAllBytes(packagePath));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    private static void WriteTestPackage(string path, string payload)
    {
        using FileStream stream = File.Open(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: false);
        ZipArchiveEntry nuspec = archive.CreateEntry("Package.Snapshot.nuspec");
        using (var writer = new StreamWriter(nuspec.Open()))
        {
            writer.Write("<package><metadata><id>Package.Snapshot</id><version>1.0.0</version><authors>PowerForge</authors><description>Snapshot fixture</description></metadata></package>");
        }
        ZipArchiveEntry content = archive.CreateEntry("content/payload.txt");
        using var contentWriter = new StreamWriter(content.Open());
        contentWriter.Write(payload);
    }
}
