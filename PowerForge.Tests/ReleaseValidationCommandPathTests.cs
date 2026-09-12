using System.IO.Compression;

namespace PowerForge.Tests;

public sealed class ReleaseValidationCommandPathTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.CommandPaths.Tests", Guid.NewGuid().ToString("N"));
    public ReleaseValidationCommandPathTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancelled_command_is_not_reported_as_a_successful_platform_skip(bool completeRun)
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var runner = new Runner();
        var service = new ReleaseValidationService(runner);
        var command = new ReleaseCommandValidation { FileName = "probe",
            Platforms = [OperatingSystem.IsWindows() ? "Linux" : "Windows"] };
        var error = completeRun
            ? await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RunAsync(new() { Commands = [command] }, cancellationToken: cancelled.Token))
            : await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RunCommandAsync(command, cancellationToken: cancelled.Token));
        Assert.Equal(cancelled.Token, error.CancellationToken);
        Assert.Empty(runner.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Relative_standalone_root_is_normalized_once_without_changing_caller_variables(bool explicitWorkingDirectory)
    {
        var variables = new Dictionary<string, string> { ["projectroot"] = "fixtures", ["Probe"] = "probe" };
        var original = variables.ToArray();
        var runner = new Runner();
        var command = new ReleaseCommandValidation { FileName = "{ProjectRoot}/{Probe}",
            WorkingDirectory = explicitWorkingDirectory ? "child" : null };
        await new ReleaseValidationService(runner).RunCommandAsync(command, variables);
        var request = Assert.Single(runner.Requests);
        var expectedRoot = Path.GetFullPath("fixtures");
        Assert.Equal(Path.Combine(expectedRoot, "probe"), request.FileName);
        Assert.Equal(explicitWorkingDirectory ? Path.Combine(expectedRoot, "child") : expectedRoot, request.WorkingDirectory);
        Assert.Equal(original, variables.ToArray());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Substituted_executable_paths_preserve_literal_braces(bool completeRun, bool matchingVariable)
    {
        var root = Directory.CreateDirectory(Path.Combine(_root, "{build_id}")).FullName;
        var variables = new Dictionary<string, string> { ["ProjectRoot"] = root };
        if (matchingVariable) { variables["build_id"] = "wrong-directory"; }
        var runner = new Runner();
        var command = new ReleaseCommandValidation { FileName = "{ProjectRoot}/probe", Arguments = ["{ProjectRoot}"] };
        var service = new ReleaseValidationService(runner);
        if (completeRun) {
            var report = await service.RunAsync(new() { Commands = [command] }, request: new() { ProjectRoot = root, Variables = variables });
            Assert.True(report.Success, string.Join("; ", report.Errors));
        } else {
            await service.RunCommandAsync(command, variables);
        }
        var observed = Assert.Single(runner.Requests);
        Assert.Equal(Path.Combine(root, "probe"), observed.FileName);
        Assert.Equal(root, observed.WorkingDirectory);
        Assert.Equal(root, Assert.Single(observed.Arguments));
    }

    [Fact]
    public async Task Generated_module_probe_keeps_resolved_script_and_environment_paths_literal()
    {
        var root = Directory.CreateDirectory(Path.Combine(_root, "{build_id}")).FullName;
        File.WriteAllText(Path.Combine(root, "Example.psd1"), "@{ ModuleVersion = '1.2.3' }");
        var probeDirectory = Directory.CreateDirectory(Path.Combine(_root, "{probe_id}")).FullName;
        var script = Path.Combine(probeDirectory, "probe.ps1");
        File.WriteAllText(script, "Write-Output probe");
        var runner = new Runner();
        var report = await new ReleaseValidationService(runner).RunAsync(new() { Modules = [new() {
            Path = "{ProjectRoot}", Manifest = "Example.psd1", ProbeScript = "{ProbeRoot}/probe.ps1", Hosts = ["probe"]
        }] }, request: new() { ProjectRoot = root, Variables = new() { ["ProbeRoot"] = probeDirectory } });
        Assert.True(report.Success, string.Join("; ", report.Errors));
        var observed = Assert.Single(runner.Requests);
        Assert.Equal(script, observed.Arguments.Last());
        Assert.NotEqual(root, observed.EnvironmentVariables!["POWERFORGE_MODULE_PATH"]);
        Assert.False(Directory.Exists(observed.EnvironmentVariables["POWERFORGE_MODULE_PATH"]));
        Assert.False(Directory.Exists(observed.EnvironmentVariables["POWERFORGE_TEST_ROOT"]));
    }

    [Fact]
    public async Task Generated_consumer_project_argument_is_literal_after_copying_the_project()
    {
        var package = Path.Combine(_root, "Example.1.2.3.nupkg");
        using (var archive = ZipFile.Open(package, ZipArchiveMode.Create)) {
            using var writer = new StreamWriter(archive.CreateEntry("Example.nuspec").Open());
            writer.Write("<package><metadata><id>Example</id><version>1.2.3</version><authors>Tests</authors><description>Fixture</description></metadata></package>");
        }
        var source = Directory.CreateDirectory(Path.Combine(_root, "consumer")).FullName;
        var relativeProject = Path.Combine("{build_id}", "Probe.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(source, relativeProject))!);
        File.WriteAllText(Path.Combine(source, relativeProject), "<Project />");
        var runner = new Runner(request => {
            if (request.Arguments[0] == "restore") {
                var restored = Directory.CreateDirectory(Path.Combine(request.EnvironmentVariables!["NUGET_PACKAGES"]!, "example", "1.2.3")).FullName;
                File.Copy(package, Path.Combine(restored, "example.1.2.3.nupkg"));
            }
        });
        var report = await new ReleaseValidationService(runner).RunAsync(new() {
            Packages = new() { Path = _root, Items = [new() { Id = "Example" }] },
            Consumers = [new() { SourceDirectory = source, ProjectFile = relativeProject, Frameworks = ["net10.0"] }]
        }, request: new() { ProjectRoot = _root });
        Assert.True(report.Success, string.Join("; ", report.Errors));
        Assert.Equal(2, runner.Requests.Count);
        foreach (var request in runner.Requests) {
            Assert.Contains(Path.Combine(request.WorkingDirectory, relativeProject), request.Arguments);
            Assert.Contains("-p:PackageVersion=1.2.3", request.Arguments);
            Assert.False(Directory.Exists(request.WorkingDirectory));
        }
    }

    private sealed class Runner(Action<ProcessRunRequest>? execute = null) : IProcessRunner
    {
        internal List<ProcessRunRequest> Requests { get; } = [];
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            execute?.Invoke(request);
            return Task.FromResult(new ProcessRunResult(0, "", "", request.FileName, TimeSpan.Zero, false));
        }
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
