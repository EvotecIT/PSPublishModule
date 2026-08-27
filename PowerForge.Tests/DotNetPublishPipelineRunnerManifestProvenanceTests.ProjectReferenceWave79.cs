using System.Security.Cryptography;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public sealed partial class DotNetPublishPipelineRunnerManifestProvenanceTests
{
    [Fact]
    public void Run_BenchmarkOnlyPlanDoesNotResolveBuildToolchains()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        string? previousDotNet = Environment.GetEnvironmentVariable("POWERFORGE_DOTNET_PATH");
        string? previousGit = Environment.GetEnvironmentVariable("POWERFORGE_GIT_PATH");
        try
        {
            string sourcePath = Path.Combine(root, "bench.log");
            string baselinePath = Path.Combine(root, "baseline.json");
            File.WriteAllText(sourcePath, "elapsed=10ms");
            File.WriteAllText(baselinePath, "{\"metrics\":{\"Elapsed\":10.0}}");
            Environment.SetEnvironmentVariable("POWERFORGE_DOTNET_PATH", Path.Combine(root, "missing-dotnet"));
            Environment.SetEnvironmentVariable("POWERFORGE_GIT_PATH", Path.Combine(root, "missing-git"));
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                BenchmarkGates =
                [
                    new DotNetPublishBenchmarkGatePlan
                    {
                        Id = "standalone",
                        Enabled = true,
                        SourcePath = sourcePath,
                        BaselinePath = baselinePath,
                        BaselineMode = DotNetPublishBaselineMode.Verify,
                        Metrics =
                        [
                            new DotNetPublishBenchmarkMetricPlan
                            {
                                Name = "Elapsed",
                                Source = DotNetPublishBenchmarkMetricSource.Regex,
                                Pattern = "elapsed=([0-9]+)ms",
                                Group = 1
                            }
                        ]
                    }
                ],
                Steps =
                [
                    new DotNetPublishStep
                    {
                        Kind = DotNetPublishStepKind.BenchmarkExtract,
                        GateId = "standalone"
                    },
                    new DotNetPublishStep
                    {
                        Kind = DotNetPublishStepKind.BenchmarkGate,
                        GateId = "standalone"
                    }
                ]
            };

            DotNetPublishResult result = new DotNetPublishPipelineRunner(new NullLogger()).Run(plan, progress: null);

            Assert.True(result.Succeeded, result.ErrorMessage);
        }
        finally
        {
            Environment.SetEnvironmentVariable("POWERFORGE_DOTNET_PATH", previousDotNet);
            Environment.SetEnvironmentVariable("POWERFORGE_GIT_PATH", previousGit);
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void NativeAotEnvironment_ReplacesInheritedPathForRealDotNetChild()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
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
                Targets =
                [
                    new DotNetPublishTargetPlan
                    {
                        Name = "App",
                        ProjectPath = Path.Combine(root, "App.csproj"),
                        Combinations =
                        [
                            new DotNetPublishTargetCombination
                            {
                                Framework = "net8.0",
                                Runtime = "win-x64",
                                Style = DotNetPublishStyle.AotSpeed
                            }
                        ]
                    }
                ],
                Steps =
                [
                    new DotNetPublishStep
                    {
                        Kind = DotNetPublishStepKind.Build,
                        Title = "Build"
                    }
                ]
            };

            DotNetPublishResult result = runner.Run(plan, progress: null);

            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.NotNull(captured);
            Assert.Equal(
                Path.GetDirectoryName(DotNetPublishPipelineRunner.ResolveRunDotNetExecutablePath()),
                captured!.EnvironmentVariables!["PATH"],
                OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_RejectOnErrorDestinationWithExec()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(
                projectPath,
                "<Project><Target Name=\"Build\"><Error Text=\"fail\" /><OnError ExecuteTargets=\"Recovery\" /></Target><Target Name=\"Recovery\"><Exec Command=\"reachable\" /></Target></Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root, [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void ControlledBuildInputs_FailClosedForUnresolvedOnErrorDestination()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string projectPath = Path.Combine(root, "App.proj");
            File.WriteAllText(
                projectPath,
                "<Project><Target Name=\"Build\"><OnError ExecuteTargets=\"$(UnresolvedRecovery)\" /></Target></Project>");

            Assert.False(DotNetPublishPipelineRunner.HasOnlyControlledBuildFileInputs(root, [projectPath]));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void NoBuildPublishSnapshot_PreservesProvenBytesAfterOriginalReplacement()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string sourcePath = Path.Combine(root, "Library.dll");
            byte[] provenBytes = "controlled-library"u8.ToArray();
            File.WriteAllBytes(sourcePath, provenBytes);
            string sha256 = Convert.ToHexString(SHA256.HashData(provenBytes));
            var input = new DotNetPublishPipelineRunner.NoBuildPublishInput(
                "evaluation",
                sourcePath,
                "Library.dll",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["RelativePath"] = "Library.dll",
                    ["CopyToPublishDirectory"] = "PreserveNewest"
                },
                sha256);

            using DotNetPublishPipelineRunner.NoBuildPublishInputSnapshot snapshot =
                DotNetPublishPipelineRunner.NoBuildPublishInputSnapshot.Create([input], existingCustomAfterTargets: null);
            File.WriteAllText(sourcePath, "replacement-library");
            snapshot.ValidateUnchanged();
            string targets = File.ReadAllText(snapshot.TargetsPath);
            string snapshotPath = Directory.GetFiles(
                Path.Combine(Path.GetDirectoryName(snapshot.TargetsPath)!, "inputs"),
                "*.dll").Single();

            Assert.Equal(provenBytes, File.ReadAllBytes(snapshotPath));
            Assert.Contains("AfterTargets=\"ComputeFilesToPublish\"", targets, StringComparison.Ordinal);
            Assert.Contains(snapshotPath, targets, StringComparison.Ordinal);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void NoBuildPublishSnapshot_RejectsInputChangedAfterControlledProof()
    {
        string root = Directory.CreateTempSubdirectory().FullName;
        try
        {
            string sourcePath = Path.Combine(root, "Library.dll");
            File.WriteAllText(sourcePath, "replacement-library");
            string provenSha256 = Convert.ToHexString(SHA256.HashData("controlled-library"u8.ToArray()));
            var input = new DotNetPublishPipelineRunner.NoBuildPublishInput(
                "evaluation",
                sourcePath,
                "Library.dll",
                new Dictionary<string, string>(),
                provenSha256);

            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                DotNetPublishPipelineRunner.NoBuildPublishInputSnapshot.Create(
                    [input],
                    existingCustomAfterTargets: null));

            Assert.Contains("changed after controlled proof", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    [Fact]
    public void Run_NoBuildPublishConsumesProvenSnapshotWhenProjectReferenceOutputIsReplaced()
    {
        if (!OperatingSystem.IsWindows())
            return;

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
            string outputDirectory = Path.Combine(root, "publish");
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
            File.WriteAllText(Path.Combine(root, ".gitignore"), "bin/\nobj/\npublish/\n");
            RunDotNet(root, $"restore \"{appProject}\" --use-lock-file --nologo");
            RunGit(root, "add .");
            RunGit(root, "commit -m \"approved source\"");
            string revision = RunGit(root, "rev-parse HEAD").Trim();
            RunDotNet(
                root,
                $"build \"{appProject}\" -c Release -f net8.0 --no-restore --nologo " +
                $"/p:SourceRevisionId={revision} " +
                "/p:IncludeSourceRevisionInInformationalVersion=true");
            string libraryOutput = Path.Combine(libraryDirectory, "bin", "Release", "net8.0", "Library.dll");
            Assert.True(File.Exists(libraryOutput), libraryOutput);
            byte[] provenBytes = File.ReadAllBytes(libraryOutput);
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
                        Publish = new DotNetPublishPublishOptions
                        {
                            OutputPath = outputDirectory,
                            Style = DotNetPublishStyle.FrameworkDependent
                        },
                        Combinations =
                        [
                            new DotNetPublishTargetCombination
                            {
                                Framework = "net8.0",
                                Runtime = string.Empty,
                                Style = DotNetPublishStyle.FrameworkDependent
                            }
                        ]
                    }
                ],
                Steps =
                [
                    new DotNetPublishStep
                    {
                        Kind = DotNetPublishStepKind.Publish,
                        TargetName = "App",
                        Framework = "net8.0",
                        Runtime = string.Empty,
                        Style = DotNetPublishStyle.FrameworkDependent
                    }
                ]
            };
            var runner = new DotNetPublishPipelineRunner(
                new NullLogger(),
                new RestoringProjectReferenceOutputRunner(libraryOutput, provenBytes));

            DotNetPublishResult result = runner.Run(plan, progress: null);

            Assert.True(result.Succeeded, result.ErrorMessage);
            Assert.Equal(provenBytes, File.ReadAllBytes(Path.Combine(outputDirectory, "Library.dll")));
            Assert.Equal(provenBytes, File.ReadAllBytes(libraryOutput));
        }
        finally
        {
            DeleteTestRepository(root);
        }
    }

    private sealed class RestoringProjectReferenceOutputRunner(
        string projectReferenceOutput,
        byte[] provenBytes) : IProcessRunner
    {
        private readonly ProcessRunner _inner = new();

        public async Task<ProcessRunResult> RunAsync(
            ProcessRunRequest request,
            CancellationToken cancellationToken = default)
        {
            File.WriteAllText(projectReferenceOutput, "unproven replacement bytes");
            try
            {
                return await _inner.RunAsync(request, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                File.WriteAllBytes(projectReferenceOutput, provenBytes);
            }
        }
    }
}
