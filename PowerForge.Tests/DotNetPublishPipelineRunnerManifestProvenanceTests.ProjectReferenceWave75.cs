using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ControlledBuildInputs_RejectAlBugReportOutputReparsePoint()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string outputLink = Path.Combine(root, "output-link");
            try
            {
                Directory.CreateSymbolicLink(outputLink, externalRoot);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                return;
            }

            string responsePath = Path.Combine(root, "linker.rsp");
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(responsePath, "/bugreport:output-link/report.txt");
            File.WriteAllText(
                projectPath,
                "<Project><Target Name=\"Build\"><AL ResponseFiles=\"linker.rsp\" OutputAssembly=\"output.dll\" /></Target></Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
                root,
                [projectPath, responsePath],
                [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ControlledBuildInputs_AcceptContainedAlBugReportOutput()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string responsePath = Path.Combine(root, "linker.rsp");
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(responsePath, "/bugreport:obj/report.txt");
            File.WriteAllText(
                projectPath,
                "<Project><Target Name=\"Build\"><AL ResponseFiles=\"linker.rsp\" OutputAssembly=\"output.dll\" /></Target></Project>");

            Assert.True(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(
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
    public void ControlledBuildInputs_RejectAlBugReportOutputTraversal()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string responsePath = Path.Combine(root, "linker.rsp");
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(responsePath, "/bugreport:../../outside.txt");
            File.WriteAllText(
                projectPath,
                "<Project><Target Name=\"Build\"><AL ResponseFiles=\"linker.rsp\" OutputAssembly=\"output.dll\" /></Target></Project>");

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
    public void ControlledBuildInputs_RejectDashPrefixedAlBugReportRootedOutput()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string responsePath = Path.Combine(root, "linker.rsp");
            string projectPath = Path.Combine(root, "App.proj");
            string rootedOutput = Path.Combine(Path.GetPathRoot(root)!, "powerforge-outside.txt");
            File.WriteAllText(responsePath, $"-bugreport:\"{rootedOutput}\"");
            File.WriteAllText(
                projectPath,
                "<Project><Target Name=\"Build\"><AL ResponseFiles=\"linker.rsp\" OutputAssembly=\"output.dll\" /></Target></Project>");

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
    public void ReadSourceProvenance_AcceptsDormantPackageTargetOutsideEvaluatedImportClosure()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadPackageTargetProvenance(
            "Package.DormantTarget",
            "<Project />",
            new Dictionary<string, string>
            {
                ["build/net472/Package.DormantTarget.targets"] =
                    "<Project><PropertyGroup><DormantToolPath>C:\\external\\tool.exe</DormantToolPath></PropertyGroup></Project>"
            });

        Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
        Assert.Empty(provenance.DirtyPaths);
    }

    [Fact]
    public void ReadSourceProvenance_PreservesCollidingLockedPackageArchiveNames()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string packageRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string firstFeed = Directory.CreateDirectory(Path.Combine(packageRoot, "feed-a")).FullName;
            string secondFeed = Directory.CreateDirectory(Path.Combine(packageRoot, "feed-b")).FullName;
            CreatePackage("A.1", "2.3.4", firstFeed);
            CreatePackage("A", "1.2.3.4", secondFeed);
            Assert.Equal(
                Path.GetFileName(Directory.GetFiles(firstFeed, "*.nupkg").Single()),
                Path.GetFileName(Directory.GetFiles(secondFeed, "*.nupkg").Single()),
                StringComparer.OrdinalIgnoreCase);

            File.WriteAllText(Path.Combine(root, "NuGet.Config"), $"""
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="a" value="{firstFeed}" />
                    <add key="b" value="{secondFeed}" />
                  </packageSources>
                </configuration>
                """);

            (string appProject, string libraryProject, _) = CreateWave40EmbeddedProjectFixture(
                root,
                """
                <PackageReference Include="A.1" Version="2.3.4" PrivateAssets="all" />
                <PackageReference Include="A" Version="1.2.3.4" PrivateAssets="all" />
                """);
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
            DeleteTestRepository(packageRoot);
        }

        void CreatePackage(string packageId, string version, string feed)
        {
            string packageDirectory = Directory.CreateDirectory(Path.Combine(
                packageRoot,
                packageId.Replace('.', '-'),
                version.Replace('.', '-'))).FullName;
            string packageProject = Path.Combine(packageDirectory, packageId + ".csproj");
            File.WriteAllText(Path.Combine(packageDirectory, "content.txt"), packageId);
            File.WriteAllText(packageProject, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <PackageId>{packageId}</PackageId>
                    <Version>{version}</Version>
                    <IncludeBuildOutput>false</IncludeBuildOutput>
                  </PropertyGroup>
                  <ItemGroup>
                    <None Include="content.txt" Pack="true" PackagePath="content/content.txt" />
                  </ItemGroup>
                </Project>
                """);
            RunDotNet(root, $"pack \"{packageProject}\" -c Release -o \"{feed}\" --nologo");
        }
    }
}
