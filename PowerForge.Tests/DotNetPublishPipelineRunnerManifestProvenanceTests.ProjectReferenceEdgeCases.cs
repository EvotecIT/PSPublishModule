using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ReadSourceProvenance_PreservesEscapedEqualsInsideProjectReferencePropertyValues()
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
            string escapedSource = Path.Combine(inputDirectory, "EscapedEquals.cs");
            File.WriteAllText(Path.Combine(root, "Directory.Build.props"), """
                <Project>
                  <PropertyGroup>
                    <ReferenceProperties>Flavor=A%3BB%3DC</ReferenceProperties>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="$(ReferenceProperties)" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'A;B=C'">
                    <Compile Include="../../inputs/EscapedEquals.cs" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { private static void Main() { } }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(escapedSource, "public static class EscapedEqualsInput { public const int Value = 1; }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            File.WriteAllText(escapedSource, "public static class EscapedEqualsInput { public const int Value = 2; }");

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
            Assert.Contains(
                provenance.DirtyPaths,
                path => path.Replace('\\', '/').EndsWith("inputs/EscapedEquals.cs", StringComparison.Ordinal));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ReadSourceProvenance_ExpandsExplicitlyEmptyProjectReferenceTargetFramework()
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
            string netEightSource = Path.Combine(inputDirectory, "NetEight.cs");
            string netTenSource = Path.Combine(inputDirectory, "NetTen.cs");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      Properties="TargetFramework="
                                      ReferenceOutputAssembly="false"
                                      BuildReference="false" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFrameworks>net8.0;net10.0</TargetFrameworks></PropertyGroup>
                  <ItemGroup Condition="'$(TargetFramework)' == 'net8.0'">
                    <Compile Include="../../inputs/NetEight.cs" />
                  </ItemGroup>
                  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
                    <Compile Include="../../inputs/NetTen.cs" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { private static void Main() { } }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(netEightSource, "public static class NetEightInput { public const int Value = 1; }");
            File.WriteAllText(netTenSource, "public static class NetTenInput { public const int Value = 1; }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            File.WriteAllText(netEightSource, "public static class NetEightInput { public const int Value = 2; }");
            File.WriteAllText(netTenSource, "public static class NetTenInput { public const int Value = 2; }");

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
            Assert.True(
                provenance.DirtyPaths.Any(path =>
                    path.Replace('\\', '/').EndsWith("inputs/NetEight.cs", StringComparison.Ordinal)),
                string.Join(Environment.NewLine, provenance.DirtyReasons));
            Assert.True(
                provenance.DirtyPaths.Any(path =>
                    path.Replace('\\', '/').EndsWith("inputs/NetTen.cs", StringComparison.Ordinal)),
                string.Join(Environment.NewLine, provenance.DirtyReasons));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ReadSourceProvenance_FailsClosedForUnrecoverableAmbiguousProjectReferenceProperties()
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
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="$(ReferenceProperties)" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { private static void Main() { } }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
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
            plan.MsBuildProperties["ReferenceProperties"] = "Flavor=A;B=C";

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, buildPlan: plan);

            Assert.True(provenance.Dirty);
            Assert.Contains(
                provenance.DirtyReasons,
                reason => reason.Contains("MSBuild input evaluation failed", StringComparison.Ordinal));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ReadSourceProvenance_RecoversPropertiesFromProjectReferenceUpdate()
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
            string selectedSource = Path.Combine(inputDirectory, "Selected.cs");
            File.WriteAllText(Path.Combine(root, "Directory.Build.props"), """
                <Project>
                  <PropertyGroup>
                    <ReferenceProperties>Flavor=A%3BB%3DC</ReferenceProperties>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(root, "Directory.Build.targets"), """
                <Project>
                  <ItemGroup>
                    <ProjectReference Update="../Library/Library.csproj"
                                      AdditionalProperties="$(ReferenceProperties)" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'A;B=C'">
                    <Compile Include="../../inputs/Selected.cs" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { private static void Main() { } }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(selectedSource, "public static class SelectedInput { public const int Value = 1; }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            File.WriteAllText(selectedSource, "public static class SelectedInput { public const int Value = 2; }");

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
            Assert.Contains(
                provenance.DirtyPaths,
                path => path.Replace('\\', '/').EndsWith("inputs/Selected.cs", StringComparison.Ordinal));
            Assert.DoesNotContain(
                provenance.DirtyReasons,
                reason => reason.Contains("MSBuild input evaluation failed", StringComparison.Ordinal));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ReadSourceProvenance_RecoversActiveConditionedProjectReferenceProperties()
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
            string selectedSource = Path.Combine(inputDirectory, "Selected.cs");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <ReferenceMode>Escaped</ReferenceMode>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj">
                      <AdditionalProperties Condition="'$(ReferenceMode)' == 'Plain'">Flavor=Plain</AdditionalProperties>
                      <AdditionalProperties Condition="'$(ReferenceMode)' == 'Escaped'">Flavor=A%3BB%3DC</AdditionalProperties>
                    </ProjectReference>
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'A;B=C'">
                    <Compile Include="../../inputs/Selected.cs" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { private static void Main() { } }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(selectedSource, "public static class SelectedInput { public const int Value = 1; }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            File.WriteAllText(selectedSource, "public static class SelectedInput { public const int Value = 2; }");

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
            Assert.Contains(
                provenance.DirtyPaths,
                path => path.Replace('\\', '/').EndsWith("inputs/Selected.cs", StringComparison.Ordinal));
            Assert.DoesNotContain(
                provenance.DirtyReasons,
                reason => reason.Contains("MSBuild input evaluation failed", StringComparison.Ordinal));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void IsTrustedMsBuildProjectReferenceTargetPath_UsesEvaluatedSdkPathForAnalyzers()
    {
        string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PowerForgeSdkTrust"));
        string toolsPath = Path.Combine(root, "sdk", "10.0.100");
        string sdksPath = Path.Combine(root, "custom-sdks");
        string evaluatedAnalyzerTarget = Path.Combine(
            sdksPath,
            "Microsoft.NET.Sdk",
            "targets",
            "Microsoft.NET.ConflictResolution.targets");
        string spoofedAnalyzerTarget = Path.Combine(
            root,
            "source",
            "Microsoft.NET.ConflictResolution.targets");

        Assert.True(DotNetPublishPipelineRunner.IsTrustedMsBuildProjectReferenceTargetPath(
            evaluatedAnalyzerTarget,
            "Analyzer",
            toolsPath,
            sdksPath));
        Assert.False(DotNetPublishPipelineRunner.IsTrustedMsBuildProjectReferenceTargetPath(
            spoofedAnalyzerTarget,
            "Analyzer",
            toolsPath,
            sdksPath));
        Assert.False(DotNetPublishPipelineRunner.IsTrustedMsBuildProjectReferenceTargetPath(
            evaluatedAnalyzerTarget,
            "EmbeddedResource",
            toolsPath,
            sdksPath));
    }
}
