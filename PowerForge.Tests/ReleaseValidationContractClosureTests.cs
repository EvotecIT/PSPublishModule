using System.Text.Json;

namespace PowerForge.Tests;

public sealed class ReleaseValidationContractClosureTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.ContractClosure.Tests", Guid.NewGuid().ToString("N"));
    public ReleaseValidationContractClosureTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("runtime", "")]
    [InlineData("style", " ")]
    [InlineData("framework", null)]
    [InlineData("valid", "valid")]
    public async Task Explicit_matrix_values_cannot_match_absent_or_blank_manifest_fields(string field, string? invalid)
    {
        var entry = new Dictionary<string, object?> {
            ["category"] = "Publish", ["target"] = "app", ["runtime"] = "linux-x64", ["framework"] = "net10.0",
            ["style"] = "Portable", ["zipPath"] = Write("app.zip", "payload")
        };
        var contract = new CliArtifactValidation { Target = "app", Runtimes = ["linux-x64"], Frameworks = ["net10.0"], Styles = ["Portable"] };
        if (field != "valid") {
            if (invalid is null) { entry.Remove(field); } else { entry[field] = invalid; }
            if (field == "runtime") { contract.Runtimes = [invalid!]; }
            if (field == "style") { contract.Styles = [invalid!]; }
            if (field == "framework") { contract.Frameworks = [invalid!]; }
        }
        contract.ManifestPath = Write("manifest.json", JsonSerializer.Serialize(new[] { entry }));

        var report = await RunCliAsync(contract);

        Assert.Equal(field == "valid", report.Success);
        if (field == "valid") { Assert.Equal("CLI app: 1 artifacts", Assert.Single(report.Checks)); }
        else { Assert.Single(report.Errors); Assert.Empty(report.Checks); }
    }

    [Theory]
    [InlineData("numeric-kind")]
    [InlineData("unknown-kind")]
    [InlineData("negative-items")]
    [InlineData("null-contains")]
    [InlineData("missing-executable-skipped")]
    public async Task Invalid_command_contract_fails_before_its_probe_or_platform_skip(string variation)
    {
        var command = new ReleaseCommandValidation { FileName = "probe", Name = "Malformed probe" };
        switch (variation) {
            case "numeric-kind": command.OutputJsonKind = "2"; break;
            case "unknown-kind": command.OutputJsonKind = "Collection"; break;
            case "negative-items": command.MinimumJsonItems = -1; break;
            case "null-contains": command.OutputContains = null!; break;
            case "missing-executable-skipped":
                command.FileName = "";
                command.Platforms = [OperatingSystem.IsWindows() ? "Linux" : "Windows"];
                break;
        }
        var runner = new Runner();

        var report = await new ReleaseValidationService(runner).RunAsync(new() { Commands = [command] },
            request: new() { ProjectRoot = _root });

        Assert.False(report.Success);
        Assert.Single(report.Errors);
        Assert.Empty(report.Checks);
        Assert.Equal(0, runner.Calls);
    }

    [InputBoundaryLinkTheory]
    [InlineData("zipPath")]
    [InlineData("exePath")]
    public async Task Standalone_link_aliases_cannot_supply_distinct_artifact_slots(string property)
    {
        var payload = Write("physical.bin", "payload");
        var first = Path.Combine(_root, "first.bin");
        var second = Path.Combine(_root, "second.bin");
        File.CreateSymbolicLink(first, payload);
        File.CreateSymbolicLink(second, payload);
        try {
            var entries = new[] { (Runtime: "linux-x64", Path: first), (Runtime: "linux-arm64", Path: second) }
                .Select(item => new Dictionary<string, string> { ["category"] = "Publish", ["target"] = "app",
                    ["runtime"] = item.Runtime, ["style"] = "Portable", [property] = item.Path }).ToArray();
            var report = await RunCliAsync(new() { ManifestPath = Write("manifest.json", JsonSerializer.Serialize(entries)),
                Target = "app", Runtimes = ["linux-x64", "linux-arm64"], Styles = ["Portable"] });

            Assert.False(report.Success);
            Assert.Single(report.Errors);
            Assert.Empty(report.Checks);
            Assert.Equal("payload", File.ReadAllText(payload));
        }
        finally { File.Delete(first); File.Delete(second); }
    }

    [CaseInsensitiveMacArtifactFact]
    public async Task Readonly_directory_case_aliases_are_still_duplicate_artifacts()
    {
        if (!OperatingSystem.IsMacOS()) throw new InvalidOperationException("This capability-gated test requires macOS.");
        var directory = Directory.CreateDirectory(Path.Combine(_root, "payloads")).FullName;
        var payload = Path.Combine(directory, "Artifact.zip");
        File.WriteAllText(payload, "payload");
        var alias = Path.Combine(directory, "artifact.ZIP");
        Assert.True(File.Exists(alias));
        var manifest = Write("manifest.json", JsonSerializer.Serialize(new[] {
            new { category = "Publish", target = "app", runtime = "osx-arm64", style = "Portable", zipPath = payload },
            new { category = "Publish", target = "app", runtime = "osx-x64", style = "Portable", zipPath = alias }
        }));
        var originalMode = File.GetUnixFileMode(directory);
        try {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserExecute |
                UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            var report = await RunCliAsync(new() { ManifestPath = manifest, Target = "app",
                Runtimes = ["osx-arm64", "osx-x64"], Styles = ["Portable"] });

            Assert.False(report.Success);
            Assert.Contains("duplicat", Assert.Single(report.Errors), StringComparison.OrdinalIgnoreCase);
            Assert.Empty(report.Checks);
        }
        finally { File.SetUnixFileMode(directory, originalMode); }
    }

    private Task<ReleaseValidationReport> RunCliAsync(CliArtifactValidation contract)
        => new ReleaseValidationService().RunAsync(new() { CliArtifacts = contract }, request: new() { ProjectRoot = _root });
    private string Write(string name, string content)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }
    private sealed class Runner : IProcessRunner
    {
        internal int Calls { get; private set; }
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ProcessRunResult(0, "[]", "", request.FileName, TimeSpan.Zero, false));
        }
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
