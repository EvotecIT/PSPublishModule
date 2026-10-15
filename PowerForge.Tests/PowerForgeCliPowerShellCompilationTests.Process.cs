namespace PowerForge.Tests;

public sealed partial class PowerForgeCliPowerShellCompilationTests
{
    private static async Task<(int ExitCode, string StdOut, string StdErr)> RunCliAsync(
        string repositoryRoot, IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment = null, TimeSpan? timeout = null)
    {
        var cli = Path.Combine(repositoryRoot, "PowerForge.Cli", "bin", "Release", "net10.0", "PowerForge.Cli.dll");
        var variables = environment?.ToDictionary(pair => pair.Key, pair => (string?)pair.Value);
        // Artifact builds own a five-minute child budget; include their output drainage
        // in the outer deadline without keeping build servers or pipe holders alive.
        var deadline = timeout ?? TimeSpan.FromMinutes(6);
        var result = await new ProcessRunner(ownProcessTree: true).RunAsync(new ProcessRunRequest(
            "dotnet", repositoryRoot, new[] { cli }.Concat(arguments).ToArray(), deadline, variables));
        if (result.TimedOut)
            throw new TimeoutException($"PowerShell compilation CLI test timed out after {deadline}: {string.Join(" ", arguments)}{Environment.NewLine}STDOUT:{Environment.NewLine}{result.StdOut}{Environment.NewLine}STDERR:{Environment.NewLine}{result.StdErr}");
        Assert.False(result.StartFailed, result.StdErr);
        return (result.ExitCode, result.StdOut, result.StdErr);
    }
}
