using System.Reflection.PortableExecutable;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ControlledBuildEnvironment_PinsToolchainAndPackageCache()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string controlledRoot = Directory.CreateDirectory(Path.Combine(root, "controlled")).FullName;
        string shimRoot = Directory.CreateDirectory(Path.Combine(root, "shim")).FullName;
        try
        {
            Assert.True(DotNetPublishPipelineRunner.TryCreateControlledBuildEnvironment(
                new Dictionary<string, string?>
                {
                    ["PATH"] = shimRoot,
                    ["DOTNET_ROOT"] = shimRoot
                },
                root,
                controlledRoot,
                out IReadOnlyDictionary<string, string?> environment));
            Assert.True(DotNetPublishPipelineRunner.TryResolveTrustedBuildTool(
                "dotnet",
                out string dotNetPath));

            string expectedDotNetRoot = Path.GetDirectoryName(dotNetPath)!;
            Assert.Equal(expectedDotNetRoot, environment["PATH"]);
            Assert.Equal(expectedDotNetRoot, environment["DOTNET_ROOT"]);
            Assert.False(string.Equals(shimRoot, environment["PATH"], StringComparison.OrdinalIgnoreCase));
            Assert.StartsWith(
                Path.GetDirectoryName(controlledRoot)!,
                environment["NUGET_PACKAGES"]!,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void BuildInputEvaluation_PinsDotNetDespiteConfiguredPathShim()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string shimRoot = Directory.CreateDirectory(Path.Combine(root, "shim")).FullName;
        try
        {
            string shimName = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
            File.WriteAllText(Path.Combine(shimRoot, shimName), "not a trusted toolchain");

            (int exitCode, string stdout, _, bool timedOut) =
                DotNetPublishPipelineRunner.RunBuildInputEvaluationProcess(
                    "dotnet",
                    root,
                    ["--version"],
                    new Dictionary<string, string?> { ["PATH"] = shimRoot },
                    TimeSpan.FromMinutes(1));

            Assert.Equal(0, exitCode);
            Assert.False(timedOut);
            Assert.Matches(@"^\d+\.\d+\.\d+", stdout.Trim());
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void BuildInputEvaluation_IgnoresConfiguredGitRepositoryOverrides()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string decoy = Directory.CreateDirectory(Path.Combine(root, "decoy")).FullName;
        string repository = Directory.CreateDirectory(Path.Combine(root, "repository")).FullName;
        try
        {
            RunGit(decoy, "init");
            RunGit(repository, "init");

            (int exitCode, string stdout, _, bool timedOut) =
                DotNetPublishPipelineRunner.RunBuildInputEvaluationProcess(
                    "git",
                    repository,
                    ["rev-parse", "--show-toplevel"],
                    new Dictionary<string, string?>
                    {
                        ["GIT_DIR"] = Path.Combine(decoy, ".git"),
                        ["GIT_WORK_TREE"] = decoy,
                        ["GIT_INDEX_FILE"] = Path.Combine(decoy, ".git", "index")
                    },
                    TimeSpan.FromMinutes(1));

            Assert.Equal(0, exitCode);
            Assert.False(timedOut);
            Assert.Equal(
                Path.GetFullPath(repository).TrimEnd(Path.DirectorySeparatorChar),
                Path.GetFullPath(stdout.Trim()).TrimEnd(Path.DirectorySeparatorChar),
                OperatingSystem.IsWindows()
                    ? StringComparer.OrdinalIgnoreCase
                    : StringComparer.Ordinal);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ReadSourceProvenance_RejectsActiveGitReplacementRefs()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string project = Path.Combine(root, "App.csproj");
            string source = Path.Combine(root, "Program.cs");
            File.WriteAllText(
                project,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            File.WriteAllText(source, "internal static class Program { internal const int Value = 1; }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{project}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            string approvedRevision = RunGit(root, "rev-parse HEAD").Trim();

            File.WriteAllText(source, "internal static class Program { internal const int Value = 2; }");
            RunGit(root, "add Program.cs");
            RunGit(root, "commit -m \"replacement source\"");
            string replacementRevision = RunGit(root, "rev-parse HEAD").Trim();
            RunGit(root, $"checkout --detach {approvedRevision}");
            RunGit(root, $"replace {approvedRevision} {replacementRevision}");

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [project],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
            Assert.Contains(
                provenance.DirtyReasons,
                reason => reason.Contains("replacement refs", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ReadSourceProvenance_RejectsGeneratedOutputWithModifiedCodeViewPath()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            (string appProject, string libraryProject, string libraryOutput) =
                CreateWave40EmbeddedProjectFixture(root, packageReferenceXml: null);
            RunDotNet(root, $"build \"{libraryProject}\" -c Release --no-restore --nologo");

            byte[] image = File.ReadAllBytes(libraryOutput);
            DebugDirectoryEntry codeView;
            using (var stream = File.OpenRead(libraryOutput))
            using (var reader = new PEReader(stream))
            {
                codeView = reader.ReadDebugDirectory().Single(entry =>
                    entry.Type == DebugDirectoryEntryType.CodeView);
            }
            Assert.True(codeView.DataSize > 24);
            image[codeView.DataPointer + 24] ^= 0x01;
            File.WriteAllBytes(libraryOutput, image);

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
            Assert.Contains(
                provenance.DirtyReasons,
                reason => reason.Contains(
                    "untrusted evaluated build input",
                    StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ReadSourceProvenance_RebuildsOrdinaryLockedPackageOffline()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            (string appProject, string libraryProject, _) = CreateWave40EmbeddedProjectFixture(
                root,
                "<PackageReference Include=\"Newtonsoft.Json\" Version=\"13.0.3\" />");
            File.WriteAllText(
                Path.Combine(Path.GetDirectoryName(libraryProject)!, "Library.cs"),
                "public static class Library { public static string Value => Newtonsoft.Json.JsonConvert.SerializeObject(1); }");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add src/*/packages.lock.json src/Library/Library.cs");
            RunGit(root, "commit -m \"lock ordinary package\"");
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadSourceProvenance_RejectsLockedPackageWithUncontrolledBuildInput(bool rootedToolPath)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string packageDirectory = Directory.CreateDirectory(Path.Combine(root, "package")).FullName;
            string buildDirectory = Directory.CreateDirectory(Path.Combine(packageDirectory, "build")).FullName;
            string feedDirectory = Directory.CreateDirectory(Path.Combine(root, "feed")).FullName;
            string packageProject = Path.Combine(packageDirectory, "Unsafe.Build.csproj");
            File.WriteAllText(packageProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <PackageId>Unsafe.Build</PackageId>
                    <Version>1.0.0</Version>
                    <IncludeBuildOutput>false</IncludeBuildOutput>
                  </PropertyGroup>
                  <ItemGroup>
                    <None Include="build/Unsafe.Build.targets"
                          Pack="true"
                          PackagePath="build/Unsafe.Build.targets" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(buildDirectory, "Unsafe.Build.targets"),
                rootedToolPath
                    ? $"<Project><PropertyGroup><CscToolPath>{(OperatingSystem.IsWindows() ? "C:\\untrusted" : "/tmp/untrusted")}</CscToolPath></PropertyGroup></Project>"
                    : "<Project><UsingTask TaskName=\"Unsafe.NetworkTask\" AssemblyFile=\"$(MSBuildThisFileDirectory)Unsafe.dll\" /></Project>");
            RunDotNet(root, $"pack \"{packageProject}\" -c Release -o \"{feedDirectory}\" --nologo");
            File.WriteAllText(Path.Combine(root, "NuGet.Config"), $"""
                <configuration>
                  <packageSources>
                    <clear />
                    <add key="local" value="{feedDirectory}" />
                  </packageSources>
                </configuration>
                """);

            (string appProject, string libraryProject, _) = CreateWave40EmbeddedProjectFixture(
                root,
                "<PackageReference Include=\"Unsafe.Build\" Version=\"1.0.0\" PrivateAssets=\"all\" />");
            // Keep the fixture buildable on every runner while leaving the package archive's
            // unsafe compiler override intact for provenance inspection.
            RunDotNet(root, $"build \"{libraryProject}\" -c Release --no-restore --nologo -p:CscToolPath=");

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
            Assert.Contains(
                provenance.DirtyReasons,
                reason => reason.Contains(
                    "untrusted evaluated build input",
                    StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    private static (string AppProject, string LibraryProject, string LibraryOutput)
        CreateWave40EmbeddedProjectFixture(string root, string? packageReferenceXml)
    {
        RunGit(root, "init");
        RunGit(root, "config user.name \"PowerForge Tests\"");
        RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
        string appDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;
        string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "Library")).FullName;
        string appProject = Path.Combine(appDirectory, "App.csproj");
        string libraryProject = Path.Combine(libraryDirectory, "Library.csproj");
        File.WriteAllText(appProject, """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
              <ItemGroup>
                <ProjectReference Include="../Library/Library.csproj"
                                  ReferenceOutputAssembly="false"
                                  OutputItemType="EmbeddedResource" />
              </ItemGroup>
            </Project>
            """);
        File.WriteAllText(libraryProject, $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
              <ItemGroup>{packageReferenceXml}</ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { }");
        File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
        File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
        RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
        RunGit(root, "add .");
        RunGit(root, "commit -m \"approved source and dependency locks\"");
        return (
            appProject,
            libraryProject,
            Path.Combine(libraryDirectory, "bin", "Release", "net8.0", "Library.dll"));
    }
}
