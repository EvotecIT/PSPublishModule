using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Theory]
    [InlineData("ResolveAssemblyReference")]
    [InlineData("ResolveComReference")]
    [InlineData("ResolveNativeReference")]
    [InlineData("ResolveSDKReference")]
    [InlineData("GetReferenceAssemblyPaths")]
    [InlineData("GetFrameworkPath")]
    [InlineData("GetFrameworkSdkPath")]
    [InlineData("GetInstalledSDKLocations")]
    public void ControlledBuildInputs_RejectAmbientDiscoveryTask(string taskName)
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "App.proj"), $$"""
                <Project>
                  <Target Name="Discover"><{{taskName}} /></Target>
                </Project>
                """);

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReadSourceProvenance_IgnoresUnrelatedControlledBuildProjects()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "Library")).FullName;
            string toolsDirectory = Directory.CreateDirectory(Path.Combine(root, "tools")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string libraryProject = Path.Combine(libraryDirectory, "Library.csproj");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      ReferenceOutputAssembly="false"
                                      OutputItemType="EmbeddedResource"
                                      LogicalName="App.Payloads.Library.dll" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(libraryProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <OutDir>$(MSBuildProjectDirectory)/../../artifacts/shared/</OutDir>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(toolsDirectory, "Unrelated.proj"), """
                <Project>
                  <Target Name="Unrelated"><Exec Command="echo unrelated" /></Target>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { private static void Main() { } }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\nartifacts/\n");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add src/*/packages.lock.json");
            RunGit(root, "commit -m \"lock approved dependencies\"");
            RunDotNet(root, $"build \"{libraryProject}\" -c Release --no-restore --nologo");

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ReadSourceProvenance_RejectsAmbientAssemblyDiscoveryOutput()
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
            string outputPath = Path.Combine(root, "artifacts", "shared", "Library.dll");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      ReferenceOutputAssembly="false"
                                      OutputItemType="EmbeddedResource"
                                      LogicalName="App.Payloads.Library.dll" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(libraryProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <OutDir>$(MSBuildProjectDirectory)/../../artifacts/shared/</OutDir>
                  </PropertyGroup>
                  <Target Name="ReplaceOutputFromAmbientAssembly" AfterTargets="CoreCompile">
                    <ItemGroup><AmbientAssembly Include="System.Xml" /></ItemGroup>
                    <ResolveAssemblyReference Assemblies="@(AmbientAssembly)" SearchPaths="{GAC}">
                      <Output TaskParameter="ResolvedFiles" ItemName="AmbientResolved" />
                    </ResolveAssemblyReference>
                    <Copy SourceFiles="@(AmbientResolved)" DestinationFiles="$(TargetPath)" />
                  </Target>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { private static void Main() { } }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\nartifacts/\n");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add src/*/packages.lock.json");
            RunGit(root, "commit -m \"lock approved dependencies\"");
            RunDotNet(root, $"build \"{libraryProject}\" -c Release --no-restore --nologo");
            Assert.True(File.Exists(outputPath));

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
            Assert.Contains(
                provenance.DirtyReasons,
                reason => reason.Contains("artifacts", StringComparison.OrdinalIgnoreCase) &&
                          reason.Contains("Library.dll", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ReadSourceProvenance_PreservesPublishConditionForStaticProjectReference()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "App")).FullName;
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "src", "Library")).FullName;
            string inputDirectory = Directory.CreateDirectory(Path.Combine(root, "inputs")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string conditionalSource = Path.Combine(inputDirectory, "Conditional.cs");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup Condition="'$(BuildProjectReferences)' == 'true'">
                    <ProjectReference Include="../Library/Library.csproj" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup><Compile Include="../../inputs/Conditional.cs" /></ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { private static void Main() { } }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(conditionalSource, "public static class ConditionalInput { public const int Value = 1; }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            File.WriteAllText(conditionalSource, "public static class ConditionalInput { public const int Value = 2; }");
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Targets =
                [
                    new DotNetPublishTargetPlan
                    {
                        Name = "App",
                        ProjectPath = appProject,
                        Combinations =
                        [
                            new DotNetPublishTargetCombination
                            {
                                Framework = "net8.0",
                                Style = DotNetPublishStyle.FrameworkDependent
                            }
                        ]
                    }
                ]
            };
            plan.MsBuildProperties["BuildProjectReferences"] = "true";

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, buildPlan: plan);

            Assert.True(provenance.Dirty);
            Assert.Contains(
                provenance.DirtyPaths,
                path => path.Replace('\\', '/').EndsWith("inputs/Conditional.cs", StringComparison.Ordinal));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}
