using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace PowerForge.Tests;

public sealed class ReleaseValidationMatrixAdmissionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.MatrixAdmission.Tests", Guid.NewGuid().ToString("N"));

    public ReleaseValidationMatrixAdmissionTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(null, null, null, false)]
    [InlineData("[]", "[]", null, false)]
    [InlineData("[\"win-x64\"]", null, null, false)]
    [InlineData(null, "[\"Portable\"]", null, false)]
    [InlineData("[]", "[\"Portable\"]", null, false)]
    [InlineData("[\"win-x64\"]", "[]", null, false)]
    [InlineData("null", "[\"Portable\"]", null, false)]
    [InlineData("[\"win-x64\"]", "null", null, false)]
    [InlineData("[\"win-x64\"]", "[\"Portable\"]", null, true)]
    [InlineData("[\"win-x64\"]", "[\"Portable\"]", "[]", true)]
    [InlineData("[\"win-x64\"]", "[\"Portable\"]", "[\"net8.0\"]", true)]
    public async Task Explicit_matrix_schema_matches_loaded_runtime(string? runtimes, string? styles, string? frameworks, bool valid)
    {
        File.WriteAllText(Path.Combine(_root, "probe.exe"), "payload");
        File.WriteAllText(Path.Combine(_root, "manifest.json"),
            "[{\"category\":\"Publish\",\"target\":\"probe\",\"runtime\":\"win-x64\",\"framework\":\"net8.0\",\"style\":\"Portable\",\"exePath\":\"probe.exe\"}]");
        var cli = new JsonObject { ["Target"] = "probe", ["ManifestPath"] = "manifest.json" };
        AddJson(cli, "Runtimes", runtimes);
        AddJson(cli, "Styles", styles);
        AddJson(cli, "Frameworks", frameworks);
        var document = new JsonObject { ["CliArtifacts"] = cli };
        var path = Path.Combine(_root, "validation.json");
        File.WriteAllText(path, document.ToJsonString());

        var report = await new ReleaseValidationService().RunAsync(ReleaseValidationService.Load(path), path);

        Assert.Equal(valid, Evaluate("powerforge.release-validation.schema.json", document));
        Assert.Equal(valid, report.Success);
        if (valid) {
            Assert.Empty(report.Errors);
            Assert.Equal("CLI probe: 1 artifacts", Assert.Single(report.Checks));
        } else {
            Assert.Empty(report.Checks);
            Assert.Contains(runtimes == "null" || styles == "null" ? "nonempty strings" : "unambiguous runtime/style matrix",
                Assert.Single(report.Errors));
        }
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", false)]
    [InlineData(" \t\r\n", false)]
    public async Task Explicit_blank_publish_path_does_not_fall_back_to_a_valid_matrix(string? publishPath, bool valid)
    {
        File.WriteAllText(Path.Combine(_root, "probe.exe"), "payload");
        File.WriteAllText(Path.Combine(_root, "manifest.json"),
            "[{\"category\":\"Publish\",\"target\":\"probe\",\"runtime\":\"win-x64\",\"style\":\"Portable\",\"exePath\":\"probe.exe\"}]");
        var report = await new ReleaseValidationService().RunAsync(new() {
            CliArtifacts = new() { Target = "probe", ManifestPath = "manifest.json", PublishConfigPath = publishPath,
                Runtimes = ["win-x64"], Styles = ["Portable"] }
        }, request: new() { ProjectRoot = _root });

        Assert.Equal(valid, report.Success);
        if (valid) {
            Assert.Empty(report.Errors);
            Assert.Equal("CLI probe: 1 artifacts", Assert.Single(report.Checks));
        } else {
            Assert.Single(report.Errors);
            Assert.Empty(report.Checks);
        }
    }

    [Theory]
    [InlineData(null, false, false)]
    [InlineData("null", false, false)]
    [InlineData(null, true, true)]
    [InlineData("null", true, true)]
    [InlineData("\"\"", false, false)]
    [InlineData("\" \\t\\r\\n\"", false, false)]
    [InlineData("\"\"", true, false)]
    [InlineData("\" \\t\\r\\n\"", true, false)]
    [InlineData("\"publish.json\"", false, true)]
    [InlineData("\"publish.json\"", true, true)]
    public void Publish_config_can_supply_the_matrix_but_a_supplied_path_must_be_nonblank(string? pathJson, bool matrix, bool valid)
    {
        var cli = new JsonObject { ["Target"] = "probe", ["Runtimes"] = new JsonArray(), ["Styles"] = new JsonArray() };
        AddJson(cli, "PublishConfigPath", pathJson);
        if (matrix) {
            cli["Runtimes"] = new JsonArray("win-x64");
            cli["Styles"] = new JsonArray("Portable");
        }

        Assert.Equal(valid, Evaluate("powerforge.release-validation.schema.json", new JsonObject { ["CliArtifacts"] = cli }));
    }

    [Theory]
    [InlineData("FilePath", null, false)]
    [InlineData("ConfigPath", null, false)]
    [InlineData("FilePath", "null", false)]
    [InlineData("ConfigPath", "null", false)]
    [InlineData("FilePath", "\"\"", false)]
    [InlineData("ConfigPath", "\"\"", false)]
    [InlineData("FilePath", "\" \\t\\r\\n\"", false)]
    [InlineData("ConfigPath", "\" \\t\\r\\n\"", false)]
    [InlineData("FilePath", "\"probe.ps1\"", true)]
    [InlineData("ConfigPath", "\"probe.json\"", true)]
    public void Staging_action_path_schema_matches_runtime_admission(string property, string? valueJson, bool valid)
    {
        File.WriteAllText(Path.Combine(_root, "probe.ps1"), "'observed'");
        File.WriteAllText(Path.Combine(_root, "probe.json"), "{\"Commands\":[{\"Name\":\"Probe\",\"FileName\":\"probe\"}]}");
        var actionJson = new JsonObject();
        AddJson(actionJson, property, valueJson);
        var action = JsonSerializer.Deserialize<PowerForgeReleaseValidationAction>(actionJson.ToJsonString())!;
        var runner = new Runner();
        var service = new PowerForgeReleaseValidationService(new NullLogger(), runner);

        Assert.Equal(valid, Evaluate("powerforge.release.schema.json",
            new JsonObject { ["AfterStaging"] = new JsonArray(actionJson) }, "Validation"));
        if (valid) {
            var result = service.Run(action, new() { ProjectRoot = _root }, _root, CancellationToken.None);
            Assert.True(result.Succeeded, result.StdErr);
            Assert.Single(runner.Requests);
            Assert.Equal(property == "FilePath" ? "observed" : "Probe", result.StdOut);
        } else {
            var error = Assert.Throws<InvalidOperationException>(() =>
                service.Run(action, new() { ProjectRoot = _root }, _root, CancellationToken.None));
            Assert.Contains("exactly one of FilePath or ConfigPath", error.Message);
            Assert.Empty(runner.Requests);
        }
    }

    private static void AddJson(JsonObject target, string property, string? json)
    {
        if (json is not null) { target[property] = JsonNode.Parse(json); }
    }

    private static bool Evaluate(string schemaName, JsonNode document, string? property = null)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) {
            var path = Path.Combine(directory.FullName, "Schemas", schemaName);
            if (File.Exists(path)) {
                var schemaText = File.ReadAllText(path);
                if (property is not null) { schemaText = JsonNode.Parse(schemaText)!["properties"]![property]!.ToJsonString(); }
                return JsonSchema.FromText(schemaText).Evaluate(document).IsValid;
            }
        }
        throw new InvalidOperationException("Release schema was not found above the test output directory.");
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
