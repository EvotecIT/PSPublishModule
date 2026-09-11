using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace PowerForge.Tests;

public sealed class ReleaseValidationSchemaDependencyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.SchemaDependency.Tests", Guid.NewGuid().ToString("N"));

    public ReleaseValidationSchemaDependencyTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("", true)]
    [InlineData(",\"Environment\":{},\"WorkingDirectory\":null,\"PreferWindowsPowerShell\":false", true)]
    [InlineData(",\"WorkingDirectory\":\"\"", true)]
    [InlineData(",\"Environment\":{},\"WorkingDirectory\":\" \\t\"", true)]
    [InlineData(",\"Environment\":{\"NAME\":\"value\"}", false)]
    [InlineData(",\"Environment\":{\"NAME\":null}", false)]
    [InlineData(",\"WorkingDirectory\":\".\"", false)]
    [InlineData(",\"PreferWindowsPowerShell\":true", false)]
    public void Config_actions_schema_and_runtime_reject_only_effective_script_options(string options, bool expectedValid)
    {
        Write("validation.json", "{\"Commands\":[{\"Name\":\"Config probe\",\"FileName\":\"probe\"}]}");
        var actionJson = "{\"ConfigPath\":\"validation.json\"" + options + "}";
        var schemaValid = EvaluateAction(actionJson);
        var action = JsonSerializer.Deserialize<PowerForgeReleaseValidationAction>(actionJson)!;
        var runner = new Runner("observed");
        var service = new PowerForgeReleaseValidationService(new NullLogger(), runner);

        if (expectedValid) {
            var result = service.Run(action, new() { ProjectRoot = _root }, _root, CancellationToken.None);
            Assert.True(result.Succeeded, result.StdErr);
            Assert.Equal("Config probe", result.StdOut);
            Assert.Equal("probe", Assert.Single(runner.Requests).FileName);
        } else {
            var error = Assert.Throws<InvalidOperationException>(() =>
                service.Run(action, new() { ProjectRoot = _root }, _root, CancellationToken.None));
            Assert.Contains("script process options", error.Message);
            Assert.Empty(runner.Requests);
        }
        Assert.Equal(expectedValid, schemaValid);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Script_actions_schema_and_runtime_preserve_process_options(bool preferWindowsPowerShell)
    {
        var script = Write("probe.ps1", "'observed'");
        var workingDirectory = Directory.CreateDirectory(Path.Combine(_root, "work")).FullName;
        var actionJson = JsonSerializer.Serialize(new {
            FilePath = "probe.ps1", WorkingDirectory = "work", Environment = new Dictionary<string, string?> {
                ["NAME"] = "value", ["REMOVED"] = null
            }, PreferWindowsPowerShell = preferWindowsPowerShell
        });
        var action = JsonSerializer.Deserialize<PowerForgeReleaseValidationAction>(actionJson)!;
        var runner = new Runner("observed");

        var result = new PowerForgeReleaseValidationService(new NullLogger(), runner)
            .Run(action, new() { ProjectRoot = _root }, _root, CancellationToken.None);

        Assert.True(EvaluateAction(actionJson));
        Assert.True(result.Succeeded, result.StdErr);
        Assert.Equal("observed", result.StdOut);
        var request = Assert.Single(runner.Requests);
        Assert.Equal(workingDirectory, request.WorkingDirectory);
        Assert.Equal(new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", script }, request.Arguments);
        Assert.Equal("value", request.EnvironmentVariables!["NAME"]);
        Assert.Null(request.EnvironmentVariables["REMOVED"]);
        Assert.Equal("AfterStaging", request.EnvironmentVariables["POWERFORGE_RELEASE_STAGE"]);
    }

    [Theory]
    [InlineData("\"Array\"", "1", "[1,2]", true)]
    [InlineData("\"aRrAy\"", "2", "[1,2]", true)]
    [InlineData("\"Object\"", "0", "{}", false)]
    [InlineData("\"Null\"", "1", "null", false)]
    [InlineData("null", "1", "[1,2]", true)]
    [InlineData(null, "1", "[1,2]", true)]
    [InlineData("\"Object\"", "null", "{}", true)]
    [InlineData("\"Null\"", null, "null", true)]
    [InlineData("null", "null", "not JSON", true)]
    [InlineData(null, null, "not JSON", true)]
    public async Task Json_item_count_rejects_explicit_nonarray_kind_in_schema_and_runtime(
        string? kindJson, string? minimumJson, string output, bool expectedValid)
    {
        var command = new JsonObject { ["Name"] = "Count probe", ["FileName"] = "probe" };
        if (kindJson is not null) { command["OutputJsonKind"] = JsonNode.Parse(kindJson); }
        if (minimumJson is not null) { command["MinimumJsonItems"] = JsonNode.Parse(minimumJson); }
        var document = new JsonObject { ["Commands"] = new JsonArray(command) };
        var schemaValid = Evaluate("powerforge.release-validation.schema.json", document);
        var configPath = Write("command.json", document.ToJsonString());
        var runner = new Runner(output);

        var report = await new ReleaseValidationService(runner).RunAsync(
            ReleaseValidationService.Load(configPath), configPath, new() { ProjectRoot = _root });

        Assert.Equal(expectedValid, report.Success);
        if (expectedValid) {
            Assert.Equal("Count probe", Assert.Single(report.Checks));
            Assert.Empty(report.Errors);
            Assert.Equal("probe", Assert.Single(runner.Requests).FileName);
        } else {
            Assert.Contains("MinimumJsonItems requires Array", Assert.Single(report.Errors));
            Assert.Empty(report.Checks);
            Assert.Empty(runner.Requests);
        }
        Assert.Equal(expectedValid, schemaValid);
    }

    [Theory]
    [InlineData(false, "{}")]
    [InlineData(true, "null")]
    [InlineData(false, "[1]")]
    [InlineData(true, "[1]")]
    public async Task Implicit_array_contract_is_admitted_but_rejects_nonarray_or_short_output(bool explicitNullKind, string output)
    {
        var command = new JsonObject { ["FileName"] = "probe", ["MinimumJsonItems"] = 2 };
        if (explicitNullKind) { command["OutputJsonKind"] = null; }
        var document = new JsonObject { ["Commands"] = new JsonArray(command) };
        var configPath = Write("implicit-array.json", document.ToJsonString());
        var runner = new Runner(output);

        var report = await new ReleaseValidationService(runner).RunAsync(
            ReleaseValidationService.Load(configPath), configPath, new() { ProjectRoot = _root });

        Assert.True(Evaluate("powerforge.release-validation.schema.json", document));
        Assert.False(report.Success);
        Assert.Contains("required array items", Assert.Single(report.Errors));
        Assert.Empty(report.Checks);
        Assert.Equal("probe", Assert.Single(runner.Requests).FileName);
    }

    private static bool EvaluateAction(string actionJson) => Evaluate("powerforge.release.schema.json",
        JsonNode.Parse("{\"AfterStaging\":[" + actionJson + "]}")!, "Validation");

    private static bool Evaluate(string schemaName, JsonNode document, string? property = null)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) {
            var path = Path.Combine(directory.FullName, "Schemas", schemaName);
            if (File.Exists(path)) {
                var schemaText = File.ReadAllText(path);
                if (property is not null) {
                    schemaText = JsonNode.Parse(schemaText)!["properties"]![property]!.ToJsonString();
                }
                return JsonSchema.FromText(schemaText)
                    .Evaluate(document, new EvaluationOptions { OutputFormat = OutputFormat.List }).IsValid;
            }
        }
        throw new InvalidOperationException("Release schema was not found above the test output directory.");
    }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }

    private sealed class Runner(string output) : IProcessRunner
    {
        internal List<ProcessRunRequest> Requests { get; } = [];
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ProcessRunResult(0, output, "", request.FileName, TimeSpan.Zero, false));
        }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
