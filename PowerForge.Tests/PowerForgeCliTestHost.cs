namespace PowerForge.Tests;

/// <summary>Selects the exact prebuilt CLI for process tests without changing product discovery.</summary>
internal static class PowerForgeCliTestHost
{
    internal static string FindRepositoryRoot([System.Runtime.CompilerServices.CallerFilePath] string sourceFile = "")
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Path.GetDirectoryName(sourceFile)! })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "PowerForge.Cli", "PowerForge.Cli.csproj")))
                    return directory.FullName;
        throw new DirectoryNotFoundException("Unable to locate the PowerForge CLI repository fixture.");
    }

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
