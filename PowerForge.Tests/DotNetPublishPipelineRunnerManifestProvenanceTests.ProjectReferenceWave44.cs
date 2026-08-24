using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ControlledBuildInputScanner_RejectsFileSystemPropertyFunctions()
    {
        Assert.True(DotNetPublishPipelineRunner.ContainsUncontrolledFileSystemPropertyFunction(
            "$([System.IO.Path]::GetPathRoot('$(MSBuildProjectDirectory)'))tmp/payload.dll"));
        Assert.True(DotNetPublishPipelineRunner.ContainsUncontrolledFileSystemPropertyFunction(
            "$([MSBuild]::NormalizePath('..', 'payload.dll'))"));
        Assert.False(DotNetPublishPipelineRunner.ContainsUncontrolledFileSystemPropertyFunction(
            "artifacts/payload.dll"));
    }

    [Fact]
    public void ReadSourceProvenance_RejectsPackageResponseFileWithRootedInput()
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
                    <PackageId>Unsafe.Response</PackageId>
                    <Version>1.0.0</Version>
                    <IncludeBuildOutput>false</IncludeBuildOutput>
                  </PropertyGroup>
                  <ItemGroup>
                    <None Include="build/Unsafe.Response.targets" Pack="true" PackagePath="build/Unsafe.Response.targets" />
                    <None Include="build/payload.rsp" Pack="true" PackagePath="build/payload.rsp" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(buildDirectory, "Unsafe.Response.targets"),
                "<Project><Target Name=\"ReadPayload\" BeforeTargets=\"Build\"><ReadLinesFromFile File=\"$(MSBuildThisFileDirectory)payload.rsp\"><Output TaskParameter=\"Lines\" ItemName=\"Payload\" /></ReadLinesFromFile></Target></Project>");
            File.WriteAllText(
                Path.Combine(buildDirectory, "payload.rsp"),
                OperatingSystem.IsWindows() ? "C:\\outside\\payload.dll" : "/outside/payload.dll");
            RunDotNet(root, $"pack \"{packageProject}\" -c Release -o \"{feedDirectory}\" --nologo");
            File.WriteAllText(Path.Combine(root, "NuGet.Config"), $"""
                <configuration><packageSources><clear /><add key="local" value="{feedDirectory}" /></packageSources></configuration>
                """);

            (string appProject, string libraryProject, _) = CreateWave40EmbeddedProjectFixture(
                root,
                "<PackageReference Include=\"Unsafe.Response\" Version=\"1.0.0\" PrivateAssets=\"all\" />");
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

    [Fact]
    public void ReadSourceProvenance_RecoversDynamicItemAfterBraceContainingOutput()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <Target Name="CreateReference" BeforeTargets="ResolveReferences">
                    <Warning Text="{ambiguous auxiliary output}" />
                    <CreateItem Include="../Library/Library.csproj">
                      <Output TaskParameter="Include" ItemName="ProjectReference" />
                    </CreateItem>
                  </Target>
                </Project>
                """,
            libraryProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup><Compile Include="../../inputs/Selected.cs" /></ItemGroup>
                </Project>
                """,
            repositoryFiles: SelectedInput,
            mutatedPath: "inputs/Selected.cs",
            buildFramework: "net8.0");

        AssertSelectedInputIsDirty(provenance);
    }

    [Fact]
    public void ReadSourceProvenance_FailsClosedWhenTaskOutputMutatesTargetSchedule()
    {
        DotNetPublishPipelineRunner.SourceProvenance provenance = ReadProjectReferencePropertyRecoveryFixture(
            appProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <Target Name="ScheduleReference" BeforeTargets="ResolveReferences">
                    <CreateProperty Value="CreateReference;$(ResolveReferencesDependsOn)">
                      <Output TaskParameter="Value" PropertyName="ResolveReferencesDependsOn" />
                    </CreateProperty>
                  </Target>
                  <Target Name="CreateReference">
                    <ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup>
                  </Target>
                </Project>
                """,
            libraryProjectXml: """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup><Compile Include="../../inputs/Selected.cs" /></ItemGroup>
                </Project>
                """,
            repositoryFiles: SelectedInput,
            mutatedPath: "inputs/Selected.cs",
            buildFramework: "net8.0");

        Assert.True(provenance.Dirty);
        Assert.Contains(provenance.DirtyReasons, reason =>
            reason.Contains("MSBuild input evaluation failed", StringComparison.Ordinal));
    }

    [Fact]
    public void ReadSourceProvenance_RejectsNestedSourceHiddenByCleanFilter()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string leafRoot = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(leafRoot, "init");
            RunGit(leafRoot, "config user.name \"PowerForge Tests\"");
            RunGit(leafRoot, "config user.email \"powerforge-tests@example.invalid\"");
            const string approved = "public static class Leaf { public const int Value = 1; }";
            string leafSource = Path.Combine(leafRoot, "Leaf.cs");
            File.WriteAllText(leafSource, approved);
            File.WriteAllText(Path.Combine(leafRoot, ".gitattributes"), "Leaf.cs filter=hide\n");
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
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n.cache/\n");
            RunGit(root, $"-c protocol.file.allow=always submodule add \"{leafRoot.Replace('\\', '/')}\" src/Leaf");
            File.AppendAllText(Path.Combine(root, ".gitmodules"), "\n\tignore = all\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");

            string cacheDirectory = Directory.CreateDirectory(Path.Combine(root, ".cache")).FullName;
            string approvedPath = Path.Combine(cacheDirectory, "approved.cs");
            File.WriteAllText(approvedPath, approved);
            string nestedRoot = Path.Combine(root, "src", "Leaf");
            RunGit(nestedRoot, $"config filter.hide.clean \"cat '{approvedPath.Replace('\\', '/')}'\"");
            File.WriteAllText(
                Path.Combine(nestedRoot, "Leaf.cs"),
                "public static class Leaf { public const int Value = 2; }");
            Assert.Equal(string.Empty, RunGit(nestedRoot, "status --porcelain").Trim());

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
}
