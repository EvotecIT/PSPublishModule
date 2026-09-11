using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace PowerForge.Tests;

public sealed class ReleaseValidationInputBoundaryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.InputBoundary.Tests", Guid.NewGuid().ToString("N"));
    public ReleaseValidationInputBoundaryTests() => Directory.CreateDirectory(_root);

    [InputBoundaryLinkTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Module_and_unzipped_cli_reject_linked_files_and_directory_loops(bool cli, bool directoryLoop)
    {
        var input = Directory.CreateDirectory(Path.Combine(_root, "input")).FullName;
        var link = Path.Combine(input, directoryLoop ? "loop" : "linked.dll");
        if (!cli) File.WriteAllText(Path.Combine(input, "Example.psd1"), "@{ ModuleVersion = '1.2.3' }");
        if (directoryLoop) Directory.CreateSymbolicLink(link, input);
        else File.CreateSymbolicLink(link, Write("external.dll", "payload"));
        var spec = cli ? new ReleaseValidationSpec { CliArtifacts = new() {
            ManifestPath = Write("cli.json", JsonSerializer.Serialize(new[] { new {
                category = "Publish", target = "app", runtime = "linux-x64", style = "Portable", outputDir = input
            } })), Target = "app", Runtimes = ["linux-x64"], Styles = ["Portable"]
        } } : new ReleaseValidationSpec { Modules = [new() { Path = input, Manifest = "Example.psd1" }] };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try {
            var report = await Task.Run(() => new ReleaseValidationService().RunAsync(spec,
                request: new() { ProjectRoot = _root }, cancellationToken: cancellation.Token)).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.False(report.Success);
            Assert.Contains("link", Assert.Single(report.Errors), StringComparison.OrdinalIgnoreCase);
            Assert.Empty(report.Checks);
        }
        finally {
            cancellation.Cancel();
            if (directoryLoop) Directory.Delete(link); else File.Delete(link);
        }
    }

    [ReleaseValidationLinuxTheory]
    [InlineData("tool-uppercase", true)]
    [InlineData("undeclared-uppercase", false)]
    [InlineData("symbols-uppercase", true)]
    public async Task Package_discovery_handles_case_variants_without_confusing_symbols_with_primary_packages(string variation, bool expectedSuccess)
    {
        Package("Example.1.2.3" + (variation == "tool-uppercase" ? ".NUPKG" : ".nupkg"), "Example");
        if (variation == "undeclared-uppercase") Package("Unexpected.1.2.3.NUPKG", "Unexpected");
        if (variation == "symbols-uppercase") {
            Package("Example.1.2.3.SYMBOLS.NUPKG", "Example");
            Package("Example.1.2.3.SNUPKG", "Example");
        }
        var runner = new Runner();
        var spec = variation == "undeclared-uppercase"
            ? new ReleaseValidationSpec { Packages = new() { Path = _root, ExactSet = true, Items = [new() { Id = "Example" }] } }
            : new ReleaseValidationSpec { Tools = [new() { PackageRoot = _root, PackageId = "Example", CommandName = "example" }] };

        var report = await new ReleaseValidationService(runner).RunAsync(spec, request: new() { ProjectRoot = _root });

        Assert.Equal(expectedSuccess, report.Success);
        if (expectedSuccess) Assert.Equal(1, runner.Calls);
        else { Assert.Equal(0, runner.Calls); Assert.Contains("undeclared", Assert.Single(report.Errors)); }
    }

    [ReleaseValidationLinuxTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Symbol_sibling_lookup_accepts_uppercase_extension_but_rejects_case_only_duplicates(bool duplicate)
    {
        Package("Example.1.2.3.nupkg", "Example");
        Package("Example.1.2.3.SNUPKG", "Example");
        if (duplicate) Package("Example.1.2.3.snupkg", "Example");

        var report = await new ReleaseValidationService().RunAsync(new() { Packages = new() {
            Path = _root, Items = [new() { Id = "Example", SymbolEntries = ["Example.nuspec"] }]
        } }, request: new() { ProjectRoot = _root });

        Assert.Equal(!duplicate, report.Success);
        if (duplicate) { Assert.Single(report.Errors); Assert.Empty(report.Checks); }
        else Assert.Equal("Package Example 1.2.3", Assert.Single(report.Checks));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task Empty_cli_target_cannot_validate_an_equally_malformed_manifest(string target)
    {
        var manifest = Write("manifest.json", JsonSerializer.Serialize(new[] { new {
            category = "Publish", target, runtime = "linux-x64", style = "Portable", zipPath = Write("app.zip", "payload")
        } }));
        var report = await new ReleaseValidationService().RunAsync(new() { CliArtifacts = new() {
            ManifestPath = manifest, Target = target, Runtimes = ["linux-x64"], Styles = ["Portable"]
        } }, request: new() { ProjectRoot = _root });

        Assert.False(report.Success);
        Assert.Contains("target", Assert.Single(report.Errors), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(report.Checks);
    }

    [Theory]
    [InlineData("{}", false)]
    [InlineData("{\"Packages\":null,\"CliArtifacts\":null}", false)]
    [InlineData("{\"Modules\":[],\"Tools\":[],\"Consumers\":[],\"Commands\":[]}", false)]
    [InlineData("{\"Commands\":[{\"FileName\":\"probe\"}]}", true)]
    public async Task Schema_and_runtime_require_an_actual_contract(string json, bool expectedValid)
    {
        var schema = JsonSchema.FromText(File.ReadAllText(FindSchema()));
        var evaluation = schema.Evaluate(JsonNode.Parse(json)!, new EvaluationOptions { OutputFormat = OutputFormat.List });
        var config = Write("validation.json", json);
        var runner = new Runner();
        var report = await new ReleaseValidationService(runner).RunAsync(ReleaseValidationService.Load(config), config,
            new() { ProjectRoot = _root });

        Assert.Equal(expectedValid, evaluation.IsValid);
        Assert.Equal(expectedValid, report.Success);
        Assert.Equal(expectedValid ? 1 : 0, runner.Calls);
        if (!expectedValid) Assert.Single(report.Errors);
    }

    [CaseInsensitiveMacArtifactFact]
    public async Task Case_aliases_cannot_supply_two_distinct_cli_artifacts()
    {
        var payload = Write("Artifact.zip", "payload");
        var alias = Path.Combine(_root, "artifact.ZIP");
        Assert.True(File.Exists(alias), "The selected filesystem must be case insensitive.");
        var manifest = Write("manifest.json", JsonSerializer.Serialize(new[] {
            new { category = "Publish", target = "app", runtime = "osx-arm64", style = "Portable", zipPath = payload },
            new { category = "Publish", target = "app", runtime = "osx-x64", style = "Portable", zipPath = alias }
        }));
        var report = await new ReleaseValidationService().RunAsync(new() { CliArtifacts = new() {
            ManifestPath = manifest, Target = "app", Runtimes = ["osx-arm64", "osx-x64"], Styles = ["Portable"]
        } }, request: new() { ProjectRoot = _root });

        Assert.False(report.Success);
        Assert.Contains("duplicat", Assert.Single(report.Errors), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(report.Checks);
    }

    private void Package(string name, string id)
    {
        using var zip = ZipFile.Open(Path.Combine(_root, name), ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry(id + ".nuspec").Open());
        writer.Write($"<package><metadata><id>{id}</id><version>1.2.3</version><authors>Tests</authors><description>Fixture</description></metadata></package>");
    }
    private string Write(string name, string content) { var path = Path.Combine(_root, name); File.WriteAllText(path, content); return path; }
    private static string FindSchema()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent) {
            var schema = Path.Combine(directory.FullName, "Schemas", "powerforge.release-validation.schema.json");
            if (File.Exists(schema)) return schema;
        }
        throw new InvalidOperationException("Schema not found.");
    }
    private sealed class Runner : IProcessRunner
    {
        internal int Calls { get; private set; }
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default) {
            Calls++;
            return Task.FromResult(new ProcessRunResult(0, "ok", "", request.FileName, TimeSpan.Zero, false));
        }
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}

internal sealed class CaseInsensitiveMacArtifactFactAttribute : FactAttribute
{
    public CaseInsensitiveMacArtifactFactAttribute()
    {
        if (!OperatingSystem.IsMacOS()) { Skip = "Requires a case-insensitive macOS filesystem."; return; }
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.CaseCapability." + Guid.NewGuid().ToString("N"))).FullName;
        try {
            File.WriteAllText(Path.Combine(root, "Probe"), "fixture");
            if (!File.Exists(Path.Combine(root, "probe"))) Skip = "The temporary filesystem is case sensitive.";
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}

internal sealed class InputBoundaryLinkTheoryAttribute : TheoryAttribute
{
    public InputBoundaryLinkTheoryAttribute()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.LinkCapability." + Guid.NewGuid().ToString("N"))).FullName;
        var link = Path.Combine(root, "link");
        try {
            var target = Path.Combine(root, "target");
            File.WriteAllText(target, "fixture");
            File.CreateSymbolicLink(link, target);
        }
        catch (UnauthorizedAccessException) { Skip = "Symbolic-link creation is not permitted for this test host."; }
        catch (PlatformNotSupportedException) { Skip = "The test filesystem does not support symbolic links."; }
        finally {
            if (File.Exists(link)) File.Delete(link);
            Directory.Delete(root, recursive: true);
        }
    }
}
