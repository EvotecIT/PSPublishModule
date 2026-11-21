using System.Text.Json.Serialization.Metadata;
using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private const string ModuleRestoreUsage =
        "Usage: powerforge module restore --requirements <modules.json> --lock-file <modules.lock.json> --destination <directory> [--source <feed-url-or-folder>] [--write-lock] [--plan] [--accept-license] [--allow-clobber] [--output json]";

    private static bool IsModuleAction(string action) => action is "install" or "restore";

    private static int CommandModule(string[] args, CliOptions cli, ILogger logger)
    {
        var action = args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;
        var command = IsModuleAction(action) ? "module." + action : "module";
        var usage = action switch
        {
            "install" => ModuleInstallUsage,
            "restore" => ModuleRestoreUsage,
            _ => ModuleInstallUsage + Environment.NewLine + ModuleRestoreUsage
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

            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancel = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };
            Console.CancelKeyPress += cancel;
            try
            {
                var commandArgs = args.Skip(2).ToArray();
                if (action == "install")
                    return InstallModuleFromFeed(commandArgs, cli, commandLogger, logBuffer, outputJson, cancellation.Token);
                return RestoreModules(commandArgs, cli, commandLogger, logBuffer, outputJson, cancellation.Token);
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
        var (options, _) = ParseModuleCommandOptions(args, "restore");
        var request = new ManagedModuleRestoreRequest(
            RequireModuleOption(options, "--requirements", ModuleRestoreUsage),
            RequireModuleOption(options, "--lock-file", ModuleRestoreUsage),
            RequireModuleOption(options, "--destination", ModuleRestoreUsage),
            options.GetValueOrDefault("--source") ?? ManagedModuleRestoreService.DefaultSource,
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
            logger.Success(result.Plan
                ? $"Planned {result.Lock.Packages.Length} module(s) for {result.Destination}"
                : $"Restored {result.Lock.Packages.Length} module(s) to {result.Destination}");
            if (result.Plan || !cli.Quiet)
                foreach (var package in result.Lock.Packages)
                    logger.Info($"  {package.Name} {package.Version}");
            if (result.LockWritten)
                logger.Info($"Wrote {result.LockPath}");
        }

        return 0;
    }

    private static (Dictionary<string, string?> Options, string? ModuleName) ParseModuleCommandOptions(string[] args, string action)
    {
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        string? moduleName = null;
        for (var index = 0; index < args.Length; index++)
        {
            if (action == "install" && moduleName is null && !args[index].StartsWith("-", StringComparison.Ordinal))
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
                case "--prerelease" when action == "install":
                case "--plan":
                case "--accept-license":
                case "--allow-clobber":
                case "--output-json":
                case "--json":
                    options.Add(option, null);
                    break;
                case "--requirements" when action == "restore":
                case "--lock-file" when action == "restore":
                case "--version" when action == "install":
                case "--destination":
                case "--source":
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
