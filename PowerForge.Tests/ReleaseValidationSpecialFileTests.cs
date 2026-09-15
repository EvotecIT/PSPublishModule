using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class ReleaseValidationSpecialFileTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.SpecialFile.Tests", Guid.NewGuid().ToString("N"));
    public ReleaseValidationSpecialFileTests() => Directory.CreateDirectory(_root);

    [ReleaseValidationLinuxTheory]
    [InlineData("module-directory")]
    [InlineData("module-archive")]
    [InlineData("consumer")]
    [InlineData("package")]
    [InlineData("configuration")]
    [InlineData("manifest")]
    [InlineData("linked-configuration")]
    public async Task Cli_rejects_unopened_fifo_inputs_without_waiting_for_a_writer(string scenario)
    {
        var input = Directory.CreateDirectory(Path.Combine(_root, "input")).FullName;
        var fifo = Path.Combine(input, scenario == "package" ? "Fixture.1.2.3.nupkg" : "blocked-input");
        using (var make = Process.Start(new ProcessStartInfo("mkfifo") {
            UseShellExecute = false, ArgumentList = { fifo }
        })!) {
            await make.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, make.ExitCode);
        }
        var spec = new ReleaseValidationSpec();
        if (scenario == "module-directory" || scenario == "module-archive") {
            spec.Modules = [new() { Path = scenario == "module-directory" ? input : fifo, Manifest = "Fixture.psd1" }];
        } else if (scenario == "package" || scenario == "consumer") {
            var feed = scenario == "package" ? input : Directory.CreateDirectory(Path.Combine(_root, "feed")).FullName;
            if (scenario == "consumer") {
                using var archive = ZipFile.Open(Path.Combine(feed, "Fixture.1.2.3.nupkg"), ZipArchiveMode.Create);
                using var writer = new StreamWriter(archive.CreateEntry("Fixture.nuspec").Open());
                writer.Write("<package><metadata><id>Fixture</id><version>1.2.3</version><authors>Fixture</authors><description>Fixture</description></metadata></package>");
                spec.Consumers = [new() { SourceDirectory = input, ProjectFile = "Fixture.csproj", Frameworks = ["net10.0"] }];
            }
            spec.Packages = new() { Path = feed, Items = [new() { Id = "Fixture" }] };
        } else if (scenario == "manifest") {
            spec.CliArtifacts = new() { ManifestPath = fifo, Target = "Fixture", Runtimes = ["linux-x64"], Styles = ["Portable"] };
        }
        var config = Path.Combine(_root, "validation.json");
        if (scenario == "configuration") { config = fifo; }
        else if (scenario == "linked-configuration") { File.CreateSymbolicLink(config, fifo); }
        else { File.WriteAllText(config, ReleaseValidationService.Serialize(spec)); }

        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var cli = Path.Combine(FindRepository(), "PowerForge.Cli", "bin", configuration, "net10.0", "PowerForge.Cli.dll");
        Assert.True(File.Exists(cli), "Build the matching CLI configuration before running this test.");
        var start = new ProcessStartInfo("dotnet") {
            WorkingDirectory = _root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            ArgumentList = { cli, "validate-release", "--config", config, "--output", "json" }
        };
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try {
            // A separate process makes the regression safe: synchronous FIFO open cannot hang the test host.
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var output = await stdout;
            var errors = await stderr;
            Assert.True(process.ExitCode == 1, $"Exit {process.ExitCode}: {output}\n{errors}");
            using var report = JsonDocument.Parse(output);
            Assert.Contains("regular file", output, StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(fifo));
        } finally {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
    }

    [ArtifactBoundaryUnixFact]
    public async Task Bounded_metadata_reads_preserve_links_to_regular_files()
    {
        var file = Path.Combine(_root, "metadata.json");
        var link = Path.Combine(_root, "linked.json");
        File.WriteAllText(file, "{\"fixture\":true}");
        File.CreateSymbolicLink(link, file);
        Assert.Equal("{\"fixture\":true}", await DotNetPublishReleaseArtifactVerifier.ReadBoundedTextAsync(link, "Fixture", 1024));
    }

    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "PSPublishModule.sln"))) return directory.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
