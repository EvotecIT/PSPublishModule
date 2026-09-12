using System.IO.Compression;
using System.Text.Json.Nodes;

namespace PowerForge.Tests;

public sealed class ReleaseValidationInstalledToolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.InstalledTool.Tests", Guid.NewGuid().ToString("N"));
    public ReleaseValidationInstalledToolTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("valid", false, true)]
    [InlineData("valid", true, true)]
    [InlineData("hidden-manifest", false, true)]
    [InlineData("missing-shim", false, false)]
    [InlineData("missing-shim", true, false)]
    [InlineData("empty-shim", false, false)]
    [InlineData("manifest-command", false, false)]
    [InlineData("manifest-version", false, false)]
    [InlineData("manifest-package", false, false)]
    public async Task Installation_must_produce_the_declared_command_even_without_a_toolpath_probe(string variant, bool unrelatedProbe, bool success)
    {
        using (var zip = ZipFile.Open(Path.Combine(_root, "Example.Tool.nupkg"), ZipArchiveMode.Create)) {
            using var writer = new StreamWriter(zip.CreateEntry("Example.Tool.nuspec").Open());
            writer.Write("<package><metadata><id>Example.Tool</id><version>1.2.3</version><authors>Tests</authors><description>Fixture</description></metadata></package>");
        }
        var runner = new Runner(variant);
        var report = await new ReleaseValidationService(runner).RunAsync(new() { Tools = [new() {
            PackageRoot = _root, PackageId = "Example.Tool", CommandName = "example", IncludeManifestInstall = true,
            Commands = unrelatedProbe ? [new() { FileName = "unrelated" }] : []
        }] }, request: new() { ProjectRoot = _root });

        Assert.Equal(success, report.Success);
        if (!success) { Assert.Contains("installed", Assert.Single(report.Errors), StringComparison.OrdinalIgnoreCase); }
        Assert.Equal(success ? 2 : variant.StartsWith("manifest", StringComparison.Ordinal) ? 2 : 1, runner.Installs);
        Assert.Equal(success && unrelatedProbe ? 2 : 0, runner.UnrelatedProbes);
        Assert.All(runner.Roots, path => Assert.False(Directory.Exists(path)));
        Assert.True(File.Exists(Path.Combine(_root, "Example.Tool.nupkg")));
    }

    private sealed class Runner(string variant) : IProcessRunner
    {
        internal HashSet<string> Roots { get; } = [];
        internal int Installs { get; private set; }
        internal int UnrelatedProbes { get; private set; }
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Roots.Add(request.WorkingDirectory);
            if (request.FileName == "unrelated") { UnrelatedProbes++; }
            if (request.Arguments.Contains("install")) {
                Installs++;
                ReleaseValidationToolInstallFixture.Complete(request, variant == "missing-shim" ? "other" : "example", variant == "hidden-manifest");
                if (request.Arguments.Contains("--tool-path") && variant == "empty-shim") {
                    File.WriteAllText(Path.Combine(request.WorkingDirectory, "tools", "example" + (OperatingSystem.IsWindows() ? ".exe" : "")), "");
                }
                if (request.Arguments.Contains("--local") && variant.StartsWith("manifest", StringComparison.Ordinal)) {
                    var path = Path.Combine(request.WorkingDirectory, "dotnet-tools.json");
                    var manifest = JsonNode.Parse(File.ReadAllText(path))!;
                    var tools = manifest["tools"]!.AsObject();
                    var package = tools["example.tool"]!;
                    if (variant == "manifest-command") { package["commands"] = new JsonArray("other"); }
                    if (variant == "manifest-version") { package["version"] = "9.0.0"; }
                    if (variant == "manifest-package") { tools.Remove("example.tool"); tools["other"] = package; }
                    File.WriteAllText(path, manifest.ToJsonString());
                }
            }
            return Task.FromResult(new ProcessRunResult(0, "installed", "", request.FileName, TimeSpan.Zero, false));
        }
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
