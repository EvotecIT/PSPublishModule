using System.Text.Json.Nodes;
using System.IO.Compression;
using Json.Schema;

namespace PowerForge.Tests;

public sealed class ReleaseValidationAdmissionSchemaTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.AdmissionSchema.Tests", Guid.NewGuid().ToString("N"));

    public ReleaseValidationAdmissionSchemaTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("\r\n", false)]
    [InlineData("\t", false)]
    [InlineData("probe", true)]
    [InlineData("probe tool", true)]
    public async Task Command_filename_schema_matches_loaded_runtime_admission(string? fileName, bool expectedValid)
    {
        var command = new JsonObject {
            ["Name"] = "Admission probe",
            ["Arguments"] = new JsonArray("--probe", "value with spaces"),
            ["ExpectedOutput"] = "observed"
        };
        if (fileName is not null) { command["FileName"] = fileName; }
        var document = new JsonObject { ["Commands"] = new JsonArray(command) };
        var schemaValid = LoadSchema().Evaluate(document).IsValid;
        var configPath = Path.Combine(_root, "validation.json");
        File.WriteAllText(configPath, document.ToJsonString());
        var runner = new Runner();

        var report = await new ReleaseValidationService(runner).RunAsync(
            ReleaseValidationService.Load(configPath), configPath, new() { ProjectRoot = _root });

        Assert.Equal(expectedValid, report.Success);
        if (expectedValid) {
            Assert.Empty(report.Errors);
            Assert.Equal("Admission probe", Assert.Single(report.Checks));
            var request = Assert.Single(runner.Requests);
            Assert.Equal(fileName, request.FileName);
            Assert.Equal(new[] { "--probe", "value with spaces" }, request.Arguments);
        } else {
            Assert.Contains("executable", Assert.Single(report.Errors));
            Assert.Empty(report.Checks);
            Assert.Empty(runner.Requests);
        }
        Assert.Equal(expectedValid, schemaValid);
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("[]", null, false)]
    [InlineData(null, "[]", false)]
    [InlineData("[]", "[]", false)]
    [InlineData("[\"\"]", "[\" \"]", false)]
    [InlineData("[\"\\t\\r\\n\"]", null, false)]
    [InlineData(null, "[\"\\t\\r\\n\"]", false)]
    [InlineData("[\"net8.0\"]", null, true)]
    [InlineData(null, "[\"net8.0-windows\"]", true)]
    [InlineData("[]", "[\"net8.0-windows\"]", true)]
    [InlineData("[\"net8.0\"]", "[]", true)]
    [InlineData("[\"net8.0\"]", "[\"net8.0-windows\"]", true)]
    [InlineData("[\"net8.0\",\" \"]", "[\"net8.0-windows\"]", false)]
    [InlineData("[\"net8.0\"]", "[\"net8.0-windows\",\"\"]", false)]
    public void Consumer_schema_requires_a_configured_framework_and_rejects_blank_items(
        string? frameworks, string? windowsFrameworks, bool expectedValid)
    {
        // Windows-only configuration is portable schema input; runtime selects executable frameworks for its host.
        var consumer = new JsonObject { ["SourceDirectory"] = "consumer", ["ProjectFile"] = "Smoke.csproj" };
        if (frameworks is not null) { consumer["Frameworks"] = JsonNode.Parse(frameworks); }
        if (windowsFrameworks is not null) { consumer["WindowsFrameworks"] = JsonNode.Parse(windowsFrameworks); }
        var document = new JsonObject {
            ["Packages"] = new JsonObject { ["Items"] = new JsonArray(new JsonObject { ["Id"] = "Example" }) },
            ["Consumers"] = new JsonArray(consumer)
        };

        var result = LoadSchema().Evaluate(document, new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.Equal(expectedValid, result.IsValid);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData(" \t\n", false)]
    [InlineData("Example.Tool", true)]
    public void Package_and_tool_identity_schema_rejects_blank_names(string id, bool valid)
    {
        var package = new JsonObject { ["Packages"] = new JsonObject { ["Items"] = new JsonArray(new JsonObject { ["Id"] = id }) } };
        var tool = new JsonObject { ["Tools"] = new JsonArray(new JsonObject { ["PackageId"] = id, ["CommandName"] = "example" }) };
        Assert.Equal(valid, LoadSchema().Evaluate(package).IsValid);
        Assert.Equal(valid, LoadSchema().Evaluate(tool).IsValid);
    }

    [Theory]
    [InlineData("RequiredEntries")]
    [InlineData("ForbiddenEntries")]
    [InlineData("SymbolEntries")]
    [InlineData("ForbiddenSymbolEntries")]
    public async Task Package_entry_patterns_reject_blank_items_before_artifact_access(string property)
    {
        var package = new JsonObject { ["Id"] = "Example", [property] = new JsonArray(" ", "lib/**/*.dll") };
        var document = new JsonObject { ["Packages"] = new JsonObject {
            ["Path"] = "missing-packages", ["Items"] = new JsonArray(package) } };
        Assert.False(LoadSchema().Evaluate(document).IsValid);

        var contract = new PackageArtifactContract { Id = "Example" };
        typeof(PackageArtifactContract).GetProperty(property)!.SetValue(contract, new[] { " ", "lib/**/*.dll" });
        var report = await new ReleaseValidationService().RunAsync(new() {
            Packages = new() { Path = "missing-packages", Items = [contract] }
        }, request: new() { ProjectRoot = _root });

        Assert.False(report.Success);
        Assert.Contains(property, Assert.Single(report.Errors));
        Assert.Empty(report.Checks);
    }

    [Theory]
    [InlineData("DependencyFrameworks")]
    [InlineData("RequiredDependencies")]
    [InlineData("ForbiddenDependencies")]
    [InlineData("RuntimeOnlyDependencies")]
    public async Task Package_dependency_contracts_reject_blank_items_before_artifact_access(string property)
    {
        var package = new JsonObject { ["Id"] = "Example", [property] = new JsonArray(" ", "Example.Dependency") };
        var document = new JsonObject { ["Packages"] = new JsonObject {
            ["Path"] = "missing-packages", ["Items"] = new JsonArray(package) } };
        Assert.False(LoadSchema().Evaluate(document).IsValid);

        var contract = new PackageArtifactContract { Id = "Example" };
        typeof(PackageArtifactContract).GetProperty(property)!.SetValue(contract, new[] { " ", "Example.Dependency" });
        var report = await new ReleaseValidationService().RunAsync(new() {
            Packages = new() { Path = "missing-packages", Items = [contract] }
        }, request: new() { ProjectRoot = _root });

        Assert.False(report.Success);
        Assert.Contains(property, Assert.Single(report.Errors));
        Assert.Empty(report.Checks);
    }

    [Theory]
    [InlineData("RequiredFiles")]
    [InlineData("VersionedAssemblies")]
    [InlineData("SignatureInclude")]
    public async Task Module_file_patterns_reject_blank_items_before_artifact_access(string property)
    {
        var module = new JsonObject { ["Path"] = "missing-module", ["Manifest"] = "Example.psd1" };
        if (property == "SignatureInclude")
            module["Signatures"] = new JsonObject { ["Include"] = new JsonArray("\t", "**/*.dll") };
        else
            module[property] = new JsonArray("\t", "**/*.dll");
        Assert.False(LoadSchema().Evaluate(new JsonObject { ["Modules"] = new JsonArray(module) }).IsValid);

        var contract = new ModuleArtifactValidation { Path = "missing-module", Manifest = "Example.psd1" };
        if (property == "SignatureInclude") contract.Signatures = new() { Include = ["\t", "**/*.dll"] };
        else typeof(ModuleArtifactValidation).GetProperty(property)!.SetValue(contract, new[] { "\t", "**/*.dll" });
        var report = await new ReleaseValidationService().RunAsync(new() { Modules = [contract] },
            request: new() { ProjectRoot = _root });

        Assert.False(report.Success);
        Assert.Contains(property == "SignatureInclude" ? "Include" : property, Assert.Single(report.Errors));
        Assert.Empty(report.Checks);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("blank")]
    [InlineData("mixed")]
    [InlineData("windows-blank")]
    [InlineData("expanded-empty")]
    public async Task Invalid_consumer_frameworks_fail_before_restore_or_copy(string variant)
    {
        using (var zip = ZipFile.Open(Path.Combine(_root, "Example.nupkg"), ZipArchiveMode.Create)) {
            using var writer = new StreamWriter(zip.CreateEntry("Example.nuspec").Open());
            writer.Write("<package><metadata><id>Example</id><version>1.2.3</version><authors>Tests</authors><description>Fixture</description></metadata></package>");
        }
        var runner = new Runner();
        var consumer = new PackageConsumerValidation { SourceDirectory = "not-copied", ProjectFile = "Smoke.csproj" };
        consumer.Frameworks = variant switch {
            "blank" => [" "], "mixed" => ["net8.0", ""], "expanded-empty" => ["{EmptyFramework}"], _ => []
        };
        if (variant == "windows-blank") { consumer.WindowsFrameworks = [" "]; }
        var report = await new ReleaseValidationService(runner).RunAsync(new() {
            Packages = new() { Path = _root, Items = [new() { Id = "Example" }] }, Consumers = [consumer]
        }, request: new() { ProjectRoot = _root, Variables = new() { ["EmptyFramework"] = "" } });
        Assert.False(report.Success);
        Assert.Contains("framework", Assert.Single(report.Errors), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(runner.Requests);
        Assert.Equal("Package Example 1.2.3", Assert.Single(report.Checks));
        Assert.False(Directory.Exists(Path.Combine(_root, "not-copied")));
    }

    [Theory]
    [InlineData(null, null, true)]
    [InlineData(null, "[]", true)]
    [InlineData(null, "[\" \"]", true)]
    [InlineData("probe.ps1", null, true)]
    [InlineData("probe.ps1", "[]", false)]
    [InlineData("probe.ps1", "[\"pwsh\",\" \"]", false)]
    [InlineData("probe.ps1", "[\"pwsh\"]", true)]
    public async Task Module_host_schema_matches_loaded_runtime_admission(string? script, string? hosts, bool valid)
    {
        File.WriteAllText(Path.Combine(_root, "Example.psd1"), "@{ ModuleVersion = '1.2.3' }");
        var module = new JsonObject { ["Path"] = _root, ["Manifest"] = "Example.psd1", ["ProbeScript"] = script };
        if (hosts is not null) { module["Hosts"] = JsonNode.Parse(hosts); }
        var document = new JsonObject { ["Modules"] = new JsonArray(module) };
        var config = Path.Combine(_root, "module-validation.json");
        File.WriteAllText(config, document.ToJsonString());
        var runner = new Runner();
        var report = await new ReleaseValidationService(runner).RunAsync(ReleaseValidationService.Load(config), config);
        Assert.Equal(valid, report.Success);
        if (valid) {
            Assert.Contains("Module Example.psd1", report.Checks);
            if (script is null) { Assert.Empty(runner.Requests); }
            else {
                var request = Assert.Single(runner.Requests);
                Assert.Equal("pwsh", request.FileName);
                Assert.False(Directory.Exists(request.WorkingDirectory));
            }
        } else {
            Assert.Contains("host", Assert.Single(report.Errors), StringComparison.OrdinalIgnoreCase);
            Assert.Empty(runner.Requests);
            Assert.Empty(report.Checks);
        }
        Assert.Equal(valid, LoadSchema().Evaluate(document).IsValid);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("blank")]
    [InlineData("null")]
    public async Task Invalid_module_hosts_fail_before_reading_the_module(string variant)
    {
        var runner = new Runner();
        var report = await new ReleaseValidationService(runner).RunAsync(new() {
            Modules = [new() { Path = "missing-module", Manifest = "Example.psd1", ProbeScript = "probe.ps1",
                Hosts = variant switch { "blank" => ["pwsh", " "], "null" => null!, _ => [] } }]
        }, request: new() { ProjectRoot = _root });
        Assert.False(report.Success);
        Assert.Contains("host", Assert.Single(report.Errors), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(report.Checks);
        Assert.Empty(runner.Requests);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("probe-{Literal}", true)]
    public async Task Module_hosts_expand_once_after_the_module_version_is_known(string host, bool valid)
    {
        File.WriteAllText(Path.Combine(_root, "Example.psd1"), "@{ ModuleVersion = '1.2.3' }");
        var runner = new Runner();
        var report = await new ReleaseValidationService(runner).RunAsync(new() {
            Modules = [new() { Path = _root, Manifest = "Example.psd1", ProbeScript = "probe.ps1",
                Hosts = ["probe-{Version}", "{Host}"] }]
        }, request: new() { ProjectRoot = _root, Variables = new() { ["Host"] = host } });
        Assert.Equal(valid, report.Success);
        if (valid) {
            Assert.Equal(new[] { "probe-1.2.3", host }, runner.Requests.Select(request => request.FileName));
            Assert.All(runner.Requests, request => Assert.False(Directory.Exists(request.WorkingDirectory)));
        } else {
            Assert.Contains("host", Assert.Single(report.Errors), StringComparison.OrdinalIgnoreCase);
            Assert.Empty(runner.Requests);
            Assert.Empty(report.Checks);
        }
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData(" \t\r\n", false)]
    [InlineData("Smoke.csproj", true)]
    public async Task Consumer_project_file_must_be_nonblank(string? projectFile, bool valid)
    {
        var document = new JsonObject {
            ["Packages"] = new JsonObject { ["Path"] = _root,
                ["Items"] = new JsonArray(new JsonObject { ["Id"] = "Example" }) },
            ["Consumers"] = new JsonArray(new JsonObject { ["SourceDirectory"] = "not-copied",
                ["ProjectFile"] = projectFile, ["Frameworks"] = new JsonArray("net10.0") })
        };
        Assert.Equal(valid, LoadSchema().Evaluate(document).IsValid);
        if (valid) { return; } // Real valid consumer restore/run is covered by ReleaseValidationCommandPathTests.
        using (var zip = ZipFile.Open(Path.Combine(_root, "Example.nupkg"), ZipArchiveMode.Create)) {
            using var writer = new StreamWriter(zip.CreateEntry("Example.nuspec").Open());
            writer.Write("<package><metadata><id>Example</id><version>1.2.3</version><authors>Tests</authors><description>Fixture</description></metadata></package>");
        }
        var runner = new Runner();
        var report = await new ReleaseValidationService(runner).RunAsync(new() {
            Packages = new() { Path = _root, Items = [new() { Id = "Example" }] },
            Consumers = [new() { SourceDirectory = "not-copied", ProjectFile = projectFile!, Frameworks = ["net10.0"] }]
        });
        Assert.False(report.Success);
        Assert.Contains("nonblank project file", Assert.Single(report.Errors));
        Assert.Empty(runner.Requests);
    }

    [Theory]
    [InlineData("missing", false)]
    [InlineData("null", false)]
    [InlineData("empty", false)]
    [InlineData("declared", true)]
    public async Task Consumers_require_a_declared_package_set_even_alongside_commands(string packages, bool valid)
    {
        var document = new JsonObject {
            ["Consumers"] = new JsonArray(new JsonObject { ["SourceDirectory"] = "consumer",
                ["ProjectFile"] = "Smoke.csproj", ["Frameworks"] = new JsonArray("net10.0") }),
            ["Commands"] = new JsonArray(new JsonObject { ["FileName"] = "probe" })
        };
        if (packages != "missing") {
            document["Packages"] = packages == "null" ? null : new JsonObject {
                ["Items"] = packages == "empty" ? new JsonArray() : new JsonArray(new JsonObject { ["Id"] = "Example" }) };
        }
        Assert.Equal(valid, LoadSchema().Evaluate(document).IsValid);
        if (packages is "missing" or "null") {
            var report = await new ReleaseValidationService().RunAsync(new() {
                Consumers = [new() { SourceDirectory = "consumer", ProjectFile = "Smoke.csproj", Frameworks = ["net10.0"] }]
            });
            Assert.Contains("declared package set", Assert.Single(report.Errors));
        }
        // An empty consumer list has no dependency on a package contract.
        document.Remove("Packages");
        document["Consumers"] = new JsonArray();
        Assert.True(LoadSchema().Evaluate(document).IsValid);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData(" \t\r\n", false)]
    [InlineData(null, true)]
    public async Task Optional_module_probe_is_null_or_a_nonblank_script(string? script, bool valid)
    {
        File.WriteAllText(Path.Combine(_root, "Example.psd1"), "@{ ModuleVersion = '1.2.3' }");
        var module = new JsonObject { ["Path"] = _root, ["Manifest"] = "Example.psd1", ["ProbeScript"] = script };
        Assert.Equal(valid, LoadSchema().Evaluate(new JsonObject { ["Modules"] = new JsonArray(module) }).IsValid);
        var runner = new Runner();
        var report = await new ReleaseValidationService(runner).RunAsync(new() {
            Modules = [new() { Path = _root, Manifest = "Example.psd1", ProbeScript = script }]
        });
        Assert.Equal(valid, report.Success);
        if (!valid) { Assert.Contains("nonblank script", Assert.Single(report.Errors)); }
        Assert.Empty(runner.Requests);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData(" \t\r\n", false)]
    [InlineData("manifest.json", true)]
    public async Task Cli_manifest_path_is_nonblank_when_explicit(string manifestPath, bool valid)
    {
        var cli = new JsonObject {
            ["ManifestPath"] = manifestPath,
            ["Target"] = "app",
            ["Runtimes"] = new JsonArray("linux-x64"),
            ["Styles"] = new JsonArray("Portable")
        };
        Assert.Equal(valid, LoadSchema().Evaluate(new JsonObject { ["CliArtifacts"] = cli }).IsValid);
        if (valid) { return; }

        var report = await new ReleaseValidationService().RunAsync(new() { CliArtifacts = new() {
            ManifestPath = manifestPath, Target = "app", Runtimes = ["linux-x64"], Styles = ["Portable"]
        } });
        Assert.False(report.Success);
        Assert.Contains("nonblank manifest path", Assert.Single(report.Errors));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData(" \t\r\n", false)]
    [InlineData(null, false)]
    [InlineData("win-x64", true)]
    public void Supported_runtime_entries_cannot_silently_remove_the_restriction(string? runtime, bool valid)
    {
        var document = new JsonObject { ["Targets"] = new JsonArray(new JsonObject {
            ["Name"] = "app", ["ProjectPath"] = "App.csproj", ["SupportedRuntimes"] = new JsonArray(JsonValue.Create(runtime)),
            ["Publish"] = new JsonObject { ["Framework"] = "net10.0" }
        }) };
        Assert.Equal(valid, LoadSchema("powerforge.dotnetpublish.schema.json").Evaluate(document).IsValid);
        var target = new DotNetPublishTarget { Name = "app", SupportedRuntimes = [runtime!],
            Publish = new() { Framework = "net10.0" } };
        var spec = new DotNetPublishSpec { DotNet = new() { Runtimes = ["linux-x64"] } };
        if (valid) {
            Assert.Equal("win-x64", Assert.Single(DotNetPublishPipelineRunner.ResolveTargetCombinations(target, spec)).Runtime);
        } else {
            Assert.Contains("SupportedRuntimes", Assert.Throws<ArgumentException>(() =>
                DotNetPublishPipelineRunner.ResolveTargetCombinations(target, spec)).Message);
        }
    }

    [Theory]
    [InlineData("", "value", false)]
    [InlineData("bad=name", null, false)]
    [InlineData("bad\0name", "value", false)]
    [InlineData("GOOD", "value\0INJECTED=value", false)]
    [InlineData("GOOD", "first=second\n日本語", true)]
    [InlineData("GOOD", "", true)]
    [InlineData("GOOD", null, true)]
    public void Command_environment_schema_preserves_entry_boundaries(string name, string? value, bool valid)
    {
        var command = new JsonObject { ["FileName"] = "probe",
            ["Environment"] = new JsonObject { [name] = value } };
        Assert.Equal(valid, LoadSchema().Evaluate(new JsonObject { ["Commands"] = new JsonArray(command) }).IsValid);
    }

    [Theory]
    [InlineData("FileName")]
    [InlineData("WorkingDirectory")]
    [InlineData("Arguments")]
    public void Command_launch_fields_reject_NUL(string field)
    {
        var command = new JsonObject { ["FileName"] = "probe" };
        command[field] = field == "Arguments" ? new JsonArray("value\0suffix") : JsonValue.Create("value\0suffix");
        Assert.False(LoadSchema().Evaluate(new JsonObject { ["Commands"] = new JsonArray(command) }).IsValid);
    }

    private static JsonSchema LoadSchema(string schemaName = "powerforge.release-validation.schema.json")
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) {
            var path = Path.Combine(directory.FullName, "Schemas", schemaName);
            if (File.Exists(path)) { return JsonSchema.FromText(File.ReadAllText(path)); }
        }
        throw new InvalidOperationException("Release validation schema was not found above the test output directory.");
    }

    private sealed class Runner : IProcessRunner
    {
        internal List<ProcessRunRequest> Requests { get; } = [];

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ProcessRunResult(0, "observed", "", request.FileName, TimeSpan.Zero, false));
        }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
