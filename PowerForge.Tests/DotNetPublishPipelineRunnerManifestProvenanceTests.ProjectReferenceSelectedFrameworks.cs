using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ReadSourceProvenance_DoesNotRestoreUnselectedDeclaredFrameworkPackages()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "App")).FullName;
            string sharedDirectory = Directory.CreateDirectory(Path.Combine(root, "Shared")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string sharedProject = Path.Combine(sharedDirectory, "Shared.csproj");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <RuntimeIdentifiers>linux-x64</RuntimeIdentifiers>
                  </PropertyGroup>
                  <ItemGroup><ProjectReference Include="../Shared/Shared.csproj" /></ItemGroup>
                </Project>
                """);
            File.WriteAllText(sharedProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFrameworks>netstandard2.0;net10.0</TargetFrameworks></PropertyGroup>
                  <ItemGroup Condition="'$(TargetFramework)' == 'netstandard2.0'">
                    <PackageReference Include="System.Text.Json" Version="10.0.10" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"),
                "internal static class Program { private static void Main() { _ = Shared.Value; } }");
            File.WriteAllText(Path.Combine(sharedDirectory, "Shared.cs"),
                "public static class Shared { public const int Value = 1; }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root,
                $"restore \"{appProject}\" -r linux-x64 --use-lock-file --nologo -p:SelfContained=false");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved selected-framework graph\"");
            string revision = RunGit(root, "rev-parse HEAD").Trim();
            RunDotNet(root,
                $"build \"{appProject}\" -c Release -f net10.0 -r linux-x64 --no-restore --nologo " +
                "-m:1 -p:BuildInParallel=false " +
                $"/p:SourceRevisionId={revision} /p:IncludeSourceRevisionInInformationalVersion=true " +
                "/p:ContinuousIntegrationBuild=true /p:DebugType=None /p:DebugSymbols=false");
            var plan = new DotNetPublishPlan
            {
                UseControlledSourceProvenance = true,
                ProjectRoot = root,
                Configuration = "Release",
                SourceRevision = revision,
                NoBuildInPublish = true,
                NoRestoreInPublish = true,
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
                                Framework = "net10.0",
                                Runtime = "linux-x64",
                                Style = DotNetPublishStyle.FrameworkDependent
                            }
                        ]
                    }
                ]
            };

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, buildPlan: plan);

            Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ReadSourceProvenance_PreservesDeclaredFrameworkSemanticsForSelectedSubset()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "App")).FullName;
            string bridgeDirectory = Directory.CreateDirectory(Path.Combine(root, "Bridge")).FullName;
            string sharedDirectory = Directory.CreateDirectory(Path.Combine(root, "Shared")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string bridgeProject = Path.Combine(bridgeDirectory, "Bridge.csproj");
            string sharedProject = Path.Combine(sharedDirectory, "Shared.csproj");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <RuntimeIdentifiers>linux-x64</RuntimeIdentifiers>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Bridge/Bridge.csproj" />
                    <ProjectReference Include="../Shared/Shared.csproj" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(bridgeProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net9.0</TargetFramework></PropertyGroup>
                  <ItemGroup><ProjectReference Include="../Shared/Shared.csproj" /></ItemGroup>
                </Project>
                """);
            File.WriteAllText(sharedProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFrameworks>net10.0; net8.0;net9.0</TargetFrameworks></PropertyGroup>
                  <ItemGroup Condition="'$(TargetFrameworks)' == 'net10.0; net8.0;net9.0'">
                    <PackageReference Include="NuGet.Versioning" Version="7.9.0" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"),
                "internal static class Program { private static void Main() { _ = Bridge.Value + Shared.Value; } }");
            File.WriteAllText(Path.Combine(bridgeDirectory, "Bridge.cs"),
                "public static class Bridge { public static int Value => Shared.Value; }");
            File.WriteAllText(Path.Combine(sharedDirectory, "Shared.cs"),
                "public static class Shared { public static int Value => NuGet.Versioning.NuGetVersion.Parse(\"1.0.0\").Major; }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root,
                $"restore \"{appProject}\" -r linux-x64 --use-lock-file --nologo -p:SelfContained=false");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved selected subset graph\"");
            string revision = RunGit(root, "rev-parse HEAD").Trim();
            RunDotNet(root,
                $"build \"{appProject}\" -c Release -f net10.0 -r linux-x64 --no-restore --nologo " +
                "-m:1 -p:BuildInParallel=false " +
                $"/p:SourceRevisionId={revision} /p:IncludeSourceRevisionInInformationalVersion=true " +
                "/p:ContinuousIntegrationBuild=true /p:DebugType=None /p:DebugSymbols=false");
            var plan = new DotNetPublishPlan
            {
                UseControlledSourceProvenance = true,
                ProjectRoot = root,
                Configuration = "Release",
                SourceRevision = revision,
                NoBuildInPublish = true,
                NoRestoreInPublish = true,
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
                                Framework = "net10.0",
                                Runtime = "linux-x64",
                                Style = DotNetPublishStyle.FrameworkDependent
                            }
                        ]
                    }
                ]
            };

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, buildPlan: plan);

            Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

}
