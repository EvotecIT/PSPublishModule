using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private const string ModuleInstallUsage =
        "Usage: powerforge module install <name> --destination <directory> [--version <exact-version>] [--source <feed-url-or-folder>] [--prerelease] [--plan] [--accept-license] [--allow-clobber] [--output json]";

    private static int InstallModuleFromFeed(string[] args, CliOptions cli, ILogger logger, BufferedLogger? logBuffer,
        bool outputJson, CancellationToken cancellationToken)
    {
        var (options, moduleName) = ParseStandaloneCommandOptions(args, "install");
        if (moduleName is null)
            throw new ArgumentException(ModuleInstallUsage);
        var name = ManagedModuleStandaloneInput.ValidateName(moduleName);
        var request = new ManagedModuleInstallRequest
        {
            Name = name,
            Version = options.TryGetValue("--version", out var version)
                ? ManagedModuleStandaloneInput.ValidateVersion(name, version) : null,
            Repository = ManagedModuleStandaloneInput.ResolveRepository(
                options.GetValueOrDefault("--source") ?? ManagedModuleRestoreService.DefaultSource, "Module install"),
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = RequireModuleOption(options, "--destination", ModuleInstallUsage),
            IncludePrerelease = options.ContainsKey("--prerelease"),
            AcceptLicense = options.ContainsKey("--accept-license"),
            AllowClobber = options.ContainsKey("--allow-clobber"),
            RepairInstalledManifestDependencies = true
        };

        var installer = new ManagedModuleInstallService(logger);
        if (options.ContainsKey("--plan"))
        {
            var plan = RunWithStatus(outputJson, cli, $"Planning install of {name}",
                () => installer.PlanInstallAsync(request, cancellationToken).GetAwaiter().GetResult());
            if (outputJson)
                WriteModuleCommandResult("module.install", plan, ModuleCliJson.Default.ManagedModuleInstallPlan, logBuffer);
            else
                logger.Info($"{plan.Action}: {plan.Name} {plan.Version} at {plan.ModulePath}");
            return 0;
        }

        var result = RunWithStatus(outputJson, cli, $"Installing {name}",
            () => installer.InstallAsync(request, cancellationToken).GetAwaiter().GetResult());
        if (outputJson)
            WriteModuleCommandResult("module.install", result, ModuleCliJson.Default.ManagedModuleInstallResult, logBuffer);
        else
            logger.Success($"{result.Status}: {result.Name} {result.Version} at {result.ModulePath}");
        return 0;
    }
}
