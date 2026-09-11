using System.IO.Compression;

namespace PowerForge.Tests;

public sealed class ReleaseValidationSymbolIdentityTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.SymbolIdentity.Tests", Guid.NewGuid().ToString("N"));
    private const string SymbolEntry = "lib/net10.0/Example.pdb";

    public ReleaseValidationSymbolIdentityTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("renamed.nupkg", "Example.1.2.3.snupkg", "Example", "1.2.3", "1.2.3")]
    [InlineData("Example.1.2.3.nupkg", "renamed.snupkg", "Example", "1.2.3", "1.2.3")]
    [InlineData("primary.NUPKG", "debug.SNUPKG", "example", "1.2.3", "1.2.3.0")]
    [InlineData("Example.nupkg", "Example.snupkg", "Example", "1.2.3-RC.1", "1.2.3-rc.1")]
    public async Task Symbols_match_nuspec_identity_independently_of_archive_names(
        string primaryName, string symbolName, string symbolId, string primaryVersion, string symbolVersion)
    {
        Package(primaryName, "Example", primaryVersion, "lib/net10.0/Example.dll");
        Package(symbolName, symbolId, symbolVersion, SymbolEntry);
        var runner = new ProbeRunner();

        var report = await Run(runner);

        AssertAccepted(report, runner, primaryVersion);
    }

    [Fact]
    public async Task Unrelated_symbol_id_and_version_do_not_hide_the_matching_package()
    {
        Package("Example.1.2.3.nupkg", "Example", "1.2.3", "lib/net10.0/Example.dll");
        // The misleading conventional filename must not outrank the actual identity.
        Package("Example.1.2.3.snupkg", "Other", "1.2.3", "lib/net10.0/Other.pdb");
        Package("older.snupkg", "Example", "1.2.2", "lib/net10.0/Old.pdb");
        Package("matching.snupkg", "Example", "1.2.3", SymbolEntry);
        var runner = new ProbeRunner();

        var report = await Run(runner);

        AssertAccepted(report, runner, "1.2.3");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("Other", "1.2.3-rc.1")]
    [InlineData("Example", "1.2.4-rc.1")]
    [InlineData("Example", "1.2.3-rc.2")]
    public async Task Missing_matching_identity_blocks_subsequent_probes(string? symbolId, string? symbolVersion)
    {
        Package("Example.nupkg", "Example", "1.2.3-rc.1", "lib/net10.0/Example.dll");
        if (symbolId is not null) {
            Package("Example.snupkg", symbolId, symbolVersion!, SymbolEntry);
        }
        var runner = new ProbeRunner();

        var report = await Run(runner);

        AssertRejected(report, runner, "missing");
    }

    [Fact]
    public async Task Two_symbol_archives_with_equivalent_identities_are_ambiguous()
    {
        Package("Example.1.2.3.nupkg", "Example", "1.2.3", "lib/net10.0/Example.dll");
        Package("Example.1.2.3.snupkg", "Example", "1.2.3", SymbolEntry);
        Package("duplicate.SNUPKG", "example", "1.2.3.0", SymbolEntry);
        var runner = new ProbeRunner();

        var report = await Run(runner);

        AssertRejected(report, runner, "ambiguous");
    }

    [Fact]
    public async Task Matched_symbol_archive_must_itself_contain_required_symbols()
    {
        Package("Example.nupkg", "Example", "1.2.3", "lib/net10.0/Example.dll");
        Package("Example.snupkg", "Example", "1.2.3", "lib/net10.0/Wrong.pdb");
        Package("unrelated.snupkg", "Other", "1.2.3", SymbolEntry);
        var runner = new ProbeRunner();

        var report = await Run(runner);

        AssertRejected(report, runner, "missing");
        Assert.Contains(SymbolEntry, report.Errors[0]);
    }

    private Task<ReleaseValidationReport> Run(ProbeRunner runner)
        => new ReleaseValidationService(runner).RunAsync(new ReleaseValidationSpec {
            Packages = new() { Path = _root, Items = [new() {
                Id = "Example", RequiredEntries = ["lib/net10.0/Example.dll"], SymbolEntries = [SymbolEntry]
            }] },
            Commands = [new() { Name = "After package acceptance", FileName = "probe", Arguments = ["{Version}"] }]
        }, request: new ReleaseValidationRequest { ProjectRoot = _root });

    private static void AssertAccepted(ReleaseValidationReport report, ProbeRunner runner, string version)
    {
        Assert.True(report.Success, string.Join("\n", report.Errors));
        Assert.Equal(version, report.Version);
        Assert.Contains($"Package Example {version}", report.Checks);
        Assert.Contains("After package acceptance", report.Checks);
        Assert.Equal(new[] { version }, Assert.Single(runner.Requests).Arguments);
    }

    private static void AssertRejected(ReleaseValidationReport report, ProbeRunner runner, string category)
    {
        Assert.False(report.Success);
        Assert.Contains(category, Assert.Single(report.Errors), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(report.Checks);
        Assert.Empty(runner.Requests);
    }

    private void Package(string fileName, string id, string version, string entry)
    {
        using var archive = ZipFile.Open(Path.Combine(_root, fileName), ZipArchiveMode.Create);
        using (var writer = new StreamWriter(archive.CreateEntry(id + ".nuspec").Open())) {
            writer.Write($"<package><metadata><id>{id}</id><version>{version}</version><authors>Tests</authors><description>Symbol identity fixture</description></metadata></package>");
        }
        using var payload = new StreamWriter(archive.CreateEntry(entry).Open());
        payload.Write("fixture payload");
    }

    private sealed class ProbeRunner : IProcessRunner
    {
        internal List<ProcessRunRequest> Requests { get; } = [];

        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(new ProcessRunResult(0, "", "", "probe", TimeSpan.Zero, false));
        }
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
