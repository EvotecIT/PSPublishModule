using System.Diagnostics;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class PowerForgeCliReleaseDispatchTests
{
    [Fact]
    public async Task Release_RejectsUnknownSubcommandBeforeLoadingConfiguration()
    {
        var repositoryRoot = PowerForgeCliTestHost.FindRepositoryRoot();
        var cliPath = PowerForgeCliTestHost.ResolveAssemblyPath(repositoryRoot,
            Environment.GetEnvironmentVariable(PowerForgeCliTestHost.AssemblyPathEnvironmentVariable));
        var missingConfig = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "release.json");

        var unknown = await RunRelease("prepare-typo");
        Assert.Equal(2, unknown.ExitCode);
        using (var json = JsonDocument.Parse(unknown.Output))
        {
            Assert.False(json.RootElement.GetProperty("success").GetBoolean());
            Assert.Equal("Unknown release subcommand 'prepare-typo'.", json.RootElement.GetProperty("error").GetString());
        }

        var ordinary = await RunRelease();
        Assert.Equal(1, ordinary.ExitCode);
        using var ordinaryJson = JsonDocument.Parse(ordinary.Output);
        Assert.Equal($"Config file not found: {missingConfig}", ordinaryJson.RootElement.GetProperty("error").GetString());

        async Task<(int ExitCode, string Output)> RunRelease(params string[] subcommand)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo("dotnet")
                {
                    WorkingDirectory = repositoryRoot,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            foreach (var argument in new[] { cliPath, "release" }.Concat(subcommand)
                .Concat(new[] { "--config", missingConfig, "--output", "json" }))
                process.StartInfo.ArgumentList.Add(argument);
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
                throw new TimeoutException("PowerForge CLI release dispatch test timed out.");
            }
            Assert.True(string.IsNullOrWhiteSpace(await error), await error);
            return (process.ExitCode, await output);
        }
    }
}
