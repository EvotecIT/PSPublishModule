using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private const string ModuleSaveUsage =
        "Usage: powerforge module save <name> --destination <directory> [--source <feed-or-folder> | --profile <name>] [--version <exact-version>] [--as-nupkg] [--force] [--plan] [--output json]";
    private const string ModuleUpdateUsage =
        "Usage: powerforge module update <name> --destination <module-root> [--source <feed-or-folder> | --profile <name>] [--version <exact-version>] [--prerelease] [--plan] [--output json]";
    private const string ModuleUninstallUsage =
        "Usage: powerforge module uninstall <name> --destination <module-root> --version <exact-version> [--plan] [--output json]";
    private const string ModuleListUsage =
        "Usage: powerforge module list --destination <module-root> [--name <name-or-pattern>] [--version <version-or-range>] [--output json]";
    private const string ModulePackUsage =
        "Usage: powerforge module pack --path <module-directory> --destination <directory> [--force] [--output json]";

    private static int UpdateInstalledModule(string[] args, ILogger logger, BufferedLogger? logBuffer, bool outputJson,
        CancellationToken cancellationToken)
    {
        var (options, moduleName) = ParseStandaloneCommandOptions(args, "update");
        if (moduleName is null)
            throw new ArgumentException(ModuleUpdateUsage);

        var name = ManagedModuleStandaloneInput.ValidateName(moduleName);
        var request = new ManagedModuleUpdateRequest
        {
            Name = name,
            Version = ReadExactModuleVersion(options, name),
            Repository = ResolveModuleRepository(options, "Module update"),
            ModuleRoot = RequireModuleOption(options, "--destination", ModuleUpdateUsage),
            Scope = ManagedModuleInstallScope.Custom,
            IncludePrerelease = options.ContainsKey("--prerelease"),
            Force = options.ContainsKey("--force"),
            AcceptLicense = options.ContainsKey("--accept-license"),
            AllowClobber = options.ContainsKey("--allow-clobber"),
            AuthenticodeCheck = options.ContainsKey("--authenticode-check"),
            TrustPolicy = ReadModuleTrustPolicy(options)
        };
        var service = new ManagedModuleUpdateService(logger);
        if (options.ContainsKey("--plan"))
        {
            var plan = service.PlanUpdateAsync(request, cancellationToken).GetAwaiter().GetResult();
            if (outputJson)
                WriteModuleCommandResult("module.update", plan, ModuleCliJson.Default.ManagedModuleUpdatePlan, logBuffer);
            else
                logger.Info($"{plan.Action}: {plan.Name} {plan.TargetVersion} at {plan.ModulePath}");
            return 0;
        }

        var result = service.UpdateAsync(request, cancellationToken).GetAwaiter().GetResult();
        if (outputJson)
            WriteModuleCommandResult("module.update", result, ModuleCliJson.Default.ManagedModuleUpdateResult, logBuffer);
        else
            logger.Info($"{result.Status}: {result.Name} {result.TargetVersion} at {result.ModulePath}");
        return 0;
    }

    private static int UninstallModule(string[] args, ILogger logger, BufferedLogger? logBuffer, bool outputJson)
    {
        var (options, moduleName) = ParseStandaloneCommandOptions(args, "uninstall");
        if (moduleName is null)
            throw new ArgumentException(ModuleUninstallUsage);

        var name = ManagedModuleStandaloneInput.ValidateName(moduleName);
        var version = ManagedModuleStandaloneInput.ValidateVersion(name,
            RequireModuleOption(options, "--version", ModuleUninstallUsage));
        var moduleRoot = RequireModuleOption(options, "--destination", ModuleUninstallUsage);
        var dependencyRoots = (Environment.GetEnvironmentVariable("PSModulePath") ?? string.Empty)
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(Path.IsPathFullyQualified)
            .ToArray();
        var service = new ManagedModuleUninstallService();
        var plan = service.PlanUninstall(new ManagedModuleUninstallRequest
        {
            Name = [name],
            Version = version,
            Prerelease = version.Contains('-'),
            Scope = ManagedModuleInstallScope.Custom,
            ModuleRoot = moduleRoot,
            DependencyModuleRoots = dependencyRoots
        });
        if (options.ContainsKey("--plan"))
        {
            if (outputJson)
                WriteModuleCommandResult("module.uninstall", plan, ModuleCliJson.Default.ManagedModuleUninstallPlan, logBuffer);
            else if (plan.Targets.Count == 0)
                logger.Info($"No installed match for {name} {version}.");
            else
                foreach (var target in plan.Targets)
                    logger.Info($"Remove {target.Name} {target.Version} at {target.ModulePath}");
            return 0;
        }

        var results = service.Uninstall(plan).ToArray();
        if (outputJson)
            WriteModuleCommandResult("module.uninstall", results, ModuleCliJson.Default.ManagedModuleUninstallResultArray, logBuffer);
        else
            foreach (var result in results)
                logger.Info($"{result.Status}: {result.Name} {result.Version} at {result.ModulePath}");
        return 0;
    }

    private static int ListInstalledModules(string[] args, BufferedLogger? logBuffer, bool outputJson)
    {
        var (options, _) = ParseStandaloneCommandOptions(args, "list");
        var modules = ManagedModuleInventory.List(RequireModuleOption(options, "--destination", ModuleListUsage),
            options.GetValueOrDefault("--name"), options.GetValueOrDefault("--version"));
        if (outputJson)
            WriteModuleCommandResult("module.list", modules, ModuleCliJson.Default.ManagedModuleInstalledInfoArray, logBuffer);
        else
            foreach (var item in modules)
                Console.WriteLine($"{item.Name}\t{item.Version}\t{item.Path}");
        return 0;
    }

    private static int PackModule(string[] args, ILogger logger, BufferedLogger? logBuffer, bool outputJson)
    {
        var (options, _) = ParseStandaloneCommandOptions(args, "pack");
        var result = new ManagedModulePackService().Pack(new ManagedModulePackRequest
        {
            ModulePath = RequireModuleOption(options, "--path", ModulePackUsage),
            OutputDirectory = RequireModuleOption(options, "--destination", ModulePackUsage),
            Force = options.ContainsKey("--force")
        });
        if (outputJson)
            WriteModuleCommandResult("module.pack", result, ModuleCliJson.Default.ManagedModulePackResult, logBuffer);
        else
            logger.Success($"Packed {result.Name} {result.Version} to {result.PackagePath}");
        return 0;
    }
}
