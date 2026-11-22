using System.Text.Json.Serialization.Metadata;
using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private const string ModuleRestoreUsage =
        "Usage: powerforge module restore --requirements <modules.json> --lock-file <modules.lock.json> --destination <directory> [--source <feed-url-or-folder> | --profile <name>] [--write-lock] [--plan] [--accept-license] [--allow-clobber] [--output json]";

    private static bool IsModuleAction(string action) => action is "install" or "restore" or "manifest" or "find" or "save" or "update" or "uninstall" or "pack" or "list" or "repository";

    private static int CommandModule(string[] args, CliOptions cli, ILogger logger)
    {
        var action = args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;
        var command = IsModuleAction(action) ? "module." + action : "module";
        var usage = action switch
        {
            "install" => ModuleInstallUsage,
            "restore" => ModuleRestoreUsage,
            "manifest" => ModuleManifestUsage,
            "find" => ModuleFindUsage,
            "save" => ModuleSaveUsage,
            "update" => ModuleUpdateUsage,
            "uninstall" => ModuleUninstallUsage,
            "pack" => ModulePackUsage,
            "list" => ModuleListUsage,
            "repository" => ModuleRepositoryUsage,
            _ => string.Join(Environment.NewLine, ModuleInstallUsage, ModuleRestoreUsage, ModuleManifestUsage, ModuleFindUsage,
                ModuleSaveUsage, ModuleUpdateUsage, ModuleUninstallUsage, ModulePackUsage, ModuleListUsage, ModuleRepositoryUsage)
        };

        var outputJson = IsJsonOutput(args);
        var (commandLogger, logBuffer) = CreateCommandLogger(outputJson, cli, logger);
        try
        {
            if (args.Length == 1 || args.Skip(1).Any(static argument => argument is "--help" or "-h"))
            {
                if (outputJson)
                    WriteModuleCommandResult(command, usage, ModuleCliJson.Default.String, logBuffer);
                else
                    Console.WriteLine(usage);
                return 0;
            }

            if (!IsModuleAction(action))
                throw new ArgumentException(usage);

            var commandArgs = args.Skip(2).ToArray();
            if (action == "manifest")
                return InspectModuleManifest(commandArgs, outputJson, logBuffer);

            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancel = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };
            Console.CancelKeyPress += cancel;
            try
            {
                return action switch
                {
                    "install" or "save" => InstallModuleFromFeed(action, commandArgs, cli, commandLogger, logBuffer, outputJson, cancellation.Token),
                    "find" => FindModules(commandArgs, commandLogger, logBuffer, outputJson, cancellation.Token),
                    "update" => UpdateInstalledModule(commandArgs, commandLogger, logBuffer, outputJson, cancellation.Token),
                    "uninstall" => UninstallModule(commandArgs, commandLogger, logBuffer, outputJson),
                    "list" => ListInstalledModules(commandArgs, logBuffer, outputJson),
                    "pack" => PackModule(commandArgs, commandLogger, logBuffer, outputJson),
                    "repository" => ManageModuleRepository(commandArgs, commandLogger, logBuffer, outputJson),
                    _ => RestoreModules(commandArgs, cli, commandLogger, logBuffer, outputJson, cancellation.Token)
                };
            }
            finally
            {
                Console.CancelKeyPress -= cancel;
            }
        }
        catch (Exception exception)
        {
            var exitCode = exception is ArgumentException ? 2 : 1;
            var message = exception is OperationCanceledException ? $"Module {action} cancelled." : exception.Message;
            if (outputJson)
                WriteJson(new CliJsonEnvelope { SchemaVersion = OutputSchemaVersion, Command = command, Success = false, ExitCode = exitCode, Error = message, Logs = LogsToJsonElement(logBuffer) });
            else
                logger.Error(message);
            return exitCode;
        }
    }

    private static int RestoreModules(string[] args, CliOptions cli, ILogger logger, BufferedLogger? logBuffer,
        bool outputJson, CancellationToken cancellationToken)
    {
        var (options, _) = ParseStandaloneCommandOptions(args, "restore");
        var request = new ManagedModuleRestoreRequest(
            RequireModuleOption(options, "--requirements", ModuleRestoreUsage),
            RequireModuleOption(options, "--lock-file", ModuleRestoreUsage),
            RequireModuleOption(options, "--destination", ModuleRestoreUsage),
            ResolveModuleRepository(options, "Module restore").Source,
            options.ContainsKey("--write-lock"), options.ContainsKey("--plan"),
            options.ContainsKey("--accept-license"), options.ContainsKey("--allow-clobber"));

        var service = new ManagedModuleRestoreService(logger);
        var result = RunWithStatus(outputJson, cli,
            request.Plan ? "Planning module restore" : "Restoring modules",
            () => service.RestoreAsync(request, cancellationToken).GetAwaiter().GetResult());
        if (outputJson)
        {
            WriteModuleCommandResult("module.restore", result, ModuleCliJson.Default.ManagedModuleRestoreResult, logBuffer);
        }
        else
        {
            var resultLogger = result.Plan && logger is QuietLogger quietLogger ? quietLogger.Inner : logger;
            resultLogger.Success(result.Plan
                ? $"Planned {result.Lock.Packages.Length} module(s) for {result.Destination}"
                : $"Restored {result.Lock.Packages.Length} module(s) to {result.Destination}");
            if (result.Plan || !cli.Quiet)
                foreach (var package in result.Lock.Packages)
                    resultLogger.Info($"  {package.Name} {package.Version}");
            if (result.LockWritten)
                logger.Info($"Wrote {result.LockPath}");
        }

        return 0;
    }

    private static (Dictionary<string, string?> Options, string? ModuleName) ParseStandaloneCommandOptions(string[] args, string action)
    {
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        string? moduleName = null;
        for (var index = 0; index < args.Length; index++)
        {
            if ((action is "install" or "save" or "update" or "uninstall") && moduleName is null && !args[index].StartsWith("-", StringComparison.Ordinal))
            {
                moduleName = args[index];
                continue;
            }

            var option = args[index].ToLowerInvariant();
            if (options.ContainsKey(option))
                throw new ArgumentException($"Duplicate module {action} option '{option}'.");

            switch (option)
            {
                case "--write-lock" when action == "restore":
                case "--prerelease" when action is "install" or "find" or "save" or "update":
                case "--plan" when action is "install" or "restore" or "save" or "update" or "uninstall":
                case "--accept-license" when action is "install" or "restore" or "save" or "update":
                case "--allow-clobber" when action is "install" or "restore" or "save" or "update":
                case "--force" when action is "save" or "update" or "pack":
                case "--as-nupkg" when action == "save":
                case "--trusted" when action is "repository add" or "repository set":
                case "--untrusted" when action == "repository set":
                case "--require-trusted-repository" when action is "install" or "save" or "update":
                case "--authenticode-check" when action is "install" or "save" or "update":
                case "--output-json":
                case "--json":
                    options.Add(option, null);
                    break;
                case "--requirements" when action == "restore":
                case "--lock-file" when action == "restore":
                case "--version" when action is "install" or "find" or "save" or "update" or "uninstall" or "list":
                case "--destination" when action is "install" or "restore" or "save" or "update" or "uninstall" or "list" or "pack":
                case "--source" when action is "install" or "restore" or "find" or "save" or "update" or "repository add" or "repository set":
                case "--profile" when action is "install" or "restore" or "find" or "save" or "update":
                case "--scope" when action is "repository list" or "repository add" or "repository set" or "repository remove":
                case "--path" when action is "manifest" or "pack":
                case "--name" when action is "find" or "list" or "repository add" or "repository set" or "repository remove":
                case "--limit" when action == "find":
                case "--project-root" when action == "project versions":
                case "--module-name" when action == "project versions":
                case "--output":
                    if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal) ||
                        string.IsNullOrWhiteSpace(args[index + 1]))
                        throw new ArgumentException($"Missing value for '{option}'.");
                    options.Add(option, args[++index]);
                    break;
                default:
                    throw new ArgumentException($"Unknown module {action} option '{option}'.");
            }
        }

        if (options.TryGetValue("--output", out var output) && !string.Equals(output, "json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Module {action} supports --output json.");
        return (options, moduleName);
    }

    private static string? ReadExactModuleVersion(Dictionary<string, string?> options, string name)
        => options.TryGetValue("--version", out var version)
            ? ManagedModuleStandaloneInput.ValidateVersion(name, version)
            : null;

    private static ManagedModuleTrustPolicy? ReadModuleTrustPolicy(Dictionary<string, string?> options)
        => options.ContainsKey("--require-trusted-repository") ? new() { RequireTrustedRepository = true } : null;

    private static string RequireModuleOption(Dictionary<string, string?> options, string option, string usage)
        => options.TryGetValue(option, out var value) ? value! : throw new ArgumentException($"Missing required option '{option}'. {usage}");

    private static void WriteModuleCommandResult<T>(string command, T value, JsonTypeInfo<T> type, BufferedLogger? logBuffer)
        => WriteJson(new CliJsonEnvelope
        {
            SchemaVersion = OutputSchemaVersion,
            Command = command,
            Success = true,
            Result = System.Text.Json.JsonSerializer.SerializeToElement(value, type),
            Logs = LogsToJsonElement(logBuffer)
        });
}
