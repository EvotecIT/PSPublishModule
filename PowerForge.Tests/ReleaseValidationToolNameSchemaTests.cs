using System.IO.Compression;
using System.Text.Json.Nodes;
using Json.Schema;

namespace PowerForge.Tests;

public sealed class ReleaseValidationToolNameSchemaTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.ToolNameSchema.Tests", Guid.NewGuid().ToString("N"));

    public ReleaseValidationToolNameSchemaTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("example", true)]
    [InlineData("example-cli.v2_test", true)]
    [InlineData("example tool", true)]
    [InlineData("example{build_id}", true)]
    [InlineData("", false)]
    [InlineData(" \t\r\n", false)]
    [InlineData("folder/tool", false)]
    [InlineData("folder\\tool", false)]
    [InlineData("C:tool", false)]
    public async Task Tool_command_name_schema_matches_runtime_admission_and_preserves_dispatch(string commandName, bool expectedValid)
    {
        var document = new JsonObject { ["Tools"] = new JsonArray(new JsonObject {
            ["PackageId"] = "Example.Tool", ["PackageRoot"] = _root, ["CommandName"] = commandName,
            ["IncludeManifestInstall"] = true,
            ["Commands"] = new JsonArray(new JsonObject { ["FileName"] = "{ToolPath}",
                ["Arguments"] = new JsonArray("--probe") })
        }) };
        var schemaValid = LoadSchema().Evaluate(document,
            new EvaluationOptions { OutputFormat = OutputFormat.List }).IsValid;
        var configPath = Path.Combine(_root, "validation.json");
        File.WriteAllText(configPath, document.ToJsonString());
        using (var archive = ZipFile.Open(Path.Combine(_root, "renamed-tool.nupkg"), ZipArchiveMode.Create)) {
            using var writer = new StreamWriter(archive.CreateEntry("Example.Tool.nuspec").Open());
            writer.Write("<package><metadata><id>Example.Tool</id><version>1.2.3</version><authors>Tests</authors><description>Fixture</description></metadata></package>");
        }
        var runner = new Runner();

        var report = await new ReleaseValidationService(runner).RunAsync(
            ReleaseValidationService.Load(configPath), configPath, new() { ProjectRoot = _root });

        Assert.Equal(expectedValid, report.Success);
        if (expectedValid) {
            Assert.Empty(report.Errors);
            Assert.Equal("1.2.3", report.Version);
            var probes = runner.Requests.Where(request => request.Arguments.Contains("--probe")).ToArray();
            Assert.Equal(2, probes.Length);
            Assert.Equal(commandName + (OperatingSystem.IsWindows() ? ".exe" : ""), Path.GetFileName(probes[0].FileName));
            Assert.Equal("dotnet", probes[1].FileName);
            Assert.Equal(new[] { "tool", "run", commandName, "--", "--probe" }, probes[1].Arguments);
            Assert.All(runner.Requests, request => Assert.False(Directory.Exists(request.WorkingDirectory)));
        } else {
            Assert.Contains("simple command name", Assert.Single(report.Errors));
            Assert.Empty(report.Checks);
            Assert.Empty(runner.Requests);
        }
        Assert.Equal(expectedValid, schemaValid);
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
            if (request.Arguments.Contains("install")) {
                var arguments = request.Arguments.ToArray();
                var config = System.Xml.Linq.XDocument.Load(arguments[Array.IndexOf(arguments, "--configfile") + 1]);
                var feed = config.Root!.Element("packageSources")!.Elements("add").Single().Attribute("value")!.Value;
                Assert.Equal("Example.Tool.1.2.3.nupkg", Path.GetFileName(Assert.Single(Directory.GetFiles(feed))));
            }
            return Task.FromResult(new ProcessRunResult(0, "", "", request.FileName, TimeSpan.Zero, false));
        }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
