using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private const string ModuleInstallUsage =
        "Usage: powerforge module install <name> --destination <directory> [--version <exact-version>] [--source <feed-url-or-folder>] [--prerelease] [--plan] [--accept-license] [--allow-clobber] [--output json]";

    private static int InstallModuleFromFeed(string action, string[] args, CliOptions cli, ILogger logger, BufferedLogger? logBuffer,
        bool outputJson, CancellationToken cancellationToken)
    {
        var save = action == "save";
        var usage = save ? ModuleSaveUsage : ModuleInstallUsage;
        var (options, moduleName) = ParseStandaloneCommandOptions(args, action);
        if (moduleName is null)
            throw new ArgumentException(usage);
        var command = "module." + action;
        var name = ManagedModuleStandaloneInput.ValidateName(moduleName);
        var request = new ManagedModuleInstallRequest
        {
            Name = name,
            Version = ReadExactModuleVersion(options, name),
            Repository = ResolveModuleRepository(options, "Module " + action),
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = RequireModuleOption(options, "--destination", usage),
            IncludePrerelease = options.ContainsKey("--prerelease"),
            AcceptLicense = options.ContainsKey("--accept-license"),
            AllowClobber = options.ContainsKey("--allow-clobber"),
            RepairInstalledManifestDependencies = !save,
            SaveAsNupkg = options.ContainsKey("--as-nupkg"),
            Force = options.ContainsKey("--force"),
            AuthenticodeCheck = options.ContainsKey("--authenticode-check"),
            TrustPolicy = ReadModuleTrustPolicy(options)
        };

        var installer = new ManagedModuleInstallService(logger);
        if (options.ContainsKey("--plan"))
        {
            var plan = RunWithStatus(outputJson, cli, $"Planning module {action} for {name}",
                () => installer.PlanInstallAsync(request, cancellationToken).GetAwaiter().GetResult());
            if (outputJson)
                WriteModuleCommandResult(command, plan, ModuleCliJson.Default.ManagedModuleInstallPlan, logBuffer);
            else
                logger.Info($"{plan.Action}: {plan.Name} {plan.Version} at {plan.ModulePath}");
            return 0;
        }

        var result = RunWithStatus(outputJson, cli, $"Running module {action} for {name}",
            () => installer.InstallAsync(request, cancellationToken).GetAwaiter().GetResult());
        if (outputJson)
            WriteModuleCommandResult(command, result, ModuleCliJson.Default.ManagedModuleInstallResult, logBuffer);
        else
            logger.Success($"{result.Status}: {result.Name} {result.Version} at {result.ModulePath}");
        return 0;
    }
}
