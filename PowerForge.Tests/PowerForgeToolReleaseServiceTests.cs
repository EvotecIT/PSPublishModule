using System.Diagnostics;
using System.Text;

namespace PowerForge.Tests;

public sealed class PowerForgeToolReleaseServiceTests
{
    [Fact]
    public void Plan_AppliesSharedReleaseVersionToLegacyToolOutputsAndMsBuild()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.ToolReleaseTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var projectPath = Path.Combine(root, "Sample.Tool.csproj");
            File.WriteAllText(projectPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Version>1.2.3</Version>
  </PropertyGroup>
</Project>
""", new UTF8Encoding(false));

            var service = new PowerForgeToolReleaseService(new NullLogger());
            var plan = service.Plan(
                new PowerForgeToolReleaseSpec
                {
                    ProjectRoot = root,
                    Targets = new[]
                    {
                        new PowerForgeToolReleaseTarget
                        {
                            Name = "Sample.Tool",
                            ProjectPath = "Sample.Tool.csproj",
                            Frameworks = new[] { "net10.0" },
                            Runtimes = new[] { "win-x64" },
                            OutputPath = "artifacts/{version}/{rid}"
                        }
                    }
                },
                Path.Combine(root, "release.json"),
                new PowerForgeReleaseRequest { ResolvedReleaseVersion = "3.1.0-preview.2" });

            var target = Assert.Single(plan.Targets);
            Assert.Equal("3.1.0-preview.2", target.Version);
            Assert.Equal("3.1.0-preview.2", target.MsBuildProperties["Version"]);
            Assert.Equal("3.1.0", target.MsBuildProperties["VersionPrefix"]);
            Assert.Contains(Path.Combine("artifacts", "3.1.0-preview.2", "win-x64"), Assert.Single(target.Combinations).OutputPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_UsesAnIsolatedNuGetLockFileForRuntimePublishes()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.ToolReleaseTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var projectPath = Path.Combine(root, "Sample.Tool.csproj");
            File.WriteAllText(projectPath, """
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Version>1.2.3</Version>
  </PropertyGroup>
</Project>
""", new UTF8Encoding(false));

            ProcessStartInfo? captured = null;
            var service = new PowerForgeToolReleaseService(
                new NullLogger(),
                startInfo =>
                {
                    captured = startInfo;
                    var publishDirectory = ReadProperty(startInfo.Arguments, "/p:PublishDir=");
                    Directory.CreateDirectory(publishDirectory);
                    File.WriteAllText(Path.Combine(publishDirectory, "Sample.Tool"), "tool");
                    return new PowerForgeToolReleaseService.ProcessExecutionResult(0, string.Empty, string.Empty);
                });
            var outputPath = Path.Combine(root, "output");
            var progress = new RecordingReleaseProgress();
            var result = service.Run(new PowerForgeToolReleasePlan
            {
                ProjectRoot = root,
                Configuration = "Release",
                Targets = new[]
                {
                    new PowerForgeToolReleaseTargetPlan
                    {
                        Name = "Sample.Tool",
                        ProjectPath = projectPath,
                        OutputName = "Sample.Tool",
                        Version = "1.2.3",
                        ArtifactRootPath = outputPath,
                        KeepDocs = true,
                        KeepSymbols = true,
                        MsBuildProperties = new Dictionary<string, string>(),
                        Combinations = new[]
                        {
                            new PowerForgeToolReleaseCombinationPlan
                            {
                                Runtime = "osx-arm64",
                                Framework = "net10.0",
                                Flavor = PowerForgeToolReleaseFlavor.SingleContained,
                                OutputPath = outputPath
                            }
                        }
                    }
                }
            }, progress);

            Assert.True(result.Success, result.ErrorMessage);
            var progressItem = Assert.Single(progress.Planned);
            Assert.Contains("Sample.Tool", progressItem.Title, StringComparison.Ordinal);
            Assert.Equal(
                new[]
                {
                    PowerForgeReleaseProgressItemState.Started,
                    PowerForgeReleaseProgressItemState.Completed
                },
                progress.Updates);
            Assert.NotNull(captured);
            Assert.Contains("/p:RestoreLockedMode=false", captured!.Arguments, StringComparison.Ordinal);
            Assert.Contains(
                "/p:NuGetLockFilePath=obj/PowerForge.ToolRelease.packages.lock.json",
                captured.Arguments,
                StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_DisablesSingleFileCompressionForFrameworkDependentTools()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.ToolReleaseTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var projectPath = Path.Combine(root, "Sample.Tool.csproj");
            File.WriteAllText(projectPath, "<Project />");
            ProcessStartInfo? captured = null;
            var service = new PowerForgeToolReleaseService(
                new NullLogger(),
                startInfo =>
                {
                    captured = startInfo;
                    var publishDirectory = ReadProperty(startInfo.Arguments, "/p:PublishDir=");
                    Directory.CreateDirectory(publishDirectory);
                    File.WriteAllText(Path.Combine(publishDirectory, "Sample.Tool.exe"), "tool");
                    return new PowerForgeToolReleaseService.ProcessExecutionResult(0, string.Empty, string.Empty);
                });
            var outputPath = Path.Combine(root, "output");

            var result = service.Run(new PowerForgeToolReleasePlan
            {
                ProjectRoot = root,
                Configuration = "Release",
                Targets =
                [
                    new PowerForgeToolReleaseTargetPlan
                    {
                        Name = "Sample.Tool",
                        ProjectPath = projectPath,
                        OutputName = "Sample.Tool",
                        Version = "1.2.3",
                        ArtifactRootPath = outputPath,
                        KeepDocs = true,
                        KeepSymbols = true,
                        MsBuildProperties = new Dictionary<string, string>(),
                        Combinations =
                        [
                            new PowerForgeToolReleaseCombinationPlan
                            {
                                Runtime = "win-x64",
                                Framework = "net10.0",
                                Flavor = PowerForgeToolReleaseFlavor.SingleFx,
                                OutputPath = outputPath
                            }
                        ]
                    }
                ]
            });

            Assert.True(result.Success, result.ErrorMessage);
            Assert.NotNull(captured);
            Assert.Contains("--self-contained:false", captured!.Arguments, StringComparison.Ordinal);
            Assert.Contains("/p:PublishSingleFile=true", captured.Arguments, StringComparison.Ordinal);
            Assert.Contains("/p:EnableCompressionInSingleFile=false", captured.Arguments, StringComparison.Ordinal);
            Assert.Contains("/p:IncludeNativeLibrariesForSelfExtract=false", captured.Arguments, StringComparison.Ordinal);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Run_AllowsDuplicateTargetNamesWithoutProgressKeyCollisions()
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.ToolReleaseTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var projectPath = Path.Combine(root, "Sample.Tool.csproj");
            File.WriteAllText(projectPath, "<Project />");
            var service = new PowerForgeToolReleaseService(
                new NullLogger(),
                startInfo =>
                {
                    var publishDirectory = ReadProperty(startInfo.Arguments, "/p:PublishDir=");
                    Directory.CreateDirectory(publishDirectory);
                    File.WriteAllText(Path.Combine(publishDirectory, "Sample.Tool"), "tool");
                    return new PowerForgeToolReleaseService.ProcessExecutionResult(0, string.Empty, string.Empty);
                });
            var progress = new RecordingReleaseProgress();
            var targets = Enumerable.Range(0, 2)
                .Select(index =>
                {
                    var outputPath = Path.Combine(root, $"output-{index}");
                    return new PowerForgeToolReleaseTargetPlan
                    {
                        Name = "Sample.Tool",
                        ProjectPath = projectPath,
                        OutputName = "Sample.Tool",
                        Version = "1.2.3",
                        ArtifactRootPath = outputPath,
                        KeepDocs = true,
                        KeepSymbols = true,
                        MsBuildProperties = new Dictionary<string, string>(),
                        Combinations =
                        [
                            new PowerForgeToolReleaseCombinationPlan
                            {
                                Runtime = "osx-arm64",
                                Framework = "net10.0",
                                Flavor = PowerForgeToolReleaseFlavor.SingleContained,
                                OutputPath = outputPath
                            }
                        ]
                    };
                })
                .ToArray();

            var result = service.Run(new PowerForgeToolReleasePlan
            {
                ProjectRoot = root,
                Configuration = "Release",
                Targets = targets
            }, progress);

            Assert.True(result.Success, result.ErrorMessage);
            Assert.Equal(2, progress.Planned.Count);
            Assert.Equal(2, progress.Planned.Select(item => item.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal(4, progress.Updates.Count);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void RunProcess_cancellation_terminates_the_child_process()
    {
        var startInfo = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd.exe", "/d /s /c \"ping -n 31 127.0.0.1 > nul\"")
            : new ProcessStartInfo("/bin/sh", "-c \"sleep 30\"");
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(250));
        var stopwatch = Stopwatch.StartNew();

        Assert.ThrowsAny<OperationCanceledException>(
            () => PowerForgeToolReleaseService.RunProcess(startInfo, cancellation.Token));

        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(5),
            $"Cancellation took {stopwatch.Elapsed}.");
    }

    private sealed class RecordingReleaseProgress : IPowerForgeReleaseProgressReporterV2
    {
        public List<PowerForgeReleaseProgressItem> Planned { get; } = new();

        public List<PowerForgeReleaseProgressItemState> Updates { get; } = new();

        public void PhaseStarted(PowerForgeReleaseProgressPhase phase, int totalItems, string? detail = null) { }

        public void PhaseCompleted(PowerForgeReleaseProgressPhase phase, string? detail = null) { }

        public void PhaseFailed(PowerForgeReleaseProgressPhase phase, string? detail = null) { }

        public void ItemsPlanned(
            PowerForgeReleaseProgressPhase phase,
            IReadOnlyList<PowerForgeReleaseProgressItem> items)
            => Planned.AddRange(items);

        public void ItemUpdated(
            PowerForgeReleaseProgressItem item,
            PowerForgeReleaseProgressItemState state,
            string? detail = null)
            => Updates.Add(state);
    }

    private static string ReadProperty(string arguments, string prefix)
    {
        var start = arguments.IndexOf(prefix, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Property '{prefix}' was not present in '{arguments}'.");
        start += prefix.Length;
        if (arguments[start] == '"')
        {
            var endQuote = arguments.IndexOf('"', start + 1);
            Assert.True(endQuote > start);
            return arguments.Substring(start + 1, endQuote - start - 1);
        }

        var end = arguments.IndexOf(' ', start);
        return end < 0 ? arguments.Substring(start) : arguments.Substring(start, end - start);
    }
}
