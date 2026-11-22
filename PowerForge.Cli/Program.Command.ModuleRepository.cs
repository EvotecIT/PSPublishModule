using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private const string ModuleRepositoryUsage =
        "Usage: powerforge module repository list [--scope <all|user|machine>] [--output json]" + "\n" +
        "       powerforge module repository add --name <name> --source <feed-or-folder> [--scope <user|machine>] [--trusted] [--output json]" + "\n" +
        "       powerforge module repository set --name <name> [--source <feed-or-folder>] [--trusted | --untrusted] [--scope <user|machine>]" + "\n" +
        "       powerforge module repository remove --name <name> [--scope <user|machine>] [--output json]";

    private static int ManageModuleRepository(string[] args, ILogger logger, BufferedLogger? logBuffer, bool outputJson)
    {
        if (args.Length == 0)
        {
            if (outputJson)
                WriteModuleCommandResult("module.repository", ModuleRepositoryUsage, ModuleCliJson.Default.String, logBuffer);
            else
                Console.WriteLine(ModuleRepositoryUsage);
            return 0;
        }

        var action = args[0].ToLowerInvariant();
        if (action is not ("list" or "add" or "set" or "remove"))
            throw new ArgumentException(ModuleRepositoryUsage);
        var (options, _) = ParseStandaloneCommandOptions(args.Skip(1).ToArray(), "repository " + action);
        var scope = options.GetValueOrDefault("--scope")?.ToLowerInvariant() switch
        {
            "user" => ModuleRepositoryProfileScope.User,
            "machine" => ModuleRepositoryProfileScope.Machine,
            "all" when action == "list" => ModuleRepositoryProfileScope.All,
            null => action == "list" ? ModuleRepositoryProfileScope.All : ModuleRepositoryProfileScope.User,
            _ => throw new ArgumentException(ModuleRepositoryUsage)
        };

        ManagedModuleRepositoryProfileInfo[] results;
        if (action == "list")
        {
            results = ManagedModuleRepositoryProfiles.List(scope);
        }
        else
        {
            var name = RequireModuleOption(options, "--name", ModuleRepositoryUsage);
            if (options.ContainsKey("--trusted") && options.ContainsKey("--untrusted"))
                throw new ArgumentException("Specify either --trusted or --untrusted.");
            bool? trusted = options.ContainsKey("--trusted") ? true : options.ContainsKey("--untrusted") ? false : null;
            var source = options.GetValueOrDefault("--source");
            if (action == "set" && source is null && trusted is null)
                throw new ArgumentException("Specify --source, --trusted, or --untrusted to change a profile.");

            results = action switch
            {
                "add" => [ManagedModuleRepositoryProfiles.Add(name,
                    RequireModuleOption(options, "--source", ModuleRepositoryUsage), trusted == true, scope)],
                "set" => [ManagedModuleRepositoryProfiles.Set(name, source, trusted, scope)],
                _ => [ManagedModuleRepositoryProfiles.Remove(name, scope)]
            };
        }

        if (outputJson)
            WriteModuleCommandResult("module.repository." + action, results, ModuleCliJson.Default.ManagedModuleRepositoryProfileInfoArray, logBuffer);
        else
            foreach (var result in results)
                logger.Info($"{result.Name}\t{result.Source}\t{result.Scope}\tTrusted: {result.Trusted}");
        return 0;
    }

    private static ManagedModuleRepository ResolveModuleRepository(Dictionary<string, string?> options, string defaultName)
    {
        if (!options.TryGetValue("--profile", out var name))
            return ManagedModuleStandaloneInput.ResolveRepository(
                options.GetValueOrDefault("--source") ?? ManagedModuleRestoreService.DefaultSource, defaultName);
        if (options.ContainsKey("--source"))
            throw new ArgumentException("Specify either --profile or --source.");

        return ManagedModuleRepositoryProfiles.Resolve(name!);
    }
}
