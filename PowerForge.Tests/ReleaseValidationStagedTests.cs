using System.Text.Json;

namespace PowerForge.Tests;

public sealed class ReleaseValidationStagedTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.StagedValidation.Tests", Guid.NewGuid().ToString("N"));
    public ReleaseValidationStagedTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void Configuration_dispatch_resolves_release_and_asset_context()
    {
        var spec = new ReleaseValidationSpec { Commands = [new() {
            Name = "Asset context", FileName = "probe", Arguments = ["{Version}", "{ReleaseManifestPath}", "{PackageRoot}", "{ModuleArchive}"]
        }] };
        var context = Context();
        context.ReleaseManifestPath = Path.Combine(_root, "manifest.json");
        context.AssetEntries = [
            new() { Category = PowerForgeReleaseAssetCategory.Package, StagedPath = Path.Combine(_root, "packages", "Example.1.2.3.nupkg") },
            new() { Category = PowerForgeReleaseAssetCategory.Module, StagedPath = Path.Combine(_root, "Example.zip") }
        ];
        ProcessRunRequest? actual = null;
        var runner = new Runner((request, _) => { actual = request; return Task.FromResult(Success()); });

        var result = Run(spec, context, runner);

        Assert.True(result.Succeeded, result.StdErr);
        Assert.Equal("PowerForge", result.Executable);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Shared contract", result.Name);
        Assert.Contains("Asset context", result.StdOut);
        Assert.NotNull(actual);
        Assert.Equal("probe", actual.FileName);
        Assert.Equal(_root, actual.WorkingDirectory);
        Assert.Equal(new[] { "1.2.3", context.ReleaseManifestPath, Path.Combine(_root, "packages"), Path.Combine(_root, "Example.zip") }, actual.Arguments);
    }

    [Fact]
    public void Tools_only_selection_validates_cli_without_requiring_other_lanes()
    {
        var artifact = Path.Combine(_root, "tool.zip");
        File.WriteAllText(artifact, "staged tool");
        var manifest = Path.Combine(_root, "manifest.json");
        var evidence = Path.Combine(_root, "build-evidence.json");
        File.WriteAllText(evidence, "{}");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new { assetEntries = new object[] {
            new { category = "Tool", target = "Example", runtime = "win-x64", style = "Portable", version = "1.2.3", stagedPath = artifact },
            new { category = "Metadata", stagedPath = evidence }
        } }));
        var spec = new ReleaseValidationSpec {
            Packages = new() { Path = "missing packages", Items = [new() { Id = "Missing" }] },
            Modules = [new() { Path = "missing module", Manifest = "Missing.psd1" }],
            Consumers = [new() { SourceDirectory = "missing consumer" }],
            Tools = [new() { PackageId = "Missing.Tool", CommandName = "missing" }],
            CliArtifacts = new() { ManifestPath = "{ReleaseManifestPath}", Target = "Example", Runtimes = ["win-x64"], Styles = ["Portable"], ToolsOnly = true }
        };
        var context = Context();
        context.ModuleSelected = false;
        context.PackagesSelected = false;
        context.ToolsSelected = true;
        context.ReleaseManifestPath = manifest;
        context.StagingRoot = _root;

        var result = Run(spec, context, new Runner((_, _) => throw new InvalidOperationException("Unexpected process")));

        Assert.True(result.Succeeded, result.StdErr);
        Assert.Equal("CLI Example: 1 artifacts", result.StdOut);
    }

    [Fact]
    public void Deselected_cli_lane_does_not_load_its_manifest()
    {
        var context = Context();
        context.ToolsSelected = false;
        var spec = new ReleaseValidationSpec {
            CliArtifacts = new() { ManifestPath = "missing manifest" },
            Commands = [new() { Name = "Product probe", FileName = "probe", ExpectedOutput = "ok" }]
        };

        var result = Run(spec, context, new Runner((_, _) => Task.FromResult(Success())));

        Assert.True(result.Succeeded, result.StdErr);
        Assert.Equal("Product probe", result.StdOut);
    }

    [Fact]
    public void Configuration_timeout_is_reported_as_timeout()
    {
        var runner = new Runner(async (_, token) => {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return Success();
        });

        var result = Run(new() { Commands = [new() { FileName = "probe" }] }, Context(), runner, timeout: 1);

        Assert.False(result.Succeeded);
        Assert.True(result.TimedOut);
        Assert.Equal(-1, result.ExitCode);
    }

    [Fact]
    public void Configuration_caller_cancellation_is_not_reported_as_timeout()
    {
        using var cancellation = new CancellationTokenSource();
        var runner = new Runner((_, _) => { cancellation.Cancel(); return Task.FromResult(Success()); });

        Assert.ThrowsAny<OperationCanceledException>(() => Run(new() { Commands = [new() { FileName = "probe" }] },
            Context(), runner, cancellationToken: cancellation.Token));
    }

    [Fact]
    public void Script_process_receives_action_environment_and_context_is_cleaned_on_cancellation()
    {
        var script = Path.Combine(_root, "probe.ps1");
        File.WriteAllText(script, "'ok'");
        using var cancellation = new CancellationTokenSource();
        string? contextPath = null;
        var runner = new Runner((request, _) => {
            Assert.Equal("custom", request.EnvironmentVariables!["CUSTOM"]);
            contextPath = request.EnvironmentVariables["POWERFORGE_CONTEXT"];
            using var document = JsonDocument.Parse(File.ReadAllText(contextPath!));
            Assert.Equal("1.2.3", document.RootElement.GetProperty("ResolvedVersion").GetString());
            Assert.Equal(TimeSpan.FromSeconds(7), request.Timeout);
            cancellation.Cancel();
            return Task.FromResult(Success());
        });

        Assert.ThrowsAny<OperationCanceledException>(() => new PowerForgeReleaseValidationService(new NullLogger(), runner).Run(
            new() { FilePath = script, TimeoutSeconds = 7, Environment = new() { ["CUSTOM"] = "custom" } },
            Context(), _root, cancellation.Token));

        Assert.NotNull(contextPath);
        Assert.False(File.Exists(contextPath));
        Assert.False(Directory.Exists(Path.GetDirectoryName(contextPath)));
        Assert.True(File.Exists(script));
    }

    private PowerForgeReleaseValidationContext Context() => new() {
        ProjectRoot = _root, ResolvedVersion = "1.2.3", ConfigPath = Path.Combine(_root, "release.json")
    };

    private PowerForgeReleaseValidationResult Run(ReleaseValidationSpec spec, PowerForgeReleaseValidationContext context,
        IProcessRunner runner, int timeout = 30, CancellationToken cancellationToken = default)
    {
        File.WriteAllText(Path.Combine(_root, "validation.json"), ReleaseValidationService.Serialize(spec));
        return new PowerForgeReleaseValidationService(new NullLogger(), runner).Run(
            new() { Name = "Shared contract", ConfigPath = "validation.json", TimeoutSeconds = timeout }, context, _root, cancellationToken);
    }

    private static ProcessRunResult Success() => new(0, "ok", "", "probe", TimeSpan.Zero, false);
    private sealed class Runner(Func<ProcessRunRequest, CancellationToken, Task<ProcessRunResult>> execute) : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default) => execute(request, cancellationToken);
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
