using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ReadSourceProvenance_KeepsInnerEvaluationsInTheirExactPublishContext()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "App")).FullName;
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "Library")).FullName;
            string inputsDirectory = Directory.CreateDirectory(Path.Combine(root, "Inputs")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string libraryProject = Path.Combine(libraryDirectory, "Library.csproj");
            string netEightInput = Path.Combine(inputsDirectory, "NetEightOnly.cs");
            string netTenInput = Path.Combine(inputsDirectory, "NetTenOnly.cs");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup>
                </Project>
                """);
            File.WriteAllText(libraryProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFrameworks>net8.0;net10.0</TargetFrameworks></PropertyGroup>
                  <ItemGroup Condition="'$(TargetFramework)' == 'net8.0'">
                    <Compile Include="../Inputs/NetEightOnly.cs" />
                  </ItemGroup>
                  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0'">
                    <Compile Include="../Inputs/NetTenOnly.cs" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { private static void Main() { } }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(netEightInput, "public static class NetEightOnly { }");
            File.WriteAllText(netTenInput, "public static class NetTenOnly { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved exact contexts\"");
            File.WriteAllText(netEightInput, "public static class NetEightOnly { public const int Changed = 1; }");
            File.WriteAllText(netTenInput, "public static class NetTenOnly { public const int Changed = 1; }");
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release",
                Targets =
                [
                    CreateContextTarget("Signed", appProject, sign: true),
                    CreateContextTarget("Unsigned", appProject, sign: false)
                ]
            };

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, buildPlan: plan);

            Assert.True(provenance.Dirty);
            Assert.Contains(provenance.DirtyPaths, path => path.Replace('\\', '/').EndsWith("Inputs/NetTenOnly.cs", StringComparison.Ordinal));
            Assert.DoesNotContain(provenance.DirtyPaths, path => path.Replace('\\', '/').EndsWith("Inputs/NetEightOnly.cs", StringComparison.Ordinal));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ReadSourceProvenance_UsesEvaluatedPlatformVersionForNearestFramework()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "App")).FullName;
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "Library")).FullName;
            string inputsDirectory = Directory.CreateDirectory(Path.Combine(root, "Inputs")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string libraryProject = Path.Combine(libraryDirectory, "Library.csproj");
            string lowerPlatformInput = Path.Combine(inputsDirectory, "WindowsSevenOnly.cs");
            string selectedPlatformInput = Path.Combine(inputsDirectory, "Windows19041Only.cs");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0-windows</TargetFramework>
                    <TargetPlatformVersion>10.0.19041.0</TargetPlatformVersion>
                    <EnableWindowsTargeting>true</EnableWindowsTargeting>
                  </PropertyGroup>
                  <ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup>
                </Project>
                """);
            File.WriteAllText(libraryProject, """
                <Project Sdk="Microsoft.NET.Sdk" TreatAsLocalProperty="TargetPlatformVersion">
                  <PropertyGroup>
                    <TargetFrameworks>net10.0-windows7.0;net10.0-windows10.0.19041.0</TargetFrameworks>
                    <EnableWindowsTargeting>true</EnableWindowsTargeting>
                  </PropertyGroup>
                  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0-windows7.0'">
                    <Compile Include="../Inputs/WindowsSevenOnly.cs" />
                  </ItemGroup>
                  <ItemGroup Condition="'$(TargetFramework)' == 'net10.0-windows10.0.19041.0'">
                    <Compile Include="../Inputs/Windows19041Only.cs" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { private static void Main() { } }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(lowerPlatformInput, "public static class WindowsSevenOnly { }");
            File.WriteAllText(selectedPlatformInput, "public static class Windows19041Only { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo -p:TargetPlatformVersion=10.0.19041.0");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved platform graph\"");
            File.WriteAllText(lowerPlatformInput, "public static class WindowsSevenOnly { public const int Changed = 1; }");
            File.WriteAllText(selectedPlatformInput, "public static class Windows19041Only { public const int Changed = 1; }");
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release",
                Targets = [CreateContextTarget("App", appProject, sign: true, framework: "net10.0-windows")]
            };
            plan.Targets[0].Publish.MsBuildProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["TargetPlatformVersion"] = "10.0.19041.0"
            };

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, buildPlan: plan);

            Assert.True(provenance.Dirty);
            Assert.True(
                provenance.DirtyPaths.Any(path => path.Replace('\\', '/').EndsWith("Inputs/Windows19041Only.cs", StringComparison.Ordinal)),
                string.Join(Environment.NewLine, provenance.DirtyReasons));
            Assert.DoesNotContain(provenance.DirtyPaths, path => path.Replace('\\', '/').EndsWith("Inputs/WindowsSevenOnly.cs", StringComparison.Ordinal));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ReadSourceProvenance_ExpandsExplicitlyUndefinedFrameworkToEveryInnerBuild()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "App")).FullName;
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "Library")).FullName;
            string inputsDirectory = Directory.CreateDirectory(Path.Combine(root, "Inputs")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string libraryProject = Path.Combine(libraryDirectory, "Library.csproj");
            string netEightInput = Path.Combine(inputsDirectory, "NetEightOnly.cs");
            string netNineInput = Path.Combine(inputsDirectory, "NetNineOnly.cs");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj"
                                      UndefineProperties="TargetFramework"
                                      Targets="Build" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(libraryProject, """
                <Project>
                  <Import Project="Sdk.props" Sdk="Microsoft.NET.Sdk" />
                  <PropertyGroup><TargetFrameworks>net8.0;net9.0</TargetFrameworks></PropertyGroup>
                  <ItemGroup Condition="'$(TargetFramework)' == 'net8.0'">
                    <Compile Include="../Inputs/NetEightOnly.cs" />
                  </ItemGroup>
                  <ItemGroup Condition="'$(TargetFramework)' == 'net9.0'">
                    <Compile Include="../Inputs/NetNineOnly.cs" />
                  </ItemGroup>
                  <Import Project="Sdk.targets" Sdk="Microsoft.NET.Sdk" />
                  <Target Name="GetTargetPath" />
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"), "internal static class Program { private static void Main() { } }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"), "public static class Library { }");
            File.WriteAllText(netEightInput, "public static class NetEightOnly { }");
            File.WriteAllText(netNineInput, "public static class NetNineOnly { }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved undefined framework edge\"");
            File.WriteAllText(netEightInput, "public static class NetEightOnly { public const int Changed = 1; }");
            File.WriteAllText(netNineInput, "public static class NetNineOnly { public const int Changed = 1; }");
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release",
                Targets = [CreateContextTarget("App", appProject, sign: true)]
            };

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, buildPlan: plan);

            Assert.True(
                provenance.DirtyPaths.Any(path => path.Replace('\\', '/').EndsWith("Inputs/NetEightOnly.cs", StringComparison.Ordinal)),
                string.Join(Environment.NewLine, provenance.DirtyReasons));
            Assert.True(
                provenance.DirtyPaths.Any(path => path.Replace('\\', '/').EndsWith("Inputs/NetNineOnly.cs", StringComparison.Ordinal)),
                string.Join(Environment.NewLine, provenance.DirtyReasons));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    private static DotNetPublishTargetPlan CreateContextTarget(
        string name,
        string projectPath,
        bool sign,
        string framework = "net10.0")
        => new()
        {
            Name = name,
            ProjectPath = projectPath,
            Publish = new DotNetPublishPublishOptions
            {
                Sign = new DotNetPublishSignOptions { Enabled = sign }
            },
            Combinations =
            [
                new DotNetPublishTargetCombination
                {
                    Framework = framework,
                    Style = DotNetPublishStyle.FrameworkDependent
                }
            ]
        };
}
