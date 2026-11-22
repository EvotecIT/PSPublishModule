using System.Globalization;
using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private const string ModuleFindUsage =
        "Usage: powerforge module find --name <id-or-pattern> [--source <feed-url-or-folder>] [--version <exact-version>] [--prerelease] [--limit <count>] [--output json]";

    private static int FindModules(string[] args, ILogger logger, BufferedLogger? logBuffer, bool outputJson,
        CancellationToken cancellationToken)
    {
        var (options, _) = ParseStandaloneCommandOptions(args, "find");
        var query = RequireModuleOption(options, "--name", ModuleFindUsage);
        var limit = 100;
        if (options.TryGetValue("--limit", out var requestedLimit) &&
            (!int.TryParse(requestedLimit, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > 100))
            throw new ArgumentException("Module find --limit must be between 1 and 100.");

        var source = options.GetValueOrDefault("--source") ?? ManagedModuleRestoreService.DefaultSource;
        var repository = ManagedModuleStandaloneInput.ResolveRepository(source, "Search");
        var client = new ManagedModuleRepositoryClient(logger);
        var includePrerelease = options.ContainsKey("--prerelease");
        ManagedModuleVersionInfo[] results;
        if (options.TryGetValue("--version", out var requestedVersion))
        {
            var name = ManagedModuleStandaloneInput.ValidateName(query);
            var version = ManagedModuleStandaloneInput.ValidateVersion(name, requestedVersion);
            var versions = client.GetVersionsAsync(repository, name,
                includePrerelease || version.Contains('-'), cancellationToken: cancellationToken).GetAwaiter().GetResult();
            results = versions.Where(item => ManagedModuleVersionComparer.Instance.Compare(item.Version, version) == 0).Take(1).ToArray();
        }
        else
        {
            results = client.SearchPackagesAsync(repository, query, includePrerelease, take: limit, cancellationToken: cancellationToken)
                .GetAwaiter().GetResult().ToArray();
        }
        if (outputJson)
            WriteModuleCommandResult("module.find", results, ModuleCliJson.Default.ManagedModuleVersionInfoArray, logBuffer);
        else
            foreach (var item in results)
                Console.WriteLine($"{item.Name}\t{item.Version}\t{item.RepositorySource}");
        return 0;
    }
}
