namespace PowerForge.Tests;

public sealed class ReleaseValidationDeselectionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.Deselection.Tests", Guid.NewGuid().ToString("N"));
    public ReleaseValidationDeselectionTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("Modules")]
    [InlineData("Packages")]
    [InlineData("Consumers")]
    [InlineData("Tools")]
    [InlineData("CliArtifacts")]
    public void Configured_but_deselected_lane_returns_explicit_successful_noop(string lane)
    {
        var runner = new RecordingRunner();

        var result = Run(SpecFor(lane), runner, deselectedLane: lane);

        Assert.True(result.Succeeded, result.StdErr);
        Assert.Equal(0, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Equal("Selection contract", result.Name);
        Assert.Equal("PowerForge", result.Executable);
        Assert.Equal(string.Empty, result.StdErr);
        Assert.True(result.StdOut.Contains("skip", StringComparison.OrdinalIgnoreCase) ||
            result.StdOut.Contains("not applicable", StringComparison.OrdinalIgnoreCase),
            $"No-op evidence must explicitly explain selection: {result.StdOut}");
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public void Originally_empty_configuration_is_not_a_successful_deselection()
    {
        var runner = new RecordingRunner();

        var result = Run(new(), runner);

        Assert.False(result.Succeeded);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("at least one", result.StdErr, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(runner.Requests);
    }

    [Fact]
    public void Unsupported_schema_is_rejected_even_when_every_configured_lane_is_deselected()
    {
        var spec = SpecFor("Modules");
        spec.SchemaVersion = 2;
        var runner = new RecordingRunner();

        var result = Run(spec, runner);

        Assert.False(result.Succeeded);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("schema", result.StdErr, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(runner.Requests);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(19)]
    public void Commands_remain_active_when_artifact_lanes_are_deselected(int commandExitCode)
    {
        var spec = SpecFor("Modules");
        spec.Commands = [new() { Name = "Retained command", FileName = "probe", Arguments = ["{Version}"], ExpectedOutput = "observed" }];
        var runner = new RecordingRunner(commandExitCode);

        var result = Run(spec, runner);

        Assert.Equal(commandExitCode == 0, result.Succeeded);
        Assert.Equal(commandExitCode == 0 ? 0 : 1, result.ExitCode);
        var request = Assert.Single(runner.Requests);
        Assert.Equal("probe", request.FileName);
        Assert.Equal(new[] { "1.2.3" }, request.Arguments);
        if (commandExitCode == 0) Assert.Equal("Retained command", result.StdOut);
        else Assert.Contains("Retained command failed", result.StdErr, StringComparison.Ordinal);
    }

    [Fact]
    public void Caller_cancellation_is_not_hidden_by_a_deselected_noop()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var runner = new RecordingRunner();

        Assert.ThrowsAny<OperationCanceledException>(() => Run(SpecFor("CliArtifacts"), runner, cancellation.Token));

        Assert.Empty(runner.Requests);
    }

    private static ReleaseValidationSpec SpecFor(string lane) => lane switch {
        "Modules" => new() { Modules = [new() { Path = "not-staged-module", Manifest = "Example.psd1" }] },
        "Packages" => new() { Packages = new() { Path = "not-staged-packages", Items = [new() { Id = "Example" }] } },
        "Consumers" => new() { Consumers = [new() { SourceDirectory = "not-staged-consumer", ProjectFile = "Probe.csproj", Frameworks = ["net10.0"] }] },
        "Tools" => new() { Tools = [new() { PackageRoot = "not-staged-tools", PackageId = "Example.Tool", CommandName = "example" }] },
        "CliArtifacts" => new() { CliArtifacts = new() { ManifestPath = "not-staged-manifest.json", Target = "app", Runtimes = ["win-x64"], Styles = ["Portable"] } },
        _ => throw new ArgumentOutOfRangeException(nameof(lane))
    };

    private PowerForgeReleaseValidationResult Run(ReleaseValidationSpec spec, IProcessRunner runner, CancellationToken cancellationToken = default,
        string? deselectedLane = null)
    {
        var configPath = Path.Combine(_root, "validation.json");
        File.WriteAllText(configPath, ReleaseValidationService.Serialize(spec));
        return new PowerForgeReleaseValidationService(new NullLogger(), runner).Run(
            new() { Name = "Selection contract", ConfigPath = configPath },
            new() { ProjectRoot = _root, ResolvedVersion = "1.2.3", ConfigPath = Path.Combine(_root, "release.json"),
                ModuleSelected = deselectedLane is not null && deselectedLane != "Modules",
                PackagesSelected = deselectedLane is not null && deselectedLane is not ("Packages" or "Consumers" or "Tools"),
                ToolsSelected = deselectedLane is not null && deselectedLane != "CliArtifacts" },
            _root, cancellationToken);
    }

    private sealed class RecordingRunner(int exitCode = 0) : IProcessRunner
    {
        internal List<ProcessRunRequest> Requests { get; } = [];
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ProcessRunResult(exitCode, "observed", "", "probe", TimeSpan.Zero, false));
        }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
