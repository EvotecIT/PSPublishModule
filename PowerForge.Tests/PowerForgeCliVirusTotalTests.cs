using System.Diagnostics;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class PowerForgeCliVirusTotalTests
{
    [Fact]
    public async Task Release_JsonOutput_NeverSerializesInlineVirusTotalApiKey()
    {
        const string apiKey = "virus-total-inline-secret-value";
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var scriptPath = Path.Combine(root, "Build-Module.ps1");
        var configPath = Path.Combine(root, "powerforge.release.json");
        File.WriteAllText(scriptPath, "# plan-only module build");
        File.WriteAllText(configPath, $$"""
            {
              "Module": {
                "RepositoryRoot": ".",
                "ScriptPath": "Build-Module.ps1"
              },
              "VirusTotal": {
                "Enabled": true,
                "ApiKey": "{{apiKey}}",
                "ArtifactKinds": [ "PowerShellModule" ]
              }
            }
            """);
        try
        {
            var repositoryRoot = FindRepositoryRoot();
            var cliPath = Path.Combine(repositoryRoot, "PowerForge.Cli", "bin", "Release", "net10.0", "PowerForge.Cli.dll");
            var result = await RunAsync(
                repositoryRoot,
                $"\"{cliPath}\" release --config \"{configPath}\" --plan --output json");

            Assert.Equal(0, result.ExitCode);
            Assert.DoesNotContain(apiKey, result.StdOut + result.StdErr, StringComparison.Ordinal);
            using var document = JsonDocument.Parse(result.StdOut);
            var virusTotal = document.RootElement
                .GetProperty("spec")
                .GetProperty("virusTotal");
            Assert.True(
                !virusTotal.TryGetProperty("apiKey", out var apiKeyProperty) ||
                apiKeyProperty.ValueKind == JsonValueKind.Null);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunAsync(string workingDirectory, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start PowerForge CLI.");
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, stdout, stderr);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "PSPublishModule.sln")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("Repository root was not found.");
    }
}
