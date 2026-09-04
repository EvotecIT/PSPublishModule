using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void PowerForgeRestorePackageHashes_ReadsNormalizedEvidence()
    {
        using JsonDocument document = JsonDocument.Parse("""
            {
              "powerForgeRestorePackages": {
                "System.Runtime|4.3.1.0": "trusted-content-hash"
              }
            }
            """);

        Assert.True(DotNetPublishPipelineRunner.TryReadPowerForgeRestorePackageHashes(
            document.RootElement,
            out Dictionary<string, string> hashes));
        Assert.Equal("trusted-content-hash", hashes["System.Runtime|4.3.1"]);
    }

    [Fact]
    public void PowerForgeRestorePackageHashes_RejectsConflictingNormalizedEvidence()
    {
        using JsonDocument document = JsonDocument.Parse("""
            {
              "powerForgeRestorePackages": {
                "System.Runtime|4.3.1": "first-content-hash",
                "system.runtime|4.3.1.0": "second-content-hash"
              }
            }
            """);

        Assert.False(DotNetPublishPipelineRunner.TryReadPowerForgeRestorePackageHashes(
            document.RootElement,
            out Dictionary<string, string> hashes));
        Assert.Empty(hashes);
    }

    [Fact]
    public void PowerForgeRestorePackageHashes_AllowsMissingEvidenceSection()
    {
        using JsonDocument document = JsonDocument.Parse("{\"version\": 1}");

        Assert.True(DotNetPublishPipelineRunner.TryReadPowerForgeRestorePackageHashes(
            document.RootElement,
            out Dictionary<string, string> hashes));
        Assert.Empty(hashes);
    }

    [Fact]
    public void PublishProvenanceScope_SelectsOnlyCurrentCombination()
    {
        var step = new DotNetPublishStep
        {
            TargetName = "App",
            Framework = "net10.0",
            Runtime = "linux-arm64",
            Style = DotNetPublishStyle.PortableCompat
        };
        var selected = new DotNetPublishTargetCombination
        {
            Framework = "net10.0",
            Runtime = "linux-arm64",
            Style = DotNetPublishStyle.PortableCompat
        };
        var otherRuntime = new DotNetPublishTargetCombination
        {
            Framework = "net10.0",
            Runtime = "win-x64",
            Style = DotNetPublishStyle.PortableCompat
        };
        var otherStyle = new DotNetPublishTargetCombination
        {
            Framework = "net10.0",
            Runtime = "linux-arm64",
            Style = DotNetPublishStyle.FrameworkDependent
        };

        Assert.True(DotNetPublishPipelineRunner.IsPublishProvenanceCombinationInScope(
            "App",
            selected,
            step));
        Assert.False(DotNetPublishPipelineRunner.IsPublishProvenanceCombinationInScope(
            "Other",
            selected,
            step));
        Assert.False(DotNetPublishPipelineRunner.IsPublishProvenanceCombinationInScope(
            "App",
            otherRuntime,
            step));
        Assert.False(DotNetPublishPipelineRunner.IsPublishProvenanceCombinationInScope(
            "App",
            otherStyle,
            step));
        Assert.True(DotNetPublishPipelineRunner.IsPublishProvenanceCombinationInScope(
            "App",
            otherStyle,
            buildStep: null));
    }

    [Fact]
    public void BuildPreBuildArguments_DisablesIncrementalReuse()
    {
        var plan = new DotNetPublishPlan
        {
            Configuration = "Release",
            Restore = true
        };
        var target = new DotNetPublishTargetPlan
        {
            Name = "App",
            ProjectPath = "App.csproj"
        };

        List<string> arguments = DotNetPublishPipelineRunner.BuildPreBuildArguments(
            plan,
            target,
            "net10.0",
            "linux-arm64",
            DotNetPublishStyle.PortableCompat);

        Assert.Contains("--no-incremental", arguments);
        Assert.Contains("--no-restore", arguments);
    }

    [Fact]
    public void BuildPublishMsBuildProperties_EnableDeterministicSourcePathsForPinnedRevision()
    {
        var plan = new DotNetPublishPlan
        {
            SourceRevision = "0123456789abcdef0123456789abcdef01234567"
        };
        var target = new DotNetPublishTargetPlan
        {
            Name = "App"
        };

        Dictionary<string, string> properties =
            DotNetPublishPipelineRunner.BuildPublishMsBuildProperties(
                plan,
                target,
                "net10.0",
                "linux-arm64",
                DotNetPublishStyle.PortableCompat);

        Assert.Equal("true", properties["ContinuousIntegrationBuild"]);
        Assert.Equal(plan.SourceRevision, properties["SourceRevisionId"]);
        Assert.Equal("true", properties["IncludeSourceRevisionInInformationalVersion"]);
    }

    [Fact]
    [Trait("Category", "DotNetPublishPrGate")]
    public void ReadSourceProvenance_PortableRootDoesNotPublishReferencedLibrary()
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
                  <ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup>
                </Project>
                """);
            File.WriteAllText(libraryProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFrameworks>netstandard2.1;net8.0-windows</TargetFrameworks>
                  </PropertyGroup>
                  <ItemGroup Condition="'$(TargetFramework)' == 'netstandard2.1'">
                    <PackageReference Include="System.Diagnostics.EventLog" Version="6.0.0" />
                  </ItemGroup>
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
                "-p:SelfContained=true -p:PublishSingleFile=true " +
                "-p:IncludeNativeLibrariesForSelfExtract=true " +
                "-p:PortableTrim=false -p:PortableTrimMode=partial");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            RunDotNet(
                root,
                $"build \"{appProject}\" -c Release -f net10.0 -r win-x64 --no-restore --nologo " +
                "-p:SelfContained=true -p:PublishSingleFile=true " +
                "-p:IncludeNativeLibrariesForSelfExtract=true " +
                "-p:PortableTrim=false -p:PortableTrimMode=partial " +
                "-p:UseSharedCompilation=false -nodeReuse:false");
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release",
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
                                Style = DotNetPublishStyle.PortableCompat
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
    [InlineData("App.pdb", false, false, false)]
    [InlineData("App.pdb", true, false, true)]
    [InlineData("App.xml", false, false, false)]
    [InlineData("App.xml", false, true, true)]
    [InlineData("Guide.pdf", false, false, false)]
    [InlineData("Guide.pdf", false, true, true)]
    [InlineData("App.dll", false, false, true)]
    public void IsFinalPublishInputRetained_HonorsFinalLayoutPolicy(
        string path,
        bool keepSymbols,
        bool keepDocs,
        bool expected)
    {
        Assert.Equal(
            expected,
            DotNetPublishPipelineRunner.IsFinalPublishInputRetained(path, keepSymbols, keepDocs));
    }

    [Fact]
    public void ControlledBuildEnvironment_DropsPrivateFeedCredential()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string controlledRoot = Directory.CreateDirectory(Path.Combine(root, "controlled")).FullName;
        try
        {
            Assert.True(DotNetPublishPipelineRunner.TryCreateControlledBuildEnvironment(
                new Dictionary<string, string?>
                {
                    ["NuGetPackageSourceCredentials_PrivateFeed"] = "Username=user;Password=secret"
                },
                root,
                controlledRoot,
                out IReadOnlyDictionary<string, string?> environment));
            Assert.False(environment.ContainsKey("NuGetPackageSourceCredentials_PrivateFeed"));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ShouldRefreshLockedRestoreOutputs_HonorsNoRestorePlan()
    {
        Assert.True(DotNetPublishPipelineRunner.ShouldRefreshLockedRestoreOutputs(null));
        Assert.True(DotNetPublishPipelineRunner.ShouldRefreshLockedRestoreOutputs(
            new DotNetPublishPlan { NoRestoreInPublish = false }));
        Assert.False(DotNetPublishPipelineRunner.ShouldRefreshLockedRestoreOutputs(
            new DotNetPublishPlan { NoRestoreInPublish = true }));
    }

    [Fact]
    public void RemapControlledPublishSourceValue_MapsMetadataBackToOriginalCheckout()
    {
        string controlledRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "controlled-source"));
        string originalRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "original-source"));
        string value = Path.Combine(controlledRoot, "src", "App") + ";unchanged";

        string mapped = DotNetPublishPipelineRunner.RemapControlledPublishSourceValue(
            value,
            controlledRoot,
            originalRoot);

        Assert.Equal(Path.Combine(originalRoot, "src", "App") + ";unchanged", mapped);
    }

    [Fact]
    public void SelectPublishInputSnapshotCandidates_IncludesPackageInputForBuildPublish()
    {
        var generatedInput = new DotNetPublishPipelineRunner.NoBuildPublishInput(
            "evaluation",
            Path.GetFullPath("generated.dll"),
            "generated.dll",
            new Dictionary<string, string>(),
            "AA");
        var packageInput = new DotNetPublishPipelineRunner.NoBuildPublishInput(
            "evaluation",
            Path.GetFullPath("package.dll"),
            "package.dll",
            new Dictionary<string, string>(),
            "BB",
            isPackageBacked: true);

        DotNetPublishPipelineRunner.NoBuildPublishInput selected = Assert.Single(
            DotNetPublishPipelineRunner.SelectPublishInputSnapshotCandidates(
                noBuildInPublish: false,
                [generatedInput, packageInput]));

        Assert.Same(packageInput, selected);
        Assert.Equal(
            2,
            DotNetPublishPipelineRunner.SelectPublishInputSnapshotCandidates(
                noBuildInPublish: true,
                [generatedInput, packageInput]).Length);
    }

    [Fact]
    public void NoBuildPublishSnapshot_RejectsUnattestedUnixMode()
    {
        if (OperatingSystem.IsWindows())
            return;

        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string sourcePath = Path.Combine(root, "apphost");
            byte[] bytes = "controlled-apphost"u8.ToArray();
            File.WriteAllBytes(sourcePath, bytes);
            UnixFileMode actualMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            File.SetUnixFileMode(sourcePath, actualMode);
            int provenMode = (int)(actualMode | UnixFileMode.UserExecute);
            var input = new DotNetPublishPipelineRunner.NoBuildPublishInput(
                "evaluation",
                sourcePath,
                "apphost",
                new Dictionary<string, string>(),
                Convert.ToHexString(SHA256.HashData(bytes)),
                unixFileMode: provenMode);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                DotNetPublishPipelineRunner.NoBuildPublishInputSnapshot.Create([input], null));

            Assert.Contains("Unix mode changed", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledGeneratedOutputEquivalence_RejectsUnixModeMismatch()
    {
        if (OperatingSystem.IsWindows())
            return;

        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string candidatePath = Path.Combine(root, "candidate");
            string controlledPath = Path.Combine(root, "controlled");
            File.WriteAllText(candidatePath, "same-bytes");
            File.WriteAllText(controlledPath, "same-bytes");
            File.SetUnixFileMode(
                candidatePath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.SetUnixFileMode(
                controlledPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            MethodInfo method = typeof(DotNetPublishPipelineRunner).GetMethod(
                "AreControlledGeneratedOutputsEquivalent",
                BindingFlags.NonPublic | BindingFlags.Static)!;

            Assert.False((bool)method.Invoke(null, [candidatePath, controlledPath])!);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}
