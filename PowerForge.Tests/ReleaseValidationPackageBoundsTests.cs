using System.IO.Compression;

namespace PowerForge.Tests;

public sealed class ReleaseValidationPackageBoundsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.PackageBounds.Tests", Guid.NewGuid().ToString("N"));
    public ReleaseValidationPackageBoundsTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("primary", "../outside")]
    [InlineData("symbol", "../outside")]
    [InlineData("tool", "../outside")]
    [InlineData("primary", "..\\outside")]
    [InlineData("symbol", "..\\outside")]
    [InlineData("tool", "..\\outside")]
    public async Task Unsafe_embedded_package_identity_is_rejected_before_feed_creation_or_probes(string lane, string id)
    {
        WritePackage(Path.Combine(_root, "renamed.nupkg"), lane == "symbol" ? "Example" : id, "1.2.3", null);
        if (lane == "symbol") { WritePackage(Path.Combine(_root, "renamed.snupkg"), id, "1.2.3", null); }
        var runner = new Runner();
        var spec = lane == "tool"
            ? new ReleaseValidationSpec { Tools = [new() { PackageRoot = _root, PackageId = id, CommandName = "example" }] }
            : new ReleaseValidationSpec { Packages = new() { Path = _root, Items = [new() {
                Id = lane == "symbol" ? "Example" : id, SymbolEntries = lane == "symbol" ? ["*.nuspec"] : []
            }] } };
        var report = await new ReleaseValidationService(runner).RunAsync(spec, request: new() { ProjectRoot = _root });
        Assert.False(report.Success);
        Assert.Contains("package ID", Assert.Single(report.Errors), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(report.Checks);
        Assert.Equal(0, runner.Calls);
        Assert.All(Directory.GetFiles(_root), path => Assert.StartsWith("renamed.", Path.GetFileName(path)));
    }

    [Theory]
    [InlineData("primary-entries")]
    [InlineData("tool-entries")]
    [InlineData("symbol-entries")]
    [InlineData("primary-metadata")]
    [InlineData("tool-metadata")]
    [InlineData("symbol-metadata")]
    public async Task Package_inventory_and_nuspec_bounds_fail_before_any_probe(string variation)
    {
        var symbols = variation.StartsWith("symbol", StringComparison.Ordinal);
        WritePackage(Path.Combine(_root, "Example.1.2.3.nupkg"), "Example", "1.2.3", symbols ? null : variation);
        if (symbols) { WritePackage(Path.Combine(_root, "Example.1.2.3.snupkg"), "Example", "1.2.3", variation); }
        var runner = new Runner();
        var spec = variation.StartsWith("tool", StringComparison.Ordinal)
            ? new ReleaseValidationSpec { Tools = [new() { PackageRoot = _root, PackageId = "Example", CommandName = "example" }] }
            : new ReleaseValidationSpec { Packages = new() { Path = _root, Items = [new() {
                Id = "Example", SymbolEntries = symbols ? ["Example.nuspec"] : []
            }] }, Commands = [new() { FileName = "probe" }] };

        var report = await new ReleaseValidationService(runner).RunAsync(spec, request: new() { ProjectRoot = _root });

        Assert.False(report.Success);
        Assert.Single(report.Errors);
        Assert.Contains("limit", report.Errors[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, runner.Calls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Distinct_package_versions_restore_and_run_without_a_global_version_override(bool releaseOnly, bool renamed)
    {
        WritePackage(Path.Combine(_root, "First.1.2.3.nupkg"), "First", "1.2.3", null);
        WritePackage(Path.Combine(_root, "Second.2.3.4.nupkg"), "Second", "2.3.4", null);
        if (renamed) {
            File.Move(Path.Combine(_root, "First.1.2.3.nupkg"), Path.Combine(_root, "first-renamed.nupkg"));
            File.Move(Path.Combine(_root, "Second.2.3.4.nupkg"), Path.Combine(_root, "second-renamed.NUPKG"));
        }
        var source = Directory.CreateDirectory(Path.Combine(_root, "consumer")).FullName;
        File.WriteAllText(Path.Combine(source, "Probe.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><FirstVersion>1.2.3</FirstVersion><SecondVersion>2.3.4</SecondVersion></PropertyGroup>
              <PropertyGroup Condition="'$(PackageVersion)' == '1.2.3'"><FirstVersion>$(PackageVersion)</FirstVersion><SecondVersion>$(PackageVersion)</SecondVersion></PropertyGroup>
              <ItemGroup><PackageReference Include="First" Version="[$(FirstVersion)]"/><PackageReference Include="Second" Version="[$(SecondVersion)]"/></ItemGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(source, "Program.cs"), "System.Console.WriteLine(\"distinct-versions-restored\");");
        if (releaseOnly) {
            var project = System.Xml.Linq.XDocument.Load(Path.Combine(source, "Probe.csproj"));
            project.Root!.Element("ItemGroup")!.SetAttributeValue("Condition", "'$(Configuration)' == 'Release'");
            project.Save(Path.Combine(source, "Probe.csproj"));
        }

        var report = await new ReleaseValidationService().RunAsync(new() {
            Packages = new() { Path = _root, SameVersion = false, Items = [new() { Id = "First" }, new() { Id = "Second" }] },
            Consumers = [new() { SourceDirectory = source, ProjectFile = "Probe.csproj", Frameworks = ["net10.0"], DependencySources = [] }]
        }, request: new() { ProjectRoot = _root }).WaitAsync(TimeSpan.FromSeconds(120));

        Assert.True(report.Success, string.Join("\n", report.Errors));
        Assert.Contains("Restore package consumer", report.Checks);
        Assert.Contains("Run package consumer net10.0", report.Checks);
    }

    private static void WritePackage(string path, string id, string version, string? variation)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        var metadata = zip.CreateEntry("fixture.nuspec", CompressionLevel.Optimal);
        using (var writer = new StreamWriter(metadata.Open())) {
            writer.Write($"<package><metadata><id>{id}</id><version>{version}</version><authors>Tests</authors><description>");
            if (variation?.EndsWith("metadata", StringComparison.Ordinal) == true) {
                var chunk = new string('x', 8192);
                for (var index = 0; index < 513; index++) { writer.Write(chunk); }
            } else { writer.Write("Fixture"); }
            writer.Write("</description></metadata></package>");
        }
        if (variation?.EndsWith("entries", StringComparison.Ordinal) == true) {
            for (var index = 0; index < 65536; index++) { zip.CreateEntry("content/" + index); }
        }
        // A harmless package payload makes these valid dependency packages for the real restore fixture.
        else { using var writer = new StreamWriter(zip.CreateEntry("content/fixture.txt").Open()); writer.Write("fixture"); }
    }

    [Fact]
    public async Task Oversized_central_directory_is_rejected_but_large_payload_is_not_metadata()
    {
        var path = Path.Combine(_root, "Example.1.2.3.nupkg");
        WritePackage(path, "Example", "1.2.3", null);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update)) {
            var name = new string('x', 20000);
            for (var index = 0; index < 900; index++) { zip.CreateEntry("content/" + index + name); }
        }
        var spec = new ReleaseValidationSpec { Packages = new() { Path = _root, Items = [new() { Id = "Example" }] } };
        var rejected = await new ReleaseValidationService().RunAsync(spec);
        Assert.False(rejected.Success);
        Assert.Contains("metadata", Assert.Single(rejected.Errors), StringComparison.OrdinalIgnoreCase);
        File.Delete(path);
        WritePackage(path, "Example", "1.2.3", null);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Update)) {
            using var output = zip.CreateEntry("content/large.bin", CompressionLevel.NoCompression).Open();
            var chunk = new byte[65536];
            for (var index = 0; index < 320; index++) { output.Write(chunk); }
        }
        var accepted = await new ReleaseValidationService().RunAsync(spec);
        Assert.True(accepted.Success, string.Join("\n", accepted.Errors));
        Assert.Single(accepted.Checks);
    }
    private sealed class Runner : IProcessRunner
    {
        internal int Calls { get; private set; }
        public Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new ProcessRunResult(0, "ok", "", request.FileName, TimeSpan.Zero, false));
        }
    }
    [Fact]
    public void Cancellation_during_framework_archive_reads_stops_inventory_inspection()
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true)) {
            for (var index = 0; index < 100; index++) { zip.CreateEntry("content/" + index); }
        }
        using var cancellation = new CancellationTokenSource();
        using var source = new CancelOnReadStream(bytes.ToArray(), cancellation);
        using var bounded = new ArchiveMetadataReadStream(source, cancellation.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => {
            using var zip = new ZipArchive(bounded, ZipArchiveMode.Read);
            _ = zip.Entries.Count;
        });
        Assert.True(source.BytesRead > 0);
    }
    private sealed class CancelOnReadStream(byte[] bytes, CancellationTokenSource cancellation) : MemoryStream(bytes)
    {
        internal int BytesRead { get; private set; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = base.Read(buffer, offset, count);
            BytesRead += read;
            cancellation.Cancel();
            return read;
        }
        public override int Read(Span<byte> buffer)
        {
            var read = base.Read(buffer);
            BytesRead += read;
            cancellation.Cancel();
            return read;
        }
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
