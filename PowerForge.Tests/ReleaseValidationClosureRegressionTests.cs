using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;

namespace PowerForge.Tests;

public sealed partial class ReleaseValidationClosureRegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.Closure.Tests", Guid.NewGuid().ToString("N"));
    public ReleaseValidationClosureRegressionTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void Tools_lane_preserves_explicit_strictness_and_staged_set_validation(bool strict, bool wrongSet, bool expectedSuccess)
    {
        var categories = new[] { PowerForgeReleaseAssetCategory.Tool, PowerForgeReleaseAssetCategory.Metadata,
            PowerForgeReleaseAssetCategory.Portable, PowerForgeReleaseAssetCategory.Installer, PowerForgeReleaseAssetCategory.Store };
        var entries = categories.Select(category => new {
            category = category.ToString(), target = "app", runtime = "win-x64", style = "Portable", version = "1.2.3",
            stagedPath = Payload(category + ".bin")
        }).ToArray();
        var manifest = Payload("manifest.json", JsonSerializer.Serialize(new { assetEntries = entries }));
        var config = Payload("validation.json", ReleaseValidationService.Serialize(new() { CliArtifacts = new() {
            ManifestPath = manifest, Target = "app", Runtimes = ["win-x64"], Styles = ["Portable"], ToolsOnly = strict
        } }));
        var context = Context();
        context.ModuleSelected = false;
        context.PackagesSelected = false;
        context.ToolsSelected = true;
        context.StagingRoot = _root;
        context.StagedAssets = entries.Select(entry => entry.stagedPath).ToArray();
        if (wrongSet) context.StagedAssets[0] = Payload("undeclared.bin");
        context.AssetEntries = [new() { Category = PowerForgeReleaseAssetCategory.Tool, Target = "app", Version = "1.2.3" }];

        var result = new PowerForgeReleaseValidationService(new NullLogger(), new Runner((_, _) => throw new InvalidOperationException("Unexpected process")))
            .Run(new() { ConfigPath = config }, context, _root, CancellationToken.None);

        Assert.Equal(expectedSuccess, result.Succeeded);
        if (expectedSuccess) Assert.Equal("CLI app: 1 artifacts", result.StdOut);
        else Assert.Contains(strict ? "Tools-only" : "asset set", result.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Tool_install_selects_only_primary_package_and_rejects_duplicate_primaries(bool duplicatePrimary)
    {
        var primary = Package("Example.Tool.1.2.3.nupkg");
        Package("Example.Tool.1.2.3.symbols.nupkg");
        Package("Example.Tool.1.2.3.snupkg");
        if (duplicatePrimary) Package("duplicate.nupkg");
        var workspaces = new List<string>();
        var runner = new Runner((request, _) => {
            Assert.Equal(new[] { "tool", "install", "Example.Tool" }, request.Arguments.Take(3));
            workspaces.Add(request.WorkingDirectory);
            var configIndex = Array.IndexOf(request.Arguments.ToArray(), "--configfile");
            Assert.True(configIndex >= 0);
            var config = XDocument.Load(request.Arguments[configIndex + 1]);
            var source = Assert.Single(config.Root!.Element("packageSources")!.Elements("add"));
            var feed = source.Attribute("value")!.Value;
            var selected = Assert.Single(Directory.GetFiles(feed));
            Assert.Equal(Path.GetFileName(primary), Path.GetFileName(selected));
            Assert.Equal(File.ReadAllBytes(primary), File.ReadAllBytes(selected));
            Assert.Equal("Example.Tool", Assert.Single(config.Root.Element("packageSourceMapping")!.Elements("packageSource")).Element("package")!.Attribute("pattern")!.Value);
            return Task.FromResult(Success());
        });

        var report = await new ReleaseValidationService(runner).RunAsync(new() {
            Tools = [new() { PackageRoot = _root, PackageId = "Example.Tool", CommandName = "example" }]
        }, request: new() { ProjectRoot = _root });

        Assert.Equal(!duplicatePrimary, report.Success);
        if (duplicatePrimary) { Assert.Empty(workspaces); Assert.Contains("exactly one", Assert.Single(report.Errors)); }
        else { Assert.Single(workspaces); Assert.Equal("1.2.3", report.Version); }
        Assert.All(workspaces, workspace => Assert.False(Directory.Exists(workspace)));
        Assert.True(File.Exists(primary));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    [InlineData(-1)]
    public void Script_removing_its_context_does_not_mask_result_or_cancellation(int exitCode)
    {
        var script = Payload("probe.ps1", "'fixture'");
        using var cancellation = new CancellationTokenSource();
        var runner = new Runner((request, _) => {
            var contextPath = request.EnvironmentVariables!["POWERFORGE_CONTEXT"]!;
            Assert.True(File.Exists(contextPath));
            Directory.Delete(Path.GetDirectoryName(contextPath)!, recursive: true);
            if (exitCode == -1) cancellation.Cancel();
            return Task.FromResult(new ProcessRunResult(exitCode, "original stdout", "original stderr", "probe", TimeSpan.Zero, false));
        });
        PowerForgeReleaseValidationResult Run() => new PowerForgeReleaseValidationService(new NullLogger(), runner)
            .Run(new() { FilePath = script }, Context(), _root, cancellation.Token);

        if (exitCode == -1) Assert.ThrowsAny<OperationCanceledException>(Run);
        else {
            var result = Run();
            Assert.Equal(exitCode == 0, result.Succeeded);
            Assert.Equal(exitCode, result.ExitCode);
            Assert.Equal("original stdout", result.StdOut);
            Assert.Equal("original stderr", result.StdErr);
        }
        Assert.True(File.Exists(script));
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3", true)]
    [InlineData("1.2.3", "1.2.4", false)]
    [InlineData("", "1.2.3", false)]
    [InlineData("1.2.3", "", false)]
    public async Task Standalone_unified_cli_infers_and_propagates_one_complete_version(string firstVersion, string secondVersion, bool expectedSuccess)
    {
        var entries = new[] { (Runtime: "win-x64", Version: firstVersion), (Runtime: "linux-x64", Version: secondVersion) }
            .Select(item => new { category = "Tool", target = "app", runtime = item.Runtime, style = "Portable", version = item.Version,
                stagedPath = Payload(item.Runtime + ".zip") }).ToArray();
        var manifest = Payload("manifest.json", JsonSerializer.Serialize(new { assetEntries = entries }));
        var calls = new List<ProcessRunRequest>();
        var report = await new ReleaseValidationService(new Runner((request, _) => {
            calls.Add(request);
            return Task.FromResult(new ProcessRunResult(0, "1.2.3", "", "probe", TimeSpan.Zero, false));
        })).RunAsync(new() {
            CliArtifacts = new() { ManifestPath = manifest, Target = "app", Runtimes = ["win-x64", "linux-x64"], Styles = ["Portable"] },
            Commands = [new() { FileName = "probe", Arguments = ["{Version}"], ExpectedOutput = "{Version}" }]
        }, request: new() { ProjectRoot = _root });

        Assert.Equal(expectedSuccess, report.Success);
        if (expectedSuccess) { Assert.Equal("1.2.3", report.Version); Assert.Equal(new[] { "1.2.3" }, Assert.Single(calls).Arguments); }
        else { Assert.Empty(calls); Assert.Single(report.Errors); }
    }

    [WindowsFact]
    public void Locked_context_file_does_not_mask_the_original_script_failure()
    {
        FileStream? locked = null;
        string? contextDirectory = null;
        var runner = new Runner((request, _) => {
            var contextPath = request.EnvironmentVariables!["POWERFORGE_CONTEXT"]!;
            contextDirectory = Path.GetDirectoryName(contextPath)!;
            locked = new FileStream(contextPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return Task.FromResult(new ProcessRunResult(23, "original output", "original failure", "probe", TimeSpan.Zero, false));
        });
        try {
            var result = new PowerForgeReleaseValidationService(new NullLogger(), runner)
                .Run(new() { FilePath = Payload("probe.ps1") }, Context(), _root, CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Equal(23, result.ExitCode);
            Assert.Equal("original output", result.StdOut);
            Assert.Equal("original failure", result.StdErr);
        }
        finally {
            locked?.Dispose();
            if (contextDirectory is not null && Directory.Exists(contextDirectory)) Directory.Delete(contextDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Raw_publish_manifest_does_not_require_unavailable_version_metadata()
    {
        var manifest = Payload("publish.json", JsonSerializer.Serialize(new[] { new {
            category = "Publish", target = "app", runtime = "win-x64", style = "Portable", zipPath = Payload("app.zip")
        } }));
        var report = await new ReleaseValidationService().RunAsync(new() { CliArtifacts = new() {
            ManifestPath = manifest, Target = "app", Runtimes = ["win-x64"], Styles = ["Portable"]
        } }, request: new() { ProjectRoot = _root });

        Assert.True(report.Success, string.Join("\n", report.Errors));
        Assert.Equal(string.Empty, report.Version);
        Assert.Equal("CLI app: 1 artifacts", Assert.Single(report.Checks));
    }

    private string Payload(string name, string content = "fixture")
    {
        var path = Path.Combine(_root, name);
        File.WriteAllText(path, content);
        return path;
    }
    private string Package(string name)
    {
        var path = Path.Combine(_root, name);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry("Example.Tool.nuspec").Open());
        writer.Write("<package><metadata><id>Example.Tool</id><version>1.2.3</version><authors>Tests</authors><description>Fixture</description></metadata></package>");
        return path;
    }
    private PowerForgeReleaseValidationContext Context() => new() { ProjectRoot = _root, ResolvedVersion = "1.2.3" };
    private static ProcessRunResult Success() => new(0, "ok", "", "probe", TimeSpan.Zero, false);
    private sealed class Runner(Func<ProcessRunRequest, CancellationToken, Task<ProcessRunResult>> execute) : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default) => execute(request, cancellationToken);
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
