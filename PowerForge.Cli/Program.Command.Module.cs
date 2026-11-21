using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private const string ModuleRestoreUsage =
        "Usage: powerforge module restore --requirements <modules.json> --lock-file <modules.lock.json> --destination <directory> [--source <feed-url-or-folder>] [--write-lock] [--plan] [--accept-license] [--allow-clobber] [--output json]";

    private static int CommandModule(string[] args, CliOptions cli, ILogger logger)
    {
        const string command = "module.restore";
        var outputJson = IsJsonOutput(args);
        var (commandLogger, logBuffer) = CreateCommandLogger(outputJson, cli, logger);
        try
        {
            if (args.Length == 1 || args.Skip(1).Any(static argument => argument is "--help" or "-h"))
            {
                if (outputJson)
                    WriteJson(new CliJsonEnvelope { SchemaVersion = OutputSchemaVersion, Command = command, Success = true, Result = CliJson.SerializeToElement(ModuleRestoreUsage, CliJson.Context.String) });
                else
                    Console.WriteLine(ModuleRestoreUsage);
                return 0;
            }

            if (!args[1].Equals("restore", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(ModuleRestoreUsage);
            var request = ParseModuleRestoreArgs(args.Skip(2).ToArray());
            var service = new ManagedModuleRestoreService(commandLogger);
            using var cancellation = new CancellationTokenSource();
            ConsoleCancelEventHandler cancel = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };
            Console.CancelKeyPress += cancel;
            ManagedModuleRestoreResult result;
            try
            {
                result = RunWithStatus(outputJson, cli,
                    request.Plan ? "Planning module restore" : "Restoring modules",
                    () => service.RestoreAsync(request, cancellation.Token).GetAwaiter().GetResult());
            }
            finally
            {
                Console.CancelKeyPress -= cancel;
            }

            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = command,
                    Success = true,
                    Result = CliJson.SerializeToElement(result, CliJson.Context.ManagedModuleRestoreResult),
                    Logs = LogsToJsonElement(logBuffer)
                });
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
        catch (Exception exception)
        {
            var exitCode = exception is ArgumentException ? 2 : 1;
            var message = exception is OperationCanceledException ? "Module restore cancelled." : exception.Message;
            if (outputJson)
                WriteJson(new CliJsonEnvelope { SchemaVersion = OutputSchemaVersion, Command = command, Success = false, ExitCode = exitCode, Error = message, Logs = LogsToJsonElement(logBuffer) });
            else
                logger.Error(message);
            return exitCode;
        }
    }

    private static ManagedModuleRestoreRequest ParseModuleRestoreArgs(string[] args)
    {
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            var option = args[index].ToLowerInvariant();
            if (options.ContainsKey(option))
                throw new ArgumentException($"Duplicate module restore option '{option}'.");

            switch (option)
            {
                case "--write-lock":
                case "--plan":
                case "--accept-license":
                case "--allow-clobber":
                case "--output-json":
                case "--json":
                    options.Add(option, null);
                    break;
                case "--requirements":
                case "--lock-file":
                case "--destination":
                case "--source":
                case "--output":
                    if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal) ||
                        string.IsNullOrWhiteSpace(args[index + 1]))
                        throw new ArgumentException($"Missing value for '{option}'.");
                    options.Add(option, args[++index]);
                    break;
                default:
                    throw new ArgumentException($"Unknown module restore option '{option}'.");
            }
        }

        string Require(string option) => options.TryGetValue(option, out var value)
            ? value! : throw new ArgumentException($"Missing required option '{option}'. {ModuleRestoreUsage}");

        if (options.TryGetValue("--output", out var output) && !string.Equals(output, "json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Module restore supports --output json.");

        return new ManagedModuleRestoreRequest(
            Require("--requirements"),
            Require("--lock-file"),
            Require("--destination"),
            options.TryGetValue("--source", out var source) ? source! : ManagedModuleRestoreService.DefaultSource,
            options.ContainsKey("--write-lock"), options.ContainsKey("--plan"), options.ContainsKey("--accept-license"), options.ContainsKey("--allow-clobber"));
    }
}
