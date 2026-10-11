using System.Reflection;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class PowerForgeCliAppleSimulatorTests
{
    [Fact]
    public async Task Simulator_json_identifies_the_cli_and_engine_builds_without_a_device()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PSPublishModule.sln")))
            directory = directory.Parent;
        var repository = directory?.FullName ?? throw new DirectoryNotFoundException("Repository root was not found.");
        var configuration = typeof(PowerForgeCliAppleSimulatorTests).Assembly
            .GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
        var cli = Path.Combine(repository, "PowerForge.Cli", "bin", configuration, "net10.0", "PowerForge.Cli.dll");
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", "SimulatorCli-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var result = await new ProcessRunner().RunAsync(new ProcessRunRequest(
                Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet", repository,
                new[] { cli, "apple-simulator", "status", "--state-root", root, "--output", "json" }, TimeSpan.FromSeconds(30)));

            Assert.Equal(0, result.ExitCode);
            using var json = JsonDocument.Parse(result.StdOut);
            Assert.Equal("apple-simulator.status", json.RootElement.GetProperty("command").GetString());
            var tool = json.RootElement.GetProperty("tool");
            Assert.Equal(Assembly.LoadFrom(cli).GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
                tool.GetProperty("cliVersion").GetString());
            Assert.Equal(typeof(AppleSimulatorSessionService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion,
                tool.GetProperty("engineVersion").GetString());
            Assert.Equal(Assembly.LoadFrom(cli).ManifestModule.ModuleVersionId.ToString("D"), tool.GetProperty("cliModuleId").GetString());
            Assert.Equal(typeof(AppleSimulatorSessionService).Assembly.ManifestModule.ModuleVersionId.ToString("D"), tool.GetProperty("engineModuleId").GetString());
            Assert.Empty(Directory.EnumerateFiles(root));
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
