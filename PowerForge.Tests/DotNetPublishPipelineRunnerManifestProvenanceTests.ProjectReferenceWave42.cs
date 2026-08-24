using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ControlledBuildInputScanner_RejectsEscapedRootedAndWindowsEnvironmentInputs()
    {
        string rooted = OperatingSystem.IsWindows()
            ? "%43%3A%5Coutside%5Cpayload.dll"
            : "%2Foutside%2Fpayload.dll";

        Assert.True(DotNetPublishPipelineRunner.ContainsRootedBuildValue(rooted, gitRoot: null));
        Assert.True(DotNetPublishPipelineRunner.ContainsUncontrolledEnvironmentReference(
            "$(WINDIR)\\Temp\\payload.dll"));
        Assert.True(DotNetPublishPipelineRunner.ContainsUncontrolledEnvironmentReference(
            "$(ProgramFiles)\\tool\\payload.dll"));
        Assert.True(DotNetPublishPipelineRunner.ContainsEscapingRelativeBuildValue(
            "../../../../outside/payload.dll",
            Path.Combine(Path.GetTempPath(), "controlled", "src", "App"),
            Path.Combine(Path.GetTempPath(), "controlled")));
        Assert.True(DotNetPublishPipelineRunner.ContainsUncontrolledEnvironmentReference(
            "$([System.Environment]::GetEnvironmentVariable('WINDIR'))\\Temp\\payload.dll"));
    }

    [Fact]
    public void ControlledBuildSafeguards_AreAppendedAfterRequestedOverrides()
    {
        var arguments = new List<string>
        {
            "-p:RunAnalyzers=true",
            "-p:RestoreSources=https://example.invalid/feed"
        };

        DotNetPublishPipelineRunner.AppendControlledProofSafeguards(
            arguments,
            "isolated.config",
            "isolated-source",
            "isolated.lock.json");

        Assert.Equal("-p:RunAnalyzers=false", arguments.Last(value =>
            value.StartsWith("-p:RunAnalyzers=", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal("-p:RestoreSources=isolated-source", arguments.Last(value =>
            value.StartsWith("-p:RestoreSources=", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void DotNetToolchainDiscovery_IgnoresAmbientCustomRoot()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string? previous = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        try
        {
            string executable = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            File.WriteAllText(executable, "unattested host");
            Directory.CreateDirectory(Path.Combine(root, "host", "fxr"));
            Directory.CreateDirectory(Path.Combine(root, "shared", "Microsoft.NETCore.App"));
            Directory.CreateDirectory(Path.Combine(root, "sdk"));
            Environment.SetEnvironmentVariable("DOTNET_ROOT", root);

            Assert.True(DotNetPublishPipelineRunner.TryResolveTrustedBuildTool("dotnet", out string resolved));
            Assert.NotEqual(Path.GetFullPath(executable), Path.GetFullPath(resolved));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_ROOT", previous);
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ReadSourceProvenance_RejectsImportedPackageProjectXmlWithRootedInput()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string packageDirectory = Directory.CreateDirectory(Path.Combine(externalRoot, "package")).FullName;
            string buildDirectory = Directory.CreateDirectory(Path.Combine(packageDirectory, "build")).FullName;
            string feedDirectory = Directory.CreateDirectory(Path.Combine(externalRoot, "feed")).FullName;
            string packageProject = Path.Combine(packageDirectory, "Unsafe.Import.csproj");
            File.WriteAllText(packageProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <PackageId>Unsafe.Import</PackageId>
                    <Version>1.0.0</Version>
                    <IncludeBuildOutput>false</IncludeBuildOutput>
                  </PropertyGroup>
                  <ItemGroup>
                    <None Include="build/Unsafe.Import.targets" Pack="true" PackagePath="build/Unsafe.Import.targets" />
                    <None Include="build/payload.xml" Pack="true" PackagePath="build/payload.xml" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(buildDirectory, "Unsafe.Import.targets"),
                "<Project><Import Project=\"payload.xml\" /></Project>");
            string rooted = OperatingSystem.IsWindows() ? "C:\\outside\\payload.dll" : "/outside/payload.dll";
            File.WriteAllText(
                Path.Combine(buildDirectory, "payload.xml"),
                $"<Project><ItemGroup><None Include=\"{rooted}\" /></ItemGroup></Project>");
            RunDotNet(root, $"pack \"{packageProject}\" -c Release -o \"{feedDirectory}\" --nologo");
            File.WriteAllText(Path.Combine(root, "NuGet.Config"), $"""
                <configuration><packageSources><clear /><add key="local" value="{feedDirectory}" /></packageSources></configuration>
                """);

            (string appProject, string libraryProject, _) = CreateWave40EmbeddedProjectFixture(
                root,
                "<PackageReference Include=\"Unsafe.Import\" Version=\"1.0.0\" PrivateAssets=\"all\" />");
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
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ReadSourceProvenance_SeedsOnlyPackagesRequiredByControlledChild()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string externalRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string packageDirectory = Directory.CreateDirectory(Path.Combine(externalRoot, "package")).FullName;
            string buildDirectory = Directory.CreateDirectory(Path.Combine(packageDirectory, "build")).FullName;
            string feedDirectory = Directory.CreateDirectory(Path.Combine(externalRoot, "feed")).FullName;
            string packageProject = Path.Combine(packageDirectory, "Unrelated.Unsafe.csproj");
            File.WriteAllText(packageProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <PackageId>Unrelated.Unsafe</PackageId>
                    <Version>1.0.0</Version>
                    <IncludeBuildOutput>false</IncludeBuildOutput>
                  </PropertyGroup>
                  <ItemGroup>
                    <None Include="build/Unrelated.Unsafe.targets" Pack="true" PackagePath="build/Unrelated.Unsafe.targets" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(buildDirectory, "Unrelated.Unsafe.targets"),
                $"<Project><ItemGroup><None Include=\"{(OperatingSystem.IsWindows() ? "C:\\outside\\payload.dll" : "/outside/payload.dll")}\" /></ItemGroup></Project>");
            RunDotNet(root, $"pack \"{packageProject}\" -c Release -o \"{feedDirectory}\" --nologo");
            File.WriteAllText(Path.Combine(root, "NuGet.Config"), $"""
                <configuration><packageSources><clear /><add key="local" value="{feedDirectory}" /><add key="nuget" value="https://api.nuget.org/v3/index.json" /></packageSources></configuration>
                """);

            (string appProject, string libraryProject, _) = CreateWave40EmbeddedProjectFixture(
                root,
                "<PackageReference Include=\"Newtonsoft.Json\" Version=\"13.0.3\" />");
            string unrelatedDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "Unrelated")).FullName;
            string unrelatedProject = Path.Combine(unrelatedDirectory, "Unrelated.csproj");
            File.WriteAllText(unrelatedProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup><PackageReference Include="Unrelated.Unsafe" Version="1.0.0" PrivateAssets="all" /></ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(unrelatedDirectory, "Unrelated.cs"), "public static class Unrelated { }");
            string appText = File.ReadAllText(appProject).Replace(
                "</ItemGroup>",
                "<ProjectReference Include=\"../Unrelated/Unrelated.csproj\" ReferenceOutputAssembly=\"false\" /></ItemGroup>");
            File.WriteAllText(appProject, appText);
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"add unrelated package closure\"");
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
            DeleteTestRepository(externalRoot);
        }
    }

    [Fact]
    public void ReadSourceProvenance_RejectsDirtyRecordedSubmoduleIgnoredByOuterStatus()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string leafRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(leafRoot, "init");
            RunGit(leafRoot, "config user.name \"PowerForge Tests\"");
            RunGit(leafRoot, "config user.email \"powerforge-tests@example.invalid\"");
            File.WriteAllText(
                Path.Combine(leafRoot, "Leaf.cs"),
                "public static class Leaf { public const int Value = 1; }");
            RunGit(leafRoot, "add .");
            RunGit(leafRoot, "commit -m \"approved leaf\"");

            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            RunGit(root, "config protocol.file.allow always");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup><Compile Include="../Leaf/Leaf.cs" Link="Leaf.cs" /></ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunGit(
                root,
                $"-c protocol.file.allow=always submodule add \"{leafRoot.Replace('\\', '/')}\" src/Leaf");
            File.AppendAllText(Path.Combine(root, ".gitmodules"), "\n\tignore = all\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");

            File.WriteAllText(
                Path.Combine(root, "src", "Leaf", "Leaf.cs"),
                "public static class Leaf { public const int Value = 2; }");
            Assert.Equal(string.Empty, RunGit(root, "status --porcelain").Trim());
            Assert.NotEqual(
                string.Empty,
                RunGit(Path.Combine(root, "src", "Leaf"), "status --porcelain").Trim());

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
        }
        finally
        {
            DeleteTestRepository(root);
            DeleteTestRepository(leafRoot);
        }
    }

    [Fact]
    public void Run_BlocksTamperedProjectOutputBeforePublishConsumesIt()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            (string appProject, string libraryProject, string libraryOutput) =
                CreateWave40EmbeddedProjectFixture(root, packageReferenceXml: null);
            RunDotNet(root, $"build \"{libraryProject}\" -c Release --no-restore --nologo");
            File.AppendAllText(libraryOutput, "tampered before publish");
            string revision = RunGit(root, "rev-parse HEAD").Trim();
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                SourceRevision = revision,
                Configuration = "Release",
                Targets =
                [
                    new DotNetPublishTargetPlan
                    {
                        Name = "App",
                        ProjectPath = appProject
                    }
                ],
                Steps =
                [
                    new DotNetPublishStep
                    {
                        Key = "Publish App",
                        Kind = DotNetPublishStepKind.Publish,
                        TargetName = "App",
                        Framework = "net8.0",
                        Runtime = OperatingSystem.IsWindows() ? "win-x64" : "linux-x64",
                        Style = DotNetPublishStyle.Portable
                    }
                ]
            };

            DotNetPublishResult result = new DotNetPublishPipelineRunner(new NullLogger()).Run(plan, progress: null);

            Assert.False(result.Succeeded);
            Assert.Contains("Release source changed", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(result.Artefacts);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}
