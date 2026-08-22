using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ReadSourceProvenance_IgnoresProjectReferenceOutputUnderOutDir()
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
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { private static void Main() { } }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\nartifacts/\n");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add src/*/packages.lock.json");
            RunGit(root, "commit -m \"lock approved dependencies\"");
            RunDotNet(root, $"build \"{libraryProject}\" -c Release --no-restore --nologo");
            Assert.True(File.Exists(Path.Combine(root, "artifacts", "shared", "Library.dll")));

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

    [Fact]
    public void ReadSourceProvenance_PreservesPropertiesForLegacyProjectReferences()
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
            string signedSource = Path.Combine(inputDirectory, "Signed.cs");
            File.WriteAllText(appProject, CreateLegacyProject(
                "App",
                "<ProjectReference Include=\"../Library/Library.csproj\"><AdditionalProperties>Flavor=Signed</AdditionalProperties></ProjectReference>"));
            File.WriteAllText(
                Path.Combine(libraryDirectory, "Library.csproj"),
                CreateLegacyProject(
                    "Library",
                    "<Compile Include=\"../../inputs/Signed.cs\" Condition=\"'$(Flavor)' == 'Signed'\" />"));
            File.WriteAllText(Path.Combine(appDirectory, "App.cs"), "public static class App { }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(signedSource, "public static class SignedInput { public const int Value = 1; }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            File.WriteAllText(signedSource, "public static class SignedInput { public const int Value = 2; }");

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
            Assert.Contains(
                provenance.DirtyPaths,
                path => path.Replace('\\', '/').EndsWith("inputs/Signed.cs", StringComparison.Ordinal));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ReadSourceProvenance_AppliesProjectReferencePropertyRemovalsLast()
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
            string libraryProject = Path.Combine(libraryDirectory, "Library.csproj");
            string signedSource = Path.Combine(inputDirectory, "Signed.cs");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      AdditionalProperties="Flavor=Signed"
                                      UndefineProperties="Flavor"
                                      ReferenceOutputAssembly="false"
                                      OutputItemType="EmbeddedResource"
                                      LogicalName="App.Payloads.Library.dll" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(libraryProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'Signed'">
                    <Compile Include="../../inputs/Signed.cs" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { private static void Main() { } }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(signedSource, "public static class SignedInput { public const int Value = 1; }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            RunDotNet(root, $"build \"{libraryProject}\" -c Release --no-restore --nologo");
            File.WriteAllText(signedSource, "public static class SignedInput { public const int Value = 2; }");

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

    [Fact]
    public void ReadSourceProvenance_KeepsCollidingProjectReferencePropertyContextsDistinct()
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
            string firstSource = Path.Combine(inputDirectory, "First.cs");
            string secondSource = Path.Combine(inputDirectory, "Second.cs");
            string thirdSource = Path.Combine(inputDirectory, "Third.cs");
            File.WriteAllText(appProject, CreateLegacyProject(
                "App",
                """
                <ProjectReference Include="../Library/Library.csproj"><AdditionalProperties>A=x|B=y</AdditionalProperties></ProjectReference>
                <ProjectReference Include="../Library/./Library.csproj"><AdditionalProperties>A=x;B=y</AdditionalProperties></ProjectReference>
                <ProjectReference Include="../Library/../Library/Library.csproj"><AdditionalProperties>A=X;B=y</AdditionalProperties></ProjectReference>
                """));
            File.WriteAllText(
                Path.Combine(libraryDirectory, "Library.csproj"),
                CreateLegacyProject(
                    "Library",
                    """
                    <Compile Include="../../inputs/First.cs" Condition="'$(A)' == 'x|B=y'" />
                    <Compile Include="../../inputs/Second.cs" Condition="'$(A)' == 'x' and '$(B)' == 'y'" />
                    <Compile Include="../../inputs/Third.cs" Condition="'$(A)' == 'X' and '$(B)' == 'y'" />
                    """));
            File.WriteAllText(Path.Combine(appDirectory, "App.cs"), "public static class App { }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(firstSource, "public static class FirstInput { public const int Value = 1; }");
            File.WriteAllText(secondSource, "public static class SecondInput { public const int Value = 1; }");
            File.WriteAllText(thirdSource, "public static class ThirdInput { public const int Value = 1; }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            File.WriteAllText(firstSource, "public static class FirstInput { public const int Value = 2; }");
            File.WriteAllText(secondSource, "public static class SecondInput { public const int Value = 2; }");
            File.WriteAllText(thirdSource, "public static class ThirdInput { public const int Value = 2; }");

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
            Assert.Contains(provenance.DirtyPaths, path => path.Replace('\\', '/').EndsWith("inputs/First.cs", StringComparison.Ordinal));
            Assert.Contains(provenance.DirtyPaths, path => path.Replace('\\', '/').EndsWith("inputs/Second.cs", StringComparison.Ordinal));
            Assert.Contains(provenance.DirtyPaths, path => path.Replace('\\', '/').EndsWith("inputs/Third.cs", StringComparison.Ordinal));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ReadSourceProvenance_AppliesProjectReferencePropertyTablePrecedence()
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
            string replacedSource = Path.Combine(inputDirectory, "Replaced.cs");
            string additionalSource = Path.Combine(inputDirectory, "Additional.cs");
            File.WriteAllText(appProject, CreateLegacyProject(
                "App",
                """
                <ProjectReference Include="../Library/Library.csproj"
                                  SetConfiguration="Flavor=Task"
                                  Properties="Flavor=Replacement" />
                <ProjectReference Include="../Library/./Library.csproj"
                                  SetConfiguration="Flavor=Task"
                                  Properties="Flavor=Replacement"
                                  AdditionalProperties="Flavor=Additional" />
                """));
            File.WriteAllText(
                Path.Combine(libraryDirectory, "Library.csproj"),
                CreateLegacyProject(
                    "Library",
                    """
                    <Compile Include="../../inputs/Replaced.cs" Condition="'$(Flavor)' == 'Replacement'" />
                    <Compile Include="../../inputs/Additional.cs" Condition="'$(Flavor)' == 'Additional'" />
                    """));
            File.WriteAllText(Path.Combine(appDirectory, "App.cs"), "public static class App { }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(replacedSource, "public static class ReplacedInput { public const int Value = 1; }");
            File.WriteAllText(additionalSource, "public static class AdditionalInput { public const int Value = 1; }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            File.WriteAllText(replacedSource, "public static class ReplacedInput { public const int Value = 2; }");
            File.WriteAllText(additionalSource, "public static class AdditionalInput { public const int Value = 2; }");

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
            Assert.Contains(provenance.DirtyPaths, path => path.Replace('\\', '/').EndsWith("inputs/Replaced.cs", StringComparison.Ordinal));
            Assert.Contains(provenance.DirtyPaths, path => path.Replace('\\', '/').EndsWith("inputs/Additional.cs", StringComparison.Ordinal));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ReadSourceProvenance_UsesProjectReferencePropertyTargetFramework()
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
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      Properties="TargetFramework=net8.0" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.csproj"), """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFrameworks>net8.0;net10.0</TargetFrameworks></PropertyGroup>
                  <ItemGroup Condition="'$(TargetFramework)' == 'net8.0'">
                    <Compile Include="../../inputs/NetEight.cs" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { private static void Main() { } }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(netEightSource, "public static class NetEightInput { public const int Value = 1; }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            File.WriteAllText(netEightSource, "public static class NetEightInput { public const int Value = 2; }");

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(
                    root,
                    buildProjectPaths: [appProject],
                    buildConfiguration: "Release");

            Assert.True(provenance.Dirty);
            Assert.Contains(
                provenance.DirtyPaths,
                path => path.Replace('\\', '/').EndsWith("inputs/NetEight.cs", StringComparison.Ordinal));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    private static string CreateLegacyProject(string assemblyName, string items)
        => $"""
            <Project ToolsVersion="Current" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <Configuration>Release</Configuration>
                <Platform>AnyCPU</Platform>
                <OutputType>Library</OutputType>
                <TargetFrameworkVersion>v4.7.2</TargetFrameworkVersion>
                <AssemblyName>{assemblyName}</AssemblyName>
              </PropertyGroup>
              <ItemGroup>
                <Compile Include="{assemblyName}.cs" />
                {items}
              </ItemGroup>
              <Import Project="$(MSBuildToolsPath)/Microsoft.CSharp.targets" />
            </Project>
            """;

    private static void DeleteTestRepository(string root)
    {
        if (!Directory.Exists(root))
            return;
        foreach (FileInfo file in new DirectoryInfo(root).EnumerateFiles("*", SearchOption.AllDirectories))
            file.Attributes = FileAttributes.Normal;
        Directory.Delete(root, recursive: true);
    }
}
