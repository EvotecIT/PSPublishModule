using System.Diagnostics;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class ReleaseValidationCliOptionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.CliOptions.Tests", Guid.NewGuid().ToString("N"));
    public ReleaseValidationCliOptionTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("typo")]
    [InlineData("stray")]
    [InlineData("missing-version")]
    [InlineData("missing-root")]
    [InlineData("valid")]
    public async Task Cli_rejects_unrecognized_or_incomplete_options_before_executing_contracts(string variation)
    {
        var marker = Path.Combine(_root, "executed.txt");
        var script = Path.Combine(_root, "probe.ps1");
        File.WriteAllText(script, "Set-Content -LiteralPath $env:PROBE_MARKER -Value 'executed'");
        var config = Path.Combine(_root, "validation.json");
        File.WriteAllText(config, ReleaseValidationService.Serialize(new() { Commands = [new() {
            FileName = "pwsh", Arguments = ["-NoProfile", "-NonInteractive", "-File", script],
            Environment = new() { ["PROBE_MARKER"] = marker }, TimeoutSeconds = 15
        }] }));
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var cli = Path.Combine(FindRepository(), "PowerForge.Cli", "bin", configuration, "net10.0", "PowerForge.Cli.dll");
        Assert.True(File.Exists(cli), "Build the matching CLI before running this test.");
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = _root, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in new[] { cli, "validate-release", "--config", config, "--output", "json" }) start.ArgumentList.Add(argument);
        string[] extra = variation switch {
            "typo" => ["--versoin", "1.2.3"],
            "stray" => ["unexpected-value"],
            "missing-version" => ["--version"],
            "missing-root" => ["--project-root"],
            _ => ["--version", "1.2.3"]
        };
        foreach (var argument in extra) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try {
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            var output = await stdout;
            var errors = await stderr;
            using var document = JsonDocument.Parse(output);
            if (variation == "valid") {
                Assert.True(process.ExitCode == 0, $"Exit {process.ExitCode}: {output}\n{errors}");
                Assert.True(document.RootElement.GetProperty("Success").GetBoolean());
                Assert.Equal("1.2.3", document.RootElement.GetProperty("Version").GetString());
                Assert.True(File.Exists(marker));
            } else {
                Assert.NotEqual(0, process.ExitCode);
                Assert.False(document.RootElement.GetProperty("success").GetBoolean());
                Assert.Equal(process.ExitCode, document.RootElement.GetProperty("exitCode").GetInt32());
                Assert.False(string.IsNullOrWhiteSpace(document.RootElement.GetProperty("error").GetString()));
                Assert.False(File.Exists(marker), "Invalid CLI input must not execute a validation probe.");
            }
        }
        finally {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); }
        }
    }

    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "PSPublishModule.sln"))) return directory.FullName;
        throw new InvalidOperationException("Repository not found.");
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
