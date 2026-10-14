namespace PowerForge.Tests;

/// <summary>Selects the exact prebuilt CLI for compiler process tests without changing product discovery.</summary>
internal static class PowerForgeCliTestHost
{
    internal const string AssemblyPathEnvironmentVariable = "POWERFORGE_TEST_CLI_ASSEMBLY_PATH";

    internal static string ResolveAssemblyPath(string repositoryRoot, string? configuredPath)
    {
        if (configuredPath is null)
            return Path.Combine(repositoryRoot, "PowerForge.Cli", "bin", "Release", "net10.0", "PowerForge.Cli.dll");

        if (string.IsNullOrWhiteSpace(configuredPath) || !Path.IsPathFullyQualified(configuredPath) ||
            !string.Equals(Path.GetFileName(configuredPath), "PowerForge.Cli.dll", StringComparison.Ordinal))
            throw new InvalidOperationException($"{AssemblyPathEnvironmentVariable} must identify an absolute PowerForge.Cli.dll path.");

        var path = Path.GetFullPath(configuredPath);
        if (!File.Exists(path))
            throw new FileNotFoundException($"The configured test CLI does not exist; refusing to use a different build: {path}", path);

        return path;
    }
}
