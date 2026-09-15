using System.IO.Compression;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class ReleaseValidationMixedVersionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.MixedVersion.Tests", Guid.NewGuid().ToString("N"));
    public ReleaseValidationMixedVersionTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Mixed_packages_do_not_supply_a_global_version_to_other_artifacts_or_commands(bool reverse)
    {
        var packages = Packages(reverse);
        var module = Path.Combine(_root, "Example.psd1");
        File.WriteAllText(module, "@{ ModuleVersion = '3.4.5' }");
        var payload = Path.Combine(_root, "app.zip");
        File.WriteAllText(payload, "payload");
        var manifest = Path.Combine(_root, "manifest.json");
        File.WriteAllText(manifest, JsonSerializer.Serialize(new { assetEntries = new[] { new {
            category = "Tool", target = "app", runtime = "any", style = "Portable", version = "4.5.6", path = payload
        } } }));
        var runner = new Runner();
        var tools = new[] { Tool("First"), Tool("Second") };
        if (reverse) { Array.Reverse(tools); }
        var report = await new ReleaseValidationService(runner).RunAsync(new() {
            Packages = packages,
            Modules = [new() { Path = _root, Manifest = "Example.psd1", ProbeScript = "{Version}.ps1", Hosts = ["module-probe"] }],
            CliArtifacts = new() { ManifestPath = manifest, Target = "app", Runtimes = ["any"], Styles = ["Portable"] },
            Tools = tools,
            Commands = [new() { FileName = "release-probe", Arguments = ["{Version}"] }]
        }, request: new() { ProjectRoot = _root });

        Assert.True(report.Success, string.Join("; ", report.Errors));
        Assert.Equal("", report.Version);
        Assert.Equal("", Assert.Single(runner.Requests.Single(request => request.FileName == "release-probe").Arguments));
        var probes = runner.Requests.Where(request => request.FileName == "tool-probe").ToArray();
        Assert.Equal(2, probes.Length);
        Assert.Equal(new[] { "1.2.3", "2.3.4" }, probes.Select(request => request.Arguments.Single()).OrderBy(value => value));
        Assert.Equal("3.4.5.ps1", Path.GetFileName(runner.Requests.Single(request => request.FileName == "module-probe").Arguments.Last()));
        Assert.Contains("CLI app: 1 artifacts", report.Checks);
        Assert.All(runner.Requests.Where(request => request.FileName == "dotnet"), request => Assert.False(Directory.Exists(request.WorkingDirectory)));
    }

    [Theory]
    [InlineData(null, false, false)]
    [InlineData(null, true, true)]
    [InlineData("2.3.4", true, true)]
    [InlineData("9.0.0", true, false)]
    public async Task Common_or_explicit_versions_remain_authoritative(string? expected, bool mixed, bool success)
    {
        var packages = Packages(reverse: false);
        packages.SameVersion = !mixed;
        var runner = new Runner();
        var report = await new ReleaseValidationService(runner).RunAsync(new() {
            Packages = packages, Tools = [Tool("Second")]
        }, request: new() { ProjectRoot = _root, Version = expected });
        Assert.Equal(success, report.Success);
        if (success) {
            Assert.Equal(expected ?? "", report.Version);
            Assert.Equal("2.3.4", Assert.Single(runner.Requests.Single(request => request.FileName == "tool-probe").Arguments));
        } else {
            Assert.Contains("does not match", Assert.Single(report.Errors));
            Assert.Empty(runner.Requests);
        }
    }

    private PackageSetValidation Packages(bool reverse)
    {
        foreach (var (id, version) in new[] { ("First", "1.2.3"), ("Second", "2.3.4") }) {
            using var zip = ZipFile.Open(Path.Combine(_root, id + ".nupkg"), ZipArchiveMode.Create);
            using var writer = new StreamWriter(zip.CreateEntry(id + ".nuspec").Open());
            writer.Write($"<package><metadata><id>{id}</id><version>{version}</version><authors>Tests</authors><description>Fixture</description></metadata></package>");
        }
        var items = new[] { new PackageArtifactContract { Id = "First" }, new PackageArtifactContract { Id = "Second" } };
        if (reverse) { Array.Reverse(items); }
        return new() { Path = _root, SameVersion = false, Items = items };
    }

    private DotNetToolValidation Tool(string id) => new() { PackageRoot = _root, PackageId = id, CommandName = "example",
        Commands = [new() { FileName = "tool-probe", Arguments = ["{Version}"] }] };

    private sealed class Runner : IProcessRunner
    {
        internal List<ProcessRunRequest> Requests { get; } = [];
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            ReleaseValidationToolInstallFixture.Complete(request, "example");
            return Task.FromResult(new ProcessRunResult(0, "", "", request.FileName, TimeSpan.Zero, false));
        }
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
