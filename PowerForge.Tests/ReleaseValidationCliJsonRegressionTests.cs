using System.Diagnostics;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class ReleaseValidationCliJsonRegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PowerForge.CliJsonRegression", Guid.NewGuid().ToString("N"));
    public ReleaseValidationCliJsonRegressionTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("--help", "--output", true)]
    [InlineData("--help", "--json", true)]
    [InlineData("-h", "--output-json", true)]
    [InlineData("--HELP", "--json", true)]
    [InlineData("--help", "", false)]
    [InlineData("-h", "--output", false)]
    public async Task Help_honors_the_requested_output_format_without_loading_configuration(string help, string format, bool json)
    {
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var cli = Path.Combine(FindRepository(), "PowerForge.Cli", "bin", configuration, "net10.0", "PowerForge.Cli.dll");
        var arguments = new List<string> { cli, "validate-release", help, "--config", Path.Combine(_root, "not-created.json") };
        if (format.Length > 0) arguments.Add(format);
        if (format == "--output") arguments.Add(json ? "json" : "text");
        var result = await new ProcessRunner(ownProcessTree: true).RunAsync(
            new("dotnet", _root, arguments, TimeSpan.FromSeconds(30)));
        Assert.True(result.Succeeded, result.StdErr);
        Assert.True(string.IsNullOrWhiteSpace(result.StdErr), result.StdErr);
        if (json) {
            using var document = JsonDocument.Parse(result.StdOut);
            Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
            Assert.Equal("validate-release", document.RootElement.GetProperty("command").GetString());
            Assert.True(document.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(0, document.RootElement.GetProperty("exitCode").GetInt32());
            Assert.StartsWith("Usage: powerforge validate-release", document.RootElement.GetProperty("result").GetProperty("usage").GetString());
        } else {
            Assert.StartsWith("Usage: powerforge validate-release", result.StdOut);
        }
    }

    [ReleaseValidationLinuxFact]
    public async Task Json_output_reports_caller_cancellation_with_exit_130()
    {
        var ready = Path.Combine(_root, "ready");
        var config = Path.Combine(_root, "cancel.json");
        File.WriteAllText(config, ReleaseValidationService.Serialize(new() { Commands = [new() {
            FileName = "/bin/sh", Arguments = ["-c", "touch \"$READY\"; sleep 30"], Environment = new() { ["READY"] = ready }, TimeoutSeconds = 40
        }] }));
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var cli = Path.Combine(FindRepository(), "PowerForge.Cli", "bin", configuration, "net10.0", "PowerForge.Cli.dll");
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = _root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { cli, "validate-release", "--config", config, "--output", "json" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        try
        {
            using var readyTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!File.Exists(ready) && !process.HasExited) await Task.Delay(50, readyTimeout.Token);
            Assert.True(File.Exists(ready), "CLI did not enter its cancellable command.");
            var signalStart = new ProcessStartInfo("/bin/kill") { UseShellExecute = false };
            signalStart.ArgumentList.Add("-INT");
            signalStart.ArgumentList.Add(process.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            using var signal = Process.Start(signalStart)!;
            await signal.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, signal.ExitCode);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));

            var output = await stdout;
            var errors = await stderr;
            Assert.True(process.ExitCode == 130, $"Exit {process.ExitCode}: {output}\n{errors}");
            using var report = JsonDocument.Parse(output);
            Assert.False(report.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal(130, report.RootElement.GetProperty("exitCode").GetInt32());
            Assert.False(string.IsNullOrWhiteSpace(report.RootElement.GetProperty("error").GetString()));
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
        }
    }

    [Theory]
    [InlineData("missing-option", 2)]
    [InlineData("missing-file", 1)]
    [InlineData("malformed-json", 1)]
    [InlineData("bad-variable", 1)]
    [InlineData("duplicate-variable", 1)]
    [InlineData("invalid-root", 1)]
    [InlineData("valid", 0)]
    public async Task Json_output_remains_parseable_for_setup_failures(string scenario, int expectedExitCode)
    {
        var config = Path.Combine(_root, "validation.json");
        File.WriteAllText(config, scenario == "malformed-json" ? "{ broken" : ReleaseValidationService.Serialize(new() {
            Commands = [new() { FileName = "dotnet", Arguments = ["--version"] }]
        }));
        var arguments = new List<string> { "validate-release", "--output", "json" };
        if (scenario != "missing-option") arguments.AddRange(["--config", scenario == "missing-file" ? Path.Combine(_root, "absent.json") : config]);
        if (scenario == "bad-variable") arguments.AddRange(["--variable", "broken"]);
        if (scenario == "duplicate-variable") arguments.AddRange(["--variable", "Name=one", "--variable", "Name=two"]);
        if (scenario == "invalid-root") arguments.AddRange(["--project-root", Path.Combine(_root, "absent-directory")]);
        var repository = FindRepository();
        var configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent!.Name;
        var cli = Path.Combine(repository, "PowerForge.Cli", "bin", configuration, "net10.0", "PowerForge.Cli.dll");
        Assert.True(File.Exists(cli), "Build the matching PowerForge.Cli configuration before running this test.");
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = _root, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(cli);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
        var output = await stdout;
        var errors = await stderr;

        Assert.True(process.ExitCode == expectedExitCode, $"Exit {process.ExitCode}: {output}\n{errors}");
        using var report = JsonDocument.Parse(output);
        var isSetupError = scenario != "valid" && scenario != "invalid-root";
        Assert.Equal(expectedExitCode == 0, report.RootElement.GetProperty(isSetupError ? "success" : "Success").GetBoolean());
        if (expectedExitCode == 0) Assert.NotEmpty(report.RootElement.GetProperty("Checks").EnumerateArray());
        else if (scenario == "invalid-root") Assert.NotEmpty(report.RootElement.GetProperty("Errors").EnumerateArray());
        else {
            Assert.Equal(expectedExitCode, report.RootElement.GetProperty("exitCode").GetInt32());
            Assert.Equal("validate-release", report.RootElement.GetProperty("command").GetString());
            Assert.False(string.IsNullOrWhiteSpace(report.RootElement.GetProperty("error").GetString()));
        }
    }

    private static string FindRepository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "PSPublishModule.sln"))) return directory.FullName;
        throw new InvalidOperationException("Repository root not found.");
    }
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
