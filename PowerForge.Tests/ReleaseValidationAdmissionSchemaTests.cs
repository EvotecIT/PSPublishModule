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
        var document = new JsonObject { ["Consumers"] = new JsonArray(consumer) };

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

    private static JsonSchema LoadSchema()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) {
            var path = Path.Combine(directory.FullName, "Schemas", "powerforge.release-validation.schema.json");
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
