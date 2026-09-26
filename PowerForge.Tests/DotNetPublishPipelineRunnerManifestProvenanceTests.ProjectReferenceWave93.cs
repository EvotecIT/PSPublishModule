using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ReadSourceProvenance_AllowsDeterministicPathMapDestination()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "App")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <RuntimeIdentifiers>linux-x64</RuntimeIdentifiers>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"),
                "internal static class Program { private static void Main() { } }");
            File.WriteAllText(Path.Combine(root, "Directory.Build.targets"), """
                <Project>
                  <PropertyGroup Condition="'$(Configuration)' == 'Release'">
                    <DeterministicSourcePaths>true</DeterministicSourcePaths>
                    <PathMap>$(MSBuildThisFileDirectory)=/_/</PathMap>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root,
                $"restore \"{appProject}\" -r linux-x64 --use-lock-file --nologo -p:SelfContained=false");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source and lock\"");
            string revision = RunGit(root, "rev-parse HEAD").Trim();
            RunDotNet(root,
                $"build \"{appProject}\" -c Release -f net10.0 -r linux-x64 --no-restore --nologo " +
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
    public void ReadSourceProvenance_InheritsVerifiedSdkPackageEvidenceForProjectReferences()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "App")).FullName;
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "Library")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string libraryProject = Path.Combine(libraryDirectory, "Library.csproj");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <RuntimeIdentifiers>linux-x64</RuntimeIdentifiers>
                    <IsTrimmable>true</IsTrimmable>
                  </PropertyGroup>
                  <ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup>
                </Project>
                """);
            File.WriteAllText(libraryProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                    <IsTrimmable>true</IsTrimmable>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"),
                "internal static class Program { private static void Main() { _ = Library.Value; } }");
            File.WriteAllText(Path.Combine(libraryDirectory, "Library.cs"),
                "public static class Library { public const int Value = 1; }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root,
                $"restore \"{appProject}\" -r linux-x64 --use-lock-file --nologo -p:SelfContained=false");
            File.Delete(Path.Combine(libraryDirectory, "packages.lock.json"));
            Assert.False(File.Exists(Path.Combine(libraryDirectory, "packages.lock.json")));
            File.WriteAllText(Path.Combine(root, "Directory.Build.props"), """
                <Project>
                  <PropertyGroup>
                    <NuGetLockFilePath Condition="!$([MSBuild]::IsOsPlatform('Windows')) and '$(LockedGraphRestore)' == 'true'">packages.nonwindows.lock.json</NuGetLockFilePath>
                    <DisableImplicitLibraryPacksFolder Condition="'$(LockedGraphRestore)' == 'true'">true</DisableImplicitLibraryPacksFolder>
                    <NuGetLockFilePath Condition="'$(LockedGraphRestore)' != 'true' and Exists('$(MSBuildProjectDirectory)/packages.lock.json')">$(BaseIntermediateOutputPath)packages.restore.lock.json</NuGetLockFilePath>
                  </PropertyGroup>
                </Project>
                """);
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source and root lock\"");
            string revision = RunGit(root, "rev-parse HEAD").Trim();
            RunDotNet(root,
                $"build \"{appProject}\" -c Release -f net10.0 -r linux-x64 --no-restore --nologo " +
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
                MsBuildProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["LockedGraphRestore"] = "true",
                    ["DisableImplicitLibraryPacksFolder"] = "true",
                    ["RestoreLockedMode"] = "true"
                },
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
            string publishEvaluationKey = DotNetPublishPipelineRunner.BuildPublishEvaluationRequestKey(
                plan,
                plan.Targets[0],
                "net10.0",
                "linux-x64",
                DotNetPublishStyle.FrameworkDependent);
            Assert.Contains(
                provenance.NoBuildPublishInputs,
                input => string.Equals(input.EvaluationKey, publishEvaluationKey, StringComparison.Ordinal));

            var twoRootPlan = new DotNetPublishPlan
            {
                UseControlledSourceProvenance = true,
                ProjectRoot = root,
                Configuration = "Release",
                SourceRevision = revision,
                NoBuildInPublish = true,
                NoRestoreInPublish = true,
                MsBuildProperties = plan.MsBuildProperties,
                Targets =
                [
                    plan.Targets[0],
                    new DotNetPublishTargetPlan
                    {
                        Name = "Library",
                        ProjectPath = libraryProject,
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

            DotNetPublishPipelineRunner.SourceProvenance isolatedRootProvenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, buildPlan: twoRootPlan);

            Assert.False(
                isolatedRootProvenance.Dirty,
                string.Join(Environment.NewLine, isolatedRootProvenance.DirtyReasons));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ReadSourceProvenance_PortableDiamondBuildsRidAgnosticReferencesInDependencyOrder()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "App")).FullName;
            string middleDirectory = Directory.CreateDirectory(Path.Combine(root, "Middle")).FullName;
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "Library")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string middleProject = Path.Combine(middleDirectory, "Middle.csproj");
            string libraryProject = Path.Combine(libraryDirectory, "Library.csproj");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <RuntimeIdentifiers>win-x64</RuntimeIdentifiers>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Library/Library.csproj" />
                    <ProjectReference Include="../Middle/Middle.csproj" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(middleProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFrameworks>net8.0;net8.0-windows</TargetFrameworks></PropertyGroup>
                  <ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup>
                </Project>
                """);
            File.WriteAllText(libraryProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFrameworks>net8.0;net8.0-windows</TargetFrameworks></PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(appDirectory, "Program.cs"),
                "internal static class Program { private static void Main() { _ = Middle.Value + Library.Value; } }");
            File.WriteAllText(
                Path.Combine(middleDirectory, "Middle.cs"),
                "public static class Middle { public static int Value => Library.Value; }");
            File.WriteAllText(
                Path.Combine(libraryDirectory, "Library.cs"),
                "public static class Library { public const int Value = 1; }");
            File.WriteAllText(
                Path.Combine(root, "Directory.Build.targets"),
                """
                <Project>
                  <Target Name="RemovePathDependentCompilerPropertyForGraphTest"
                          BeforeTargets="GenerateMSBuildEditorConfigFileCore">
                    <ItemGroup><CompilerVisibleProperty Remove="ProjectDir" /></ItemGroup>
                  </Target>
                </Project>
                """);
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(
                root,
                $"restore \"{appProject}\" -r win-x64 --use-lock-file --nologo " +
                "-p:SelfContained=false");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source and lock\"");
            string revision = RunGit(root, "rev-parse HEAD").Trim();
            RunDotNet(
                root,
                $"build \"{appProject}\" -c Release -f net10.0 -r win-x64 --no-restore --nologo " +
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
                                Runtime = "win-x64",
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
    public void ReadSourceProvenance_ControlledRestoreHonorsConditionalReleaseProperty()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "App")).FullName;
            string libraryDirectory = Directory.CreateDirectory(Path.Combine(root, "Library")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string libraryProject = Path.Combine(libraryDirectory, "Library.csproj");
            File.WriteAllText(appProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <RuntimeIdentifiers>win-x64</RuntimeIdentifiers>
                  </PropertyGroup>
                  <ItemGroup Condition="'$(PowerForgeFlavor)' == 'Release'">
                    <ProjectReference Include="../Library/Library.csproj" />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(libraryProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(appDirectory, "Program.cs"),
                "internal static class Program { private static void Main() { _ = Library.Value; } }");
            File.WriteAllText(
                Path.Combine(libraryDirectory, "Library.cs"),
                "public static class Library { public const int Value = 1; }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(
                root,
                $"restore \"{appProject}\" -r win-x64 --use-lock-file --nologo " +
                "-p:SelfContained=false -p:PowerForgeFlavor=Release");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source and lock\"");
            RunDotNet(
                root,
                $"build \"{appProject}\" -c Release -f net10.0 -r win-x64 --no-restore --nologo " +
                "-p:PowerForgeFlavor=Release -p:ContinuousIntegrationBuild=true " +
                "-p:DebugType=None -p:DebugSymbols=false");
            var plan = new DotNetPublishPlan
            {
                UseControlledSourceProvenance = true,
                ProjectRoot = root,
                Configuration = "Release",
                NoBuildInPublish = true,
                NoRestoreInPublish = true,
                MsBuildProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PowerForgeFlavor"] = "Release"
                },
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
                                Runtime = "win-x64",
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
