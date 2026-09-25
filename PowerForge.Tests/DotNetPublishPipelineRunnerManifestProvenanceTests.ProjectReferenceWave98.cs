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

    [Theory]
    [InlineData("single-context")]
    [InlineData("two-contexts")]
    [InlineData("conditional-packages")]
    [InlineData("custom-props")]
    [InlineData("escaped-output")]
    [InlineData("escaped-outdir")]
    [InlineData("escaped-assets")]
    [InlineData("shadowed-verifier")]
    [InlineData("controlled-properties")]
    [InlineData("disabled-props-environment")]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ReadSourceProvenance_RestoresEverySelectedFrameworkForSharedMultiTargetReference(string scenario)
    {
        bool distinctRestoreContexts = scenario != "single-context";
        bool unselectedContextPackage = scenario != "single-context" && scenario != "two-contexts";
        bool customDirectoryBuildProps = scenario == "custom-props";
        bool overrideSharedOutputPath = scenario == "escaped-output" || scenario == "shadowed-verifier";
        bool overrideSharedOutDir = scenario == "escaped-outdir";
        bool overrideSharedAssets = scenario == "escaped-assets";
        bool shadowVerifier = scenario == "shadowed-verifier";
        bool controlledProperties = scenario == "controlled-properties";
        bool disabledPropsEnvironment = scenario == "disabled-props-environment";
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string appDirectory = Directory.CreateDirectory(Path.Combine(root, "App")).FullName;
            string bridgeDirectory = Directory.CreateDirectory(Path.Combine(root, "Bridge")).FullName;
            string sharedDirectory = Directory.CreateDirectory(Path.Combine(root, "Shared")).FullName;
            string leafDirectory = Directory.CreateDirectory(Path.Combine(root, "Leaf")).FullName;
            string appProject = Path.Combine(appDirectory, "App.csproj");
            string bridgeProject = Path.Combine(bridgeDirectory, "Bridge.csproj");
            string sharedProject = Path.Combine(sharedDirectory, "Shared.csproj");
            string leafProject = Path.Combine(leafDirectory, "Leaf.csproj");
            string? customPropsPath = null;
            string customPropsArgument = string.Empty;
            if (customDirectoryBuildProps)
            {
                customPropsPath = Path.Combine(root, "Custom.Build.props");
                File.WriteAllText(customPropsPath,
                    "<Project><PropertyGroup><ContextPropsMarker>Loaded</ContextPropsMarker></PropertyGroup></Project>");
                customPropsArgument = $" -p:DirectoryBuildPropsPath=\"{customPropsPath}\"";
            }
            string directContext = distinctRestoreContexts
                ? " AdditionalProperties=\"Flavor=Direct\""
                : string.Empty;
            string bridgeContext = distinctRestoreContexts
                ? " AdditionalProperties=\"Flavor=Bridge\""
                : string.Empty;
            File.WriteAllText(appProject, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <OutputType>Exe</OutputType>
                    <TargetFramework>net10.0</TargetFramework>
                    <RuntimeIdentifiers>linux-x64</RuntimeIdentifiers>
                  </PropertyGroup>
                  <ItemGroup>
                    <ProjectReference Include="../Bridge/Bridge.csproj" />
                    <ProjectReference Include="../Shared/Shared.csproj"{directContext} />
                  </ItemGroup>
                </Project>
                """);
            File.WriteAllText(bridgeProject, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup><ProjectReference Include="../Shared/Shared.csproj"{bridgeContext} /></ItemGroup>
                </Project>
                """);
            string contextPackages = unselectedContextPackage
                ? """
                  <ItemGroup Condition="'$(Flavor)' == 'Direct' and '$(TargetFramework)' == 'net10.0'">
                    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
                  </ItemGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'Bridge' and '$(TargetFramework)' == 'net8.0'">
                    <PackageReference Include="System.Text.Json" Version="10.0.10" />
                  </ItemGroup>
                  <ItemGroup Condition="'$(Flavor)' == 'Direct' and '$(TargetFramework)' == 'net8.0'">
                    <PackageReference Include="NuGet.Versioning" Version="7.9.0" />
                  </ItemGroup>
                  """
                : string.Empty;
            string outputOverride = overrideSharedOutputPath
                ? "<OutputPath>$(BaseOutputPath)../shared/</OutputPath>"
                : string.Empty;
            string outDirOverride = overrideSharedOutDir
                ? "<OutDir>$(BaseOutputPath)../shared/</OutDir>"
                : string.Empty;
            string assetsOverride = overrideSharedAssets
                ? "<ProjectAssetsFile Condition=\"'$(_PowerForgeRequiresContextIsolation)' == 'true'\">$(MSBuildProjectDirectory)/../shared-assets/$(Flavor)/project.assets.json</ProjectAssetsFile>"
                : string.Empty;
            string verifierCollision = shadowVerifier
                ? "<Target Name=\"PowerForgeVerifyRestoreContext\" />"
                : string.Empty;
            File.WriteAllText(sharedProject, $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFrameworks>net8.0;net10.0</TargetFrameworks>
                    <NuGetLockFilePath Condition="'$(Flavor)' != ''">packages.$(Flavor).lock.json</NuGetLockFilePath>
                    {outputOverride}
                    {outDirOverride}
                    {assetsOverride}
                  </PropertyGroup>
                  <Target Name="RequireCustomProps" BeforeTargets="Restore;Build" Condition="'$(Flavor)' != '' and '{customDirectoryBuildProps.ToString().ToLowerInvariant()}' == 'true'">
                    <Error Condition="'$(ContextPropsMarker)' != 'Loaded'" Text="Custom DirectoryBuildPropsPath was not imported." />
                  </Target>
                  <ItemGroup><ProjectReference Include="../Leaf/Leaf.csproj" /></ItemGroup>
                  {contextPackages}
                  {verifierCollision}
                </Project>
                """);
            File.WriteAllText(leafProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(appDirectory, "Program.cs"),
                "internal static class Program { private static void Main() { _ = Bridge.Value + Shared.Value; } }");
            File.WriteAllText(Path.Combine(bridgeDirectory, "Bridge.cs"),
                "public static class Bridge { public static int Value => Shared.Value; }");
            File.WriteAllText(Path.Combine(sharedDirectory, "Shared.cs"),
                "public static class Shared { public static int Value => Leaf.Value; }");
            File.WriteAllText(Path.Combine(leafDirectory, "Leaf.cs"),
                "public static class Leaf { public const int Value = 1; }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\nshared-assets/\n");
            if (unselectedContextPackage)
            {
                RunDotNet(root,
                    $"restore \"{sharedProject}\" --use-lock-file --nologo -p:Flavor=Direct -p:TargetFrameworks=net10.0{customPropsArgument}");
                RunDotNet(root,
                    $"restore \"{sharedProject}\" --use-lock-file --nologo -p:Flavor=Bridge -p:TargetFrameworks=net8.0{customPropsArgument}");
            }
            RunDotNet(root,
                $"restore \"{appProject}\" -r linux-x64 --use-lock-file --nologo -p:SelfContained=false{customPropsArgument}");
            if (unselectedContextPackage)
            {
                Assert.Contains("Newtonsoft.Json", File.ReadAllText(Path.Combine(sharedDirectory, "packages.Direct.lock.json")));
                Assert.Contains("System.Text.Json", File.ReadAllText(Path.Combine(sharedDirectory, "packages.Bridge.lock.json")));
                Assert.DoesNotContain("NuGet.Versioning", File.ReadAllText(Path.Combine(sharedDirectory, "packages.Direct.lock.json")));
                Assert.DoesNotContain("NuGet.Versioning", File.ReadAllText(Path.Combine(sharedDirectory, "packages.Bridge.lock.json")));
            }
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved multi-framework graph\"");
            string revision = RunGit(root, "rev-parse HEAD").Trim();
            RunDotNet(root,
                $"build \"{appProject}\" -c Release -f net10.0 -r linux-x64 --no-restore --nologo " +
                "-m:1 -p:BuildInParallel=false " +
                $"/p:SourceRevisionId={revision} /p:IncludeSourceRevisionInInformationalVersion=true " +
                $"/p:ContinuousIntegrationBuild=true /p:DebugType=None /p:DebugSymbols=false{customPropsArgument}");
            var plan = new DotNetPublishPlan
            {
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

            if (customPropsPath is not null)
                plan.MsBuildProperties["DirectoryBuildPropsPath"] = customPropsPath;
            if (controlledProperties)
            {
                plan.MsBuildProperties["BuildProjectReferences"] = "true";
                plan.MsBuildProperties["RestoreRecursive"] = "true";
            }
            if (disabledPropsEnvironment)
            {
                plan.EnvironmentVariables["ImportDirectoryBuildProps"] = "false";
                plan.ControlledBuildEnvironmentVariableNames = ["ImportDirectoryBuildProps"];
            }

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, buildPlan: plan);

            if (overrideSharedOutputPath || overrideSharedOutDir || overrideSharedAssets || disabledPropsEnvironment)
            {
                Assert.True(provenance.Dirty);
                string expected = disabledPropsEnvironment
                    ? "disables Directory.Build.props imports"
                    : overrideSharedOutDir
                        ? "OutDir is outside its controlled context"
                        : overrideSharedAssets
                            ? "assets path is outside its controlled context"
                        : "output path is outside its controlled context";
                Assert.Contains(provenance.DirtyReasons,
                    reason => reason.Contains(expected, StringComparison.Ordinal));
            }
            else
            {
                Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
            }
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Theory]
    [InlineData(null, "net8.0;net10.0", false)]
    [InlineData("", "net8.0;net10.0", false)]
    [InlineData("net8.0", "net8.0;net10.0", false)]
    [InlineData("net8.0;net10.0", "net10.0", false)]
    [InlineData("net8.0;net10.0", "net8.0;net10.0", true)]
    [InlineData("net8.0;net9.0;net10.0", "net9.0;net10.0", true)]
    [InlineData("net8.0;net10.0", "net8.0;net9.0", false)]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ControlledRestore_UsesOnlyDeclaredMultiTargetFrameworkMatrix(
        string? declaredTargetFrameworks,
        string selectedTargetFrameworks,
        bool expectsMatrix)
    {
        var evaluatedProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (declaredTargetFrameworks is not null)
            evaluatedProperties["TargetFrameworks"] = declaredTargetFrameworks;

        string[] frameworks = DotNetPublishPipelineRunner.SelectControlledMultiFrameworkRestoreFrameworks(
            evaluatedProperties,
            selectedTargetFrameworks.Split(';'));

        Assert.Equal(expectsMatrix, frameworks.Length > 1);
        if (expectsMatrix)
        {
            Assert.Equal(
                selectedTargetFrameworks.Split(';').OrderBy(framework => framework, StringComparer.OrdinalIgnoreCase),
                frameworks);
        }
    }
}
