using PowerForge;
using PowerForge.Cli;
using System.IO;
using System.Linq;

internal static partial class Program
{
    private const string StoreUsage =
        "Usage: powerforge store submit [--config <powerforge.store.submit.json>] [--list] [--list-assets] [--target <Name>] [--submission-id <id>] [--plan] [--validate] [--no-commit] [--no-wait] [--output json]";

    private static int CommandStore(string[] filteredArgs, CliOptions cli, ILogger logger)
    {
        var argv = filteredArgs.Skip(1).ToArray();
        var outputJson = IsJsonOutput(argv);

        if (argv.Length == 0)
        {
            if (outputJson)
            {
                WriteStoreUsageEnvelope("store", success: false, exitCode: 2);
                return 2;
            }

            Console.WriteLine(StoreUsage);
            return 2;
        }

        if (argv[0].Equals("-h", StringComparison.OrdinalIgnoreCase) || argv[0].Equals("--help", StringComparison.OrdinalIgnoreCase))
        {
            if (outputJson)
            {
                WriteStoreUsageEnvelope("store", success: true, exitCode: 0);
                return 0;
            }

            Console.WriteLine(StoreUsage);
            return 0;
        }

        var sub = argv[0].ToLowerInvariant();
        if (!string.Equals(sub, "submit", StringComparison.OrdinalIgnoreCase))
        {
            if (outputJson)
            {
                WriteStoreUsageEnvelope("store", success: false, exitCode: 2);
                return 2;
            }

            Console.WriteLine(StoreUsage);
            return 2;
        }

        var subArgs = argv.Skip(1).ToArray();
        if (subArgs.Any(arg => arg.Equals("-h", StringComparison.OrdinalIgnoreCase) || arg.Equals("--help", StringComparison.OrdinalIgnoreCase)))
        {
            if (IsJsonOutput(subArgs))
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "store.submit",
                    Success = true,
                    ExitCode = 0,
                    Result = System.Text.Json.JsonSerializer.SerializeToElement(new { usage = StoreUsage })
                });
            }
            else
            {
                Console.WriteLine(StoreUsage);
            }

            return 0;
        }

        outputJson = IsJsonOutput(subArgs);
        var listOnly = subArgs.Any(arg => arg.Equals("--list", StringComparison.OrdinalIgnoreCase) || arg.Equals("--ls", StringComparison.OrdinalIgnoreCase));
        var listAssets = subArgs.Any(arg => arg.Equals("--list-assets", StringComparison.OrdinalIgnoreCase));
        var planOnly = subArgs.Any(arg => arg.Equals("--plan", StringComparison.OrdinalIgnoreCase) || arg.Equals("--dry-run", StringComparison.OrdinalIgnoreCase));
        var validateOnly = subArgs.Any(arg => arg.Equals("--validate", StringComparison.OrdinalIgnoreCase));
        var targetName = TryGetOptionValue(subArgs, "--target") ?? TryGetOptionValue(subArgs, "--name");
        var submissionId = TryGetOptionValue(subArgs, "--submission-id");
        var noCommit = subArgs.Any(arg => arg.Equals("--no-commit", StringComparison.OrdinalIgnoreCase));
        var noWait = subArgs.Any(arg => arg.Equals("--no-wait", StringComparison.OrdinalIgnoreCase));

        var configPath = TryGetOptionValue(subArgs, "--config");
        if (string.IsNullOrWhiteSpace(configPath))
        {
            var baseDir = TryGetProjectRoot(subArgs);
            if (!string.IsNullOrWhiteSpace(baseDir))
                baseDir = Path.GetFullPath(baseDir.Trim().Trim('"'));
            else
                baseDir = Directory.GetCurrentDirectory();

            configPath = FindDefaultStoreSubmissionConfig(baseDir);
        }

        if (string.IsNullOrWhiteSpace(configPath))
        {
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "store.submit",
                    Success = false,
                    ExitCode = 2,
                    Error = "Missing --config and no default store submission config found."
                });
                return 2;
            }

            Console.WriteLine(StoreUsage);
            return 2;
        }

        try
        {
            var (cmdLogger, logBuffer) = CreateCommandLogger(outputJson, cli, logger);
            var loaded = LoadStoreSubmissionSpecWithPath(configPath);
            var safeSpec = StoreSubmissionSpecSanitizer.RedactSecrets(loaded.Value);
            var service = new StoreSubmissionService(cmdLogger);
            var targetList = service.ListTargets(loaded.Value);

            if (listOnly || string.IsNullOrWhiteSpace(targetName) && !planOnly && !validateOnly)
            {
                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "store.submit.list",
                        Success = true,
                        ExitCode = 0,
                        Config = "storesubmit",
                        ConfigPath = loaded.FullPath,
                        Spec = CliJson.SerializeToElement(safeSpec, CliJson.Context.StoreSubmissionSpec),
                        Results = CliJson.SerializeToElement(targetList, CliJson.Context.StoreSubmissionTargetSummaryArray),
                        Logs = LogsToJsonElement(logBuffer)
                    });
                    return 0;
                }

                cmdLogger.Info($"Store submission config: {loaded.FullPath}");
                foreach (var target in targetList)
                {
                    cmdLogger.Success(target.Name);
                    if (!string.IsNullOrWhiteSpace(target.Description))
                        cmdLogger.Info($"  {target.Description}");
                    cmdLogger.Info($"  Provider: {target.Provider}");
                    cmdLogger.Info($"  ApplicationId: {target.ApplicationId}");
                }
                return 0;
            }

            var request = new StoreSubmissionRequest
            {
                TargetName = targetName,
                SubmissionId = submissionId,
                Commit = noCommit ? false : null,
                WaitForCommit = noWait ? false : null
            };

            if (listAssets)
            {
                var plan = service.Plan(loaded.Value, loaded.FullPath, request);
                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "store.submit.assets",
                        Success = true,
                        ExitCode = 0,
                        Config = "storesubmit",
                        ConfigPath = loaded.FullPath,
                        Spec = CliJson.SerializeToElement(safeSpec, CliJson.Context.StoreSubmissionSpec),
                        Plan = CliJson.SerializeToElement(plan, CliJson.Context.StoreSubmissionPlan),
                        Logs = LogsToJsonElement(logBuffer)
                    });
                    return 0;
                }

                cmdLogger.Success($"Store submission assets for '{plan.TargetName}'.");
                cmdLogger.Info($"Provider: {plan.Provider}");
                if (plan.Provider == StoreSubmissionProviderKind.PackagedApp)
                {
                    foreach (var file in plan.PackageFiles)
                        cmdLogger.Info($" -> {file}");
                    cmdLogger.Info($"ZIP: {plan.ZipPath}");
                }
                else
                {
                    foreach (var pkg in plan.DesktopPackages)
                        cmdLogger.Info($" -> {pkg.PackageType} {pkg.PackageUrl} [{string.Join(", ", pkg.Architectures)}]");
                }
                return 0;
            }

            if (validateOnly)
            {
                var errors = service.Validate(loaded.Value, loaded.FullPath, request);
                var plan = errors.Length == 0 ? service.Plan(loaded.Value, loaded.FullPath, request) : null;
                var validateExitCode = errors.Length == 0 ? 0 : 2;

                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "store.submit.validate",
                        Success = validateExitCode == 0,
                        ExitCode = validateExitCode,
                        Error = validateExitCode == 0 ? null : string.Join("\n", errors),
                        Config = "storesubmit",
                        ConfigPath = loaded.FullPath,
                        Spec = CliJson.SerializeToElement(safeSpec, CliJson.Context.StoreSubmissionSpec),
                        Plan = plan is null ? null : CliJson.SerializeToElement(plan, CliJson.Context.StoreSubmissionPlan),
                        Logs = LogsToJsonElement(logBuffer)
                    });
                    return validateExitCode;
                }

                if (validateExitCode == 0)
                {
                    cmdLogger.Success($"Store submission config is valid for target '{plan!.TargetName}'.");
                    return 0;
                }

                cmdLogger.Error("Store submission config failed validation.");
                foreach (var error in errors)
                    cmdLogger.Error(error);
                return validateExitCode;
            }

            var prepared = service.Plan(loaded.Value, loaded.FullPath, request);
            if (planOnly)
            {
                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "store.submit.plan",
                        Success = true,
                        ExitCode = 0,
                        Config = "storesubmit",
                        ConfigPath = loaded.FullPath,
                        Spec = CliJson.SerializeToElement(safeSpec, CliJson.Context.StoreSubmissionSpec),
                        Plan = CliJson.SerializeToElement(prepared, CliJson.Context.StoreSubmissionPlan),
                        Logs = LogsToJsonElement(logBuffer)
                    });
                    return 0;
                }

                cmdLogger.Success($"Planned Store submission for '{prepared.TargetName}'.");
                cmdLogger.Info($"Provider: {prepared.Provider}");
                cmdLogger.Info($"ApplicationId: {prepared.ApplicationId}");
                if (!string.IsNullOrWhiteSpace(prepared.SubmissionId))
                    cmdLogger.Info($"SubmissionId: {prepared.SubmissionId}");
                if (prepared.Provider == StoreSubmissionProviderKind.PackagedApp)
                {
                    cmdLogger.Info($"ZIP: {prepared.ZipPath}");
                    foreach (var file in prepared.PackageFiles)
                        cmdLogger.Info($" -> {file}");
                }
                else
                {
                    foreach (var pkg in prepared.DesktopPackages)
                        cmdLogger.Info($" -> {pkg.PackageType} {pkg.PackageUrl} [{string.Join(", ", pkg.Architectures)}]");
                }
                return 0;
            }

            var result = RunWithStatus(outputJson, cli, $"Submitting {prepared.TargetName} to Microsoft Store", () =>
                service.RunAsync(loaded.Value, loaded.FullPath, request).GetAwaiter().GetResult());
            var exitCode = result.Succeeded ? 0 : 1;

            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "store.submit",
                    Success = exitCode == 0,
                    ExitCode = exitCode,
                    Error = exitCode == 0 ? null : result.ErrorMessage,
                    Config = "storesubmit",
                    ConfigPath = loaded.FullPath,
                    Spec = CliJson.SerializeToElement(safeSpec, CliJson.Context.StoreSubmissionSpec),
                    Plan = result.Plan is null ? null : CliJson.SerializeToElement(result.Plan, CliJson.Context.StoreSubmissionPlan),
                    Result = CliJson.SerializeToElement(result, CliJson.Context.StoreSubmissionResult),
                    Logs = LogsToJsonElement(logBuffer)
                });
                return exitCode;
            }

            if (exitCode != 0)
            {
                cmdLogger.Error(result.ErrorMessage ?? "Store submission failed.");
                return exitCode;
            }

            cmdLogger.Success($"Store submission completed for '{prepared.TargetName}'.");
            if (!string.IsNullOrWhiteSpace(result.SubmissionId))
                cmdLogger.Info($"SubmissionId: {result.SubmissionId}");
            if (!string.IsNullOrWhiteSpace(result.PackageZipPath))
                cmdLogger.Info($"ZIP: {result.PackageZipPath}");
            if (!string.IsNullOrWhiteSpace(result.FinalStatus))
                cmdLogger.Info($"Status: {result.FinalStatus}");
            if (!string.IsNullOrWhiteSpace(result.StatusDetails))
                cmdLogger.Info($"Details: {result.StatusDetails}");

            return 0;
        }
        catch (Exception ex)
        {
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "store.submit",
                    Success = false,
                    ExitCode = 1,
                    Error = ex.Message
                });
                return 1;
            }

            logger.Error(ex.Message);
            return 1;
        }
    }

    private static void WriteStoreUsageEnvelope(string command, bool success, int exitCode)
    {
        WriteJson(new CliJsonEnvelope
        {
            SchemaVersion = OutputSchemaVersion,
            Command = command,
            Success = success,
            ExitCode = exitCode,
            Error = success ? null : "Usage requested or invalid store command invocation.",
            Result = System.Text.Json.JsonSerializer.SerializeToElement(new { usage = StoreUsage })
        });
    }
}
