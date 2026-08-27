using PowerForge;
using System.Security.Cryptography;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void ReadSourceProvenance_NoBuildPublishAcceptsProjectReferenceWithIgnoredOutputs()
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
                    <TargetFramework>net8.0</TargetFramework>
                  </PropertyGroup>
                  <ItemGroup><ProjectReference Include="../Library/Library.csproj" /></ItemGroup>
                </Project>
                """);
            File.WriteAllText(libraryProject, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                </Project>
                """);
            File.WriteAllText(
                Path.Combine(appDirectory, "Program.cs"),
                "internal static class Program { private static void Main() { _ = Library.Value; } }");
            File.WriteAllText(
                Path.Combine(libraryDirectory, "Library.cs"),
                "public static class Library { public const int Value = 1; }");
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            RunDotNet(root, $"build \"{appProject}\" -c Release --no-restore --nologo");

            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release",
                NoBuildInPublish = true,
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

            DotNetPublishPipelineRunner.SourceProvenance provenance =
                DotNetPublishPipelineRunner.ReadSourceProvenance(root, buildPlan: plan);

            Assert.False(provenance.Dirty, string.Join(Environment.NewLine, provenance.DirtyReasons));
            Assert.Empty(provenance.DirtyPaths);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void DotNetExecutionEnvironment_ClearsAmbientRuntimeInjectionVariables()
    {
        IReadOnlyDictionary<string, string?> environment =
            DotNetPublishPipelineRunner.CreateSafeDotNetChildEnvironment(
                new Dictionary<string, string?>
                {
                    ["PATH"] = "controlled-path",
                    ["PRODUCT_MODE"] = "release",
                    ["DOTNET_ROOT"] = "untrusted-root"
                },
                [
                    "DOTNET_STARTUP_HOOKS",
                    "DOTNET_GCPath",
                    "CORECLR_ENABLE_PROFILING",
                    "MSBUILD_EXE_PATH",
                    "MSBuildUserExtensionsPath",
                    "UNRELATED_AMBIENT_VALUE"
                ]);

        Assert.Equal("controlled-path", environment["PATH"]);
        Assert.Equal("release", environment["PRODUCT_MODE"]);
        Assert.Null(environment["DOTNET_STARTUP_HOOKS"]);
        Assert.Null(environment["DOTNET_GCPath"]);
        Assert.Null(environment["CORECLR_ENABLE_PROFILING"]);
        Assert.Null(environment["MSBUILD_EXE_PATH"]);
        Assert.Null(environment["MSBuildUserExtensionsPath"]);
        Assert.Null(environment["UNRELATED_AMBIENT_VALUE"]);
        Assert.Equal(
            Path.GetDirectoryName(DotNetPublishPipelineRunner.ResolveRunDotNetExecutablePath()),
            environment["DOTNET_ROOT"],
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        Assert.Null(environment["DOTNET_ROOT(x86)"]);
        Assert.Equal("0", environment["DOTNET_MULTILEVEL_LOOKUP"]);
        Assert.Equal("false", environment["ImportUserLocationsByWildcardBeforeMicrosoftCommonProps"]);
        Assert.Equal("false", environment["ImportUserLocationsByWildcardAfterMicrosoftCommonTargets"]);
    }

    [Theory]
    [InlineData("DOTNET_STARTUP_HOOKS")]
    [InlineData("CORECLR_PROFILER")]
    [InlineData("DOTNET_GCName")]
    [InlineData("DOTNET_GCPath")]
    [InlineData("DOTNET_JitName")]
    [InlineData("DOTNET_ROOT_X64")]
    [InlineData("MSBUILD_EXE_PATH")]
    [InlineData("MSBuildUserExtensionsPath")]
    [InlineData("CustomBeforeMicrosoftCommonProps")]
    [InlineData("CustomAfterMicrosoftCommonTargets")]
    [InlineData("MSBuildExtensionsPath")]
    [InlineData("NUGET_PLUGIN_PATHS")]
    public void DotNetExecutionEnvironment_RejectsExplicitRuntimeInjectionVariable(string name)
    {
        var configured = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [name] = "untrusted"
        };

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            DotNetPublishPipelineRunner.CreateSafeDotNetChildEnvironment(
                configured,
                Array.Empty<string>()));

        Assert.Contains(name, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DotNetExecution_RejectsUnsignedConfiguredToolchain()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string? previous = Environment.GetEnvironmentVariable("POWERFORGE_DOTNET_PATH");
        try
        {
            string configuredPath = Path.Combine(
                root,
                OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            File.WriteAllText(configuredPath, "untrusted executable");
            Directory.CreateDirectory(Path.Combine(root, "host", "fxr"));
            Directory.CreateDirectory(Path.Combine(root, "shared", "Microsoft.NETCore.App"));
            Directory.CreateDirectory(Path.Combine(root, "sdk"));
            Environment.SetEnvironmentVariable("POWERFORGE_DOTNET_PATH", configuredPath);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                DotNetPublishPipelineRunner.ResolveRunDotNetExecutablePath);

            Assert.Contains("trusted dotnet installation", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("POWERFORGE_DOTNET_PATH", previous);
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void DotNetExecution_RejectsExecutableChangedAfterAdmission()
    {
        string resolved = DotNetPublishPipelineRunner.ResolveRunDotNetExecutablePath();
        string actualSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(resolved)));

        DotNetPublishPipelineRunner.ValidateDotNetExecutableSnapshot(resolved, actualSha256);
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            DotNetPublishPipelineRunner.ValidateDotNetExecutableSnapshot(
                resolved,
                new string('0', actualSha256.Length)));

        Assert.Contains("changed after admission", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DotNetExecution_UsesResolvedAbsoluteToolInsteadOfPlanPathLookup()
    {
        string resolved = DotNetPublishPipelineRunner.ResolveRunDotNetExecutablePath();

        Assert.True(Path.IsPathRooted(resolved));
        Assert.Equal(
            resolved,
            DotNetPublishPipelineRunner.ResolveDotNetChildExecutable("dotnet"),
            OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        Assert.Equal("msbuild.exe", DotNetPublishPipelineRunner.ResolveDotNetChildExecutable("msbuild.exe"));
    }

    [Fact]
    public void Run_UsesAttestedDotNetPathWhenPlanOverridesPath()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            RunGit(root, "init");
            RunGit(root, "config user.name \"PowerForge Tests\"");
            RunGit(root, "config user.email \"powerforge-tests@example.invalid\"");
            string projectPath = Path.Combine(root, "App.csproj");
            File.WriteAllText(
                projectPath,
                "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            ProcessRunRequest? captured = null;
            var runner = new DotNetPublishPipelineRunner(
                new NullLogger(),
                new RecordingProcessRunner(request =>
                {
                    captured = request;
                    return new ProcessRunResult(
                        0,
                        string.Empty,
                        string.Empty,
                        request.FileName,
                        TimeSpan.Zero,
                        timedOut: false);
                }));
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Configuration = "Release",
                EnvironmentVariables = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["PATH"] = Path.Combine(root, "untrusted-bin")
                },
                Targets =
                [
                    new DotNetPublishTargetPlan
                    {
                        Name = "App",
                        ProjectPath = projectPath
                    }
                ],
                Steps =
                [
                    new DotNetPublishStep
                    {
                        Key = "build",
                        Kind = DotNetPublishStepKind.Build,
                        Title = "Build"
                    }
                ]
            };

            DotNetPublishResult result = runner.Run(plan, progress: null);

            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.NotNull(captured);
            Assert.True(Path.IsPathRooted(captured!.FileName));
            Assert.Equal(
                DotNetPublishPipelineRunner.ResolveRunDotNetExecutablePath(),
                captured.FileName,
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            Assert.Equal(Path.Combine(root, "untrusted-bin"), captured.EnvironmentVariables!["PATH"]);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }
}
