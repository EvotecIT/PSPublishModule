using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;

namespace PowerForge.Tests;

public sealed class ReleaseValidationServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.ReleaseValidation.Tests", Guid.NewGuid().ToString("N"));

    public ReleaseValidationServiceTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(null, false, true)]
    [InlineData(0, false, false)]
    [InlineData(7, false, true)]
    [InlineData(null, true, false)]
    public async Task Product_probe_can_interpret_exit_codes_without_disabling_timeouts(int? expectedExitCode, bool timedOut, bool success)
    {
        var runner = new RecordingRunner((_, _) => new ProcessRunResult(7, "license unavailable", "", "probe", TimeSpan.Zero, timedOut));
        var report = await Run(new ReleaseValidationSpec { Commands = [new() {
            FileName = "probe", ExpectedExitCode = expectedExitCode
        }] }, runner);

        Assert.Equal(success, report.Success);
        if (success) Assert.Single(report.Checks);
        else Assert.Single(report.Errors);
    }

    [Theory]
    [InlineData("[{\"name\":\"Event\"}]", true)]
    [InlineData("[]", false)]
    [InlineData("{\"name\":\"Event\"}", false)]
    [InlineData("not json", false)]
    public async Task Command_json_output_requires_array_with_minimum_item_count(string output, bool expectedSuccess)
    {
        var runner = new RecordingRunner((_, _) => Result(output: output));
        var command = new ReleaseCommandValidation { Name = "Types", FileName = "probe", OutputJsonKind = "Array", MinimumJsonItems = 1 };

        var report = await Run(new ReleaseValidationSpec { Commands = [command] }, runner);

        Assert.Equal(expectedSuccess, report.Success);
        if (expectedSuccess) Assert.Contains("Types", report.Checks);
        else {
            Assert.Single(report.Errors);
            Assert.Empty(report.Checks);
        }
    }

    [Fact]
    public async Task Json_roundtrip_preserves_executable_contract_and_report_evidence()
    {
        var configPath = Path.Combine(_root, "validation.json");
        var spec = new ReleaseValidationSpec { Commands = [new() {
            Name = "Quoted command", FileName = "probe", Arguments = ["a \"quoted\" value", "{Version}"],
            ExpectedOutput = "done", Environment = new() { ["REMOVE"] = null, ["LABEL"] = "żółć" }
        }] };
        File.WriteAllText(configPath, ReleaseValidationService.Serialize(spec));
        var runner = new RecordingRunner((_, _) => Result(output: "done"));

        var report = await Run(ReleaseValidationService.Load(configPath), runner, "1.2.3");

        Assert.True(report.Success, string.Join("\n", report.Errors));
        var request = Assert.Single(runner.Requests);
        Assert.Equal(new[] { "a \"quoted\" value", "1.2.3" }, request.Arguments);
        Assert.Equal("żółć", request.EnvironmentVariables!["LABEL"]);
        Assert.Null(request.EnvironmentVariables["REMOVE"]);
        using var evidence = JsonDocument.Parse(ReleaseValidationService.SerializeReport(report));
        Assert.True(evidence.RootElement.GetProperty("Success").GetBoolean());
        Assert.Equal("Quoted command", evidence.RootElement.GetProperty("Checks")[0].GetString());
    }

    [Theory]
    [InlineData("{ broken json")]
    [InlineData("{\"Commmand\":[]}")]
    [InlineData("{\"Commands\":[{\"FileName\":\"probe\",\"ExpectedOutpt\":\"ok\"}]}")]
    public void Json_load_rejects_malformed_or_unknown_contract_fields(string json)
    {
        var path = Path.Combine(_root, "invalid.json");
        File.WriteAllText(path, json);

        Assert.Throws<JsonException>(() => ReleaseValidationService.Load(path));
    }

    [Theory]
    [InlineData("../escaped.psm1")]
    [InlineData("Example/../../escaped.psm1")]
    [InlineData("..\\escaped.psm1")]
    public async Task Module_zip_rejects_traversal_before_runtime_probe(string entry)
    {
        var path = Path.Combine(_root, "unsafe.zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create)) {
            WriteEntry(archive, "Example.psd1", "@{ ModuleVersion = '1.2.3' }");
            WriteEntry(archive, entry, "outside payload");
        }
        var runner = new RecordingRunner((_, _) => throw new InvalidOperationException("Unexpected runtime probe"));

        var report = await Run(new ReleaseValidationSpec { Modules = [new() {
            Path = path, Manifest = "Example.psd1", ProbeScript = "probe.ps1"
        }] }, runner);

        Assert.False(report.Success);
        Assert.Contains("unsafe entry", Assert.Single(report.Errors));
        Assert.Empty(runner.Requests);
        Assert.Empty(report.Checks);
        Assert.DoesNotContain("Unexpected runtime probe", report.Errors[0]);
    }

    [Theory]
    [InlineData("valid", true, "")]
    [InlineData("missing-runtime", false, "matrix")]
    [InlineData("wrong-style", false, "matrix")]
    [InlineData("wrong-version", false, "version")]
    [InlineData("missing-file", false, "missing")]
    [InlineData("empty-file", false, "empty")]
    public async Task Cli_manifest_validates_runtime_style_version_and_artifact_existence(string variant, bool expectedSuccess, string error)
    {
        var entries = new List<object>();
        foreach (var runtime in new[] { "win-x64", "linux-x64" }) {
            if (variant == "missing-runtime" && runtime == "linux-x64") continue;
            var path = Path.Combine(_root, runtime + ".zip");
            if (variant != "missing-file") File.WriteAllText(path, variant == "empty-file" ? "" : "artifact");
            entries.Add(new { category = "Tool", target = "Example", runtime,
                style = variant == "wrong-style" ? "Different" : "Portable",
                version = variant == "wrong-version" ? "1.2.4" : "1.2.3", stagedPath = path });
        }
        var evidence = Path.Combine(_root, "build-evidence.json");
        File.WriteAllText(evidence, "{}");
        entries.Add(new { category = "Metadata", stagedPath = evidence });
        var manifest = Path.Combine(_root, "manifest.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new { assetEntries = entries }));

        var report = await Run(new ReleaseValidationSpec { CliArtifacts = new() {
            ManifestPath = manifest, Target = "Example", Runtimes = ["win-x64", "linux-x64"], Styles = ["Portable"], ToolsOnly = true
        } }, version: "1.2.3");

        Assert.Equal(expectedSuccess, report.Success);
        if (expectedSuccess) Assert.Contains("CLI Example: 2 artifacts", report.Checks);
        else Assert.Contains(error, Assert.Single(report.Errors));
    }

    [Fact]
    public async Task Packages_validate_contents_dependencies_symbols_and_infer_version()
    {
        Package("Example", "1.2.3", "<group targetFramework=\"net10.0\"><dependency id=\"Runtime\" version=\"[2.0.0]\" exclude=\"Compile\" /></group>", "lib/net10.0/Example.dll");
        Package("Example", "1.2.3", "", "lib/net10.0/Example.pdb", symbols: true);
        var spec = PackageSpec(new PackageArtifactContract {
            Id = "Example", RequiredEntries = ["lib/**/*.dll"], ForbiddenEntries = ["**/*.exe"],
            DependencyFrameworks = ["net10.0"], RequiredDependencies = ["Runtime"], RuntimeOnlyDependencies = ["Runtime"],
            SymbolEntries = ["lib/**/*.pdb"], ForbiddenSymbolEntries = ["**/*.dll"]
        });

        var report = await Run(spec);

        Assert.True(report.Success, string.Join("\n", report.Errors));
        Assert.Equal("1.2.3", report.Version);
        Assert.Contains("Package Example 1.2.3", report.Checks);
    }

    [Theory]
    [InlineData("missing", "missing")]
    [InlineData("forbidden", "must not contain")]
    [InlineData("extra", "undeclared packages")]
    [InlineData("duplicate", "exactly one")]
    [InlineData("version", "does not match")]
    [InlineData("symbols", "Symbol package identity")]
    public async Task Packages_reject_contract_violations(string violation, string error)
    {
        Package("Example", "1.2.3", "", "lib/net10.0/Example.dll");
        var contract = new PackageArtifactContract { Id = "Example" };
        if (violation == "missing") contract.RequiredEntries = ["lib/**/*.xml"];
        if (violation == "forbidden") contract.ForbiddenEntries = ["**/*.dll"];
        if (violation == "extra") Package("Other", "1.2.3", "", "other.txt");
        if (violation == "duplicate") Package("Example", "1.2.4", "", "other.txt");
        if (violation == "symbols") {
            contract.SymbolEntries = ["**/*.pdb"];
            Package("Example", "9.0.0", "", "lib/Example.pdb", symbols: true, fileName: "Example.1.2.3.snupkg");
        }

        var report = await Run(PackageSpec(contract), version: violation == "version" ? "2.0.0" : null);

        Assert.False(report.Success);
        Assert.Contains(error, Assert.Single(report.Errors));
    }

    [Theory]
    [InlineData("<group targetFramework=\"net10.0\"><dependency id=\"Runtime\" version=\"1.0.0\" /></group>", "exposes compile assets")]
    [InlineData("", "dependency")]
    public async Task Runtime_only_dependencies_must_exist_and_exclude_compile_assets(string dependencies, string error)
    {
        Package("Example", "1.2.3", dependencies, "lib/net10.0/Example.dll");

        var report = await Run(PackageSpec(new PackageArtifactContract { Id = "Example", RuntimeOnlyDependencies = ["Runtime"] }));

        Assert.False(report.Success);
        Assert.Contains(error, Assert.Single(report.Errors), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Command_preserves_arguments_and_expands_environment_and_asserts_artifacts()
    {
        File.WriteAllText(Path.Combine(_root, "result.txt"), "evidence");
        var runner = new RecordingRunner((_, _) => Result(exitCode: 7, output: "  release 1.2.3\n"));
        var command = new ReleaseCommandValidation {
            FileName = "probe", Arguments = ["a b", "literal; & value", "{Version}"], TimeoutSeconds = 12,
            ExpectedExitCode = 7, ExpectedOutput = "release {Version}", OutputContains = ["1.2.3"], NonEmptyFiles = ["result.txt"],
            Environment = new() { ["VERSION"] = "{Version}", ["REMOVED"] = null }
        };

        var report = await Run(new ReleaseValidationSpec { Commands = [command] }, runner, "1.2.3");

        Assert.True(report.Success, string.Join("\n", report.Errors));
        var request = Assert.Single(runner.Requests);
        Assert.Equal(new[] { "a b", "literal; & value", "1.2.3" }, request.Arguments);
        Assert.Equal(_root, request.WorkingDirectory);
        Assert.Equal(TimeSpan.FromSeconds(12), request.Timeout);
        Assert.Equal("1.2.3", request.EnvironmentVariables!["VERSION"]);
        Assert.Null(request.EnvironmentVariables["REMOVED"]);
        Assert.Contains("Command", report.Checks);
    }

    [Theory]
    [InlineData(1, false, false, false)]
    [InlineData(0, true, false, false)]
    [InlineData(0, false, true, false)]
    [InlineData(0, false, false, true)]
    public async Task Command_rejects_failed_or_incomplete_process_results(int exit, bool timeout, bool stdoutLimit, bool stderrLimit)
    {
        var runner = new RecordingRunner((_, _) => new ProcessRunResult(exit, "output", "diagnostic", "probe", TimeSpan.Zero, timeout, stdoutLimit, stderrLimit));

        var report = await Run(new ReleaseValidationSpec { Commands = [new() { Name = "Smoke", FileName = "probe" }] }, runner);

        Assert.False(report.Success);
        Assert.Contains("Smoke failed", Assert.Single(report.Errors));
        Assert.Contains("diagnostic", report.Errors[0]);
        Assert.Empty(report.Checks);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Command_cancellation_propagates_to_caller(bool runnerThrows)
    {
        using var cancellation = new CancellationTokenSource();
        var runner = new RecordingRunner((_, token) => {
            Assert.Equal(cancellation.Token, token);
            cancellation.Cancel();
            if (runnerThrows) token.ThrowIfCancellationRequested();
            return Result();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ReleaseValidationService(runner).RunAsync(
            new ReleaseValidationSpec { Commands = [new() { FileName = "probe" }] },
            request: new ReleaseValidationRequest { ProjectRoot = _root }, cancellationToken: cancellation.Token));
    }

    [Theory]
    [InlineData("exact", "standard output")]
    [InlineData("contains", "output did not contain")]
    [InlineData("file", "nonempty file")]
    public async Task Command_rejects_unsatisfied_output_contracts(string contract, string error)
    {
        var command = new ReleaseCommandValidation { FileName = "probe" };
        if (contract == "exact") command.ExpectedOutput = "expected";
        if (contract == "contains") command.OutputContains = ["required"];
        if (contract == "file") {
            File.WriteAllText(Path.Combine(_root, "empty.txt"), "");
            command.NonEmptyFiles = ["empty.txt"];
        }

        var report = await Run(new ReleaseValidationSpec { Commands = [command] }, new RecordingRunner((_, _) => Result(output: "actual")));

        Assert.False(report.Success);
        Assert.Contains(error, Assert.Single(report.Errors));
        Assert.Empty(report.Checks);
    }

    [Fact]
    public async Task Module_zip_is_extracted_probed_and_cleaned()
    {
        var zip = Path.Combine(_root, "module.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create)) {
            WriteEntry(archive, "Example/Example.psd1", "@{ ModuleVersion = '1.2.3'; ProcessorArchitecture = 'Amd64' }");
            WriteEntry(archive, "Example/Example.psm1", "function Get-Example { 'ok' }");
        }
        string? extracted = null;
        string? probe = null;
        var runner = new RecordingRunner((request, _) => {
            extracted = request.EnvironmentVariables!["POWERFORGE_MODULE_PATH"];
            probe = request.EnvironmentVariables["POWERFORGE_TEST_ROOT"];
            Assert.True(File.Exists(Path.Combine(extracted!, "Example.psm1")));
            Assert.True(Directory.Exists(probe));
            Assert.Equal(new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", Path.Combine(_root, "probe.ps1") }, request.Arguments);
            return Result();
        });

        var report = await Run(new ReleaseValidationSpec { Modules = [new() {
            Path = zip, ArchiveRoot = "Example", Manifest = "Example.psd1", RequiredFiles = ["*.psm1"],
            ProcessorArchitecture = "Amd64", ProbeScript = "probe.ps1", Hosts = ["pwsh"]
        }] }, runner);

        Assert.True(report.Success, string.Join("\n", report.Errors));
        Assert.Equal("1.2.3", report.Version);
        Assert.False(Directory.Exists(extracted));
        Assert.False(Directory.Exists(probe));
        Assert.True(File.Exists(zip));
    }

    [Fact]
    public async Task Tool_path_and_manifest_installs_use_isolated_exact_feed_and_cleanup()
    {
        Package("Example.Tool", "1.2.3", "", "tools/net10.0/any/tool.dll");
        var roots = new HashSet<string>();
        var probes = 0;
        var runner = new RecordingRunner((request, _) => {
            roots.Add(request.WorkingDirectory);
            Assert.Equal(Path.Combine(request.WorkingDirectory, "packages"), request.EnvironmentVariables!["NUGET_PACKAGES"]);
            Assert.Equal(request.WorkingDirectory, request.EnvironmentVariables["DOTNET_CLI_HOME"]);
            if (request.Arguments.Contains("install")) {
                Assert.Contains("1.2.3", request.Arguments);
                var config = XDocument.Load(Path.Combine(request.WorkingDirectory, "NuGet.Config"));
                var source = Assert.Single(config.Root!.Element("packageSources")!.Elements("add"));
                Assert.Single(Directory.GetFiles(source.Attribute("value")!.Value, "*.nupkg"));
            }
            if (request.Arguments.Contains("--version-probe")) {
                probes++;
                Assert.Equal("1.2.3", request.EnvironmentVariables["RELEASE_VERSION"]);
                if (request.FileName == "dotnet") Assert.Equal(new[] { "tool", "run", "example", "--", "--version-probe" }, request.Arguments);
                else Assert.StartsWith(Path.Combine(request.WorkingDirectory, "tools", "example"), request.FileName);
            }
            return Result(output: "1.2.3");
        });

        var report = await Run(new ReleaseValidationSpec { Tools = [new() {
            PackageId = "Example.Tool", PackageRoot = _root, CommandName = "example", IncludeManifestInstall = true,
            Commands = [new() { FileName = "{ToolPath}", Arguments = ["--version-probe"], ExpectedOutput = "{Version}", Environment = new() { ["RELEASE_VERSION"] = "{Version}" } }]
        }] }, runner);

        Assert.True(report.Success, string.Join("\n", report.Errors));
        Assert.Equal(2, probes);
        Assert.Equal(2, roots.Count);
        Assert.All(roots, root => Assert.False(Directory.Exists(root)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Consumer_requires_exact_staged_bytes_and_cleans_workspace(bool corruptRestoredPackage)
    {
        Package("Example", "1.2.3", "", "lib/net10.0/Example.dll");
        var source = Directory.CreateDirectory(Path.Combine(_root, "source")).FullName;
        File.WriteAllText(Path.Combine(source, "Smoke.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        Directory.CreateDirectory(Path.Combine(source, "obj"));
        File.WriteAllText(Path.Combine(source, "obj", "stale"), "old");
        string? workspace = null;
        var runner = new RecordingRunner((request, _) => {
            workspace = Path.GetDirectoryName(request.WorkingDirectory)!;
            Assert.False(Directory.Exists(Path.Combine(request.WorkingDirectory, "obj")));
            if (request.Arguments[0] == "restore") {
                var config = XDocument.Load(Path.Combine(workspace, "NuGet.Config"));
                var mapping = config.Root!.Element("packageSourceMapping")!.Elements("packageSource").First();
                Assert.Equal("Example", Assert.Single(mapping.Elements("package")).Attribute("pattern")!.Value);
                var cache = request.EnvironmentVariables!["NUGET_PACKAGES"]!;
                var restored = Path.Combine(Directory.CreateDirectory(Path.Combine(cache, "example", "1.2.3")).FullName, "example.1.2.3.nupkg");
                if (corruptRestoredPackage) File.WriteAllText(restored, "different bytes");
                else File.Copy(Path.Combine(_root, "Example.1.2.3.nupkg"), restored);
            } else {
                Assert.Equal("run", request.Arguments[0]);
                Assert.Contains("--no-restore", request.Arguments);
                Assert.Contains("net10.0", request.Arguments);
                Assert.Contains("-p:PackageVersion=1.2.3", request.Arguments);
            }
            return Result();
        });
        var spec = PackageSpec(new PackageArtifactContract { Id = "Example" });
        spec.Consumers = [new() { SourceDirectory = source, ProjectFile = "Smoke.csproj", Frameworks = ["net10.0"], DependencySources = [] }];

        var report = await Run(spec, runner);

        Assert.Equal(!corruptRestoredPackage, report.Success);
        Assert.Equal(corruptRestoredPackage ? 1 : 2, runner.Requests.Count);
        if (corruptRestoredPackage) Assert.Contains("exact staged", Assert.Single(report.Errors));
        Assert.NotNull(workspace);
        Assert.False(Directory.Exists(workspace));
        Assert.True(File.Exists(Path.Combine(source, "obj", "stale")));
    }

    private Task<ReleaseValidationReport> Run(ReleaseValidationSpec spec, RecordingRunner? runner = null, string? version = null)
        => new ReleaseValidationService(runner ?? new RecordingRunner((_, _) => throw new InvalidOperationException("Unexpected process")))
            .RunAsync(spec, request: new ReleaseValidationRequest { ProjectRoot = _root, Version = version,
                Variables = new() { ["StagingRoot"] = _root } });

    private PackageSetValidation Set(PackageArtifactContract contract) => new() { Path = _root, Items = [contract] };
    private ReleaseValidationSpec PackageSpec(PackageArtifactContract contract) => new() { Packages = Set(contract) };

    private void Package(string id, string version, string dependencies, string entry, bool symbols = false, string? fileName = null)
    {
        var path = Path.Combine(_root, fileName ?? $"{id}.{version}.{(symbols ? "snupkg" : "nupkg")}");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        WriteEntry(archive, id + ".nuspec", $"<?xml version=\"1.0\"?><package><metadata><id>{id}</id><version>{version}</version><authors>Tests</authors><description>Test package</description><dependencies>{dependencies}</dependencies></metadata></package>");
        WriteEntry(archive, entry, "fixture payload");
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        using var writer = new StreamWriter(archive.CreateEntry(name).Open());
        writer.Write(content);
    }

    private static ProcessRunResult Result(int exitCode = 0, string output = "") => new(exitCode, output, "", "probe", TimeSpan.Zero, false);

    private sealed class RecordingRunner(Func<ProcessRunRequest, CancellationToken, ProcessRunResult> execute) : IProcessRunner
    {
        internal List<ProcessRunRequest> Requests { get; } = [];
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(execute(request, cancellationToken));
        }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
