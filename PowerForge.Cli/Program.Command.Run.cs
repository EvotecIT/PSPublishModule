using PowerForge;
using PowerForge.Cli;
using System.IO;
using System.Linq;

internal static partial class Program
{
    private const string RunUsage =
        "Usage: powerforge run [--config <run.profiles.json>] [--list] [--target <Name>] [--configuration <Release|Debug>] [--framework <tfm>] [--no-build] [--no-restore] [--allow-root <path[,path...]>] [--include-private-tool-packs] [--testimox-root <path>] [--extra-arg <value>] [--output json]";

    private static int CommandRun(string[] filteredArgs, CliOptions cli, ILogger logger)
    {
        var argv = filteredArgs.Skip(1).ToArray();
        var outputJson = IsJsonOutput(argv);
        var listOnly = argv.Any(a => a.Equals("--list", StringComparison.OrdinalIgnoreCase) || a.Equals("--ls", StringComparison.OrdinalIgnoreCase));
        var target = TryGetOptionValue(argv, "--target") ?? TryGetOptionValue(argv, "--name");
        var configuration = TryGetOptionValue(argv, "--configuration") ?? "Release";
        var framework = TryGetOptionValue(argv, "--framework");
        var noBuild = argv.Any(a => a.Equals("--no-build", StringComparison.OrdinalIgnoreCase));
        var noRestore = argv.Any(a => a.Equals("--no-restore", StringComparison.OrdinalIgnoreCase));
        var includePrivateToolPacks = argv.Any(a => a.Equals("--include-private-tool-packs", StringComparison.OrdinalIgnoreCase));
        var testimoXRoot = TryGetOptionValue(argv, "--testimox-root") ?? TryGetOptionValue(argv, "--testimoX-root");
        var allowRoots = ParseCsvOptionValues(argv, "--allow-root");
        var extraArgs = ParseRepeatedOptionValues(argv, "--extra-arg");

        var configPath = TryGetOptionValue(argv, "--config");
        if (string.IsNullOrWhiteSpace(configPath))
        {
            var baseDir = TryGetProjectRoot(argv);
            if (!string.IsNullOrWhiteSpace(baseDir))
                baseDir = Path.GetFullPath(baseDir.Trim().Trim('"'));
            else
                baseDir = Directory.GetCurrentDirectory();

            configPath = FindDefaultRunProfilesConfig(baseDir);
        }

        if (string.IsNullOrWhiteSpace(configPath))
        {
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "run",
                    Success = false,
                    ExitCode = 2,
                    Error = "Missing --config and no default run profiles config found."
                });
                return 2;
            }

            Console.WriteLine(RunUsage);
            return 2;
        }

        try
        {
            var (cmdLogger, logBuffer) = CreateCommandLogger(outputJson, cli, logger);
            var loaded = LoadRunProfileSpecWithPath(configPath);
            var service = new RunProfileService();
            var list = service.ListProfiles(loaded.Value);

            if (listOnly || string.IsNullOrWhiteSpace(target))
            {
                if (outputJson)
                {
                    WriteJson(new CliJsonEnvelope
                    {
                        SchemaVersion = OutputSchemaVersion,
                        Command = "run.list",
                        Success = true,
                        ExitCode = 0,
                        Config = "runprofiles",
                        ConfigPath = loaded.FullPath,
                        Spec = CliJson.SerializeToElement(loaded.Value, CliJson.Context.RunProfileSpec),
                        Results = CliJson.SerializeToElement(list, CliJson.Context.RunProfileSummaryArray),
                        Logs = LogsToJsonElement(logBuffer)
                    });
                    return 0;
                }

                cmdLogger.Info($"Run profiles: {loaded.FullPath}");
                foreach (var item in list)
                {
                    cmdLogger.Success(item.Name);
                    if (!string.IsNullOrWhiteSpace(item.Description))
                        cmdLogger.Info($"  {item.Description}");
                    if (!string.IsNullOrWhiteSpace(item.Example))
                        cmdLogger.Info($"  Example: {item.Example}");
                }
                return 0;
            }

            var request = new RunProfileExecutionRequest
            {
                TargetName = target,
                Configuration = configuration,
                Framework = framework,
                NoBuild = noBuild,
                NoRestore = noRestore,
                AllowRoot = allowRoots,
                IncludePrivateToolPacks = includePrivateToolPacks,
                TestimoXRoot = testimoXRoot,
                ExtraArgs = extraArgs,
                CaptureOutput = outputJson,
                CaptureError = outputJson
            };

            var prepared = service.Prepare(loaded.Value, loaded.FullPath, request);
            if (!outputJson)
            {
                cmdLogger.Info($"Run target: {prepared.TargetName}");
                cmdLogger.Info($"Command: {prepared.DisplayCommand}");
            }

            var result = RunWithStatus(outputJson, cli, $"Running {prepared.TargetName}", () =>
                service.RunAsync(loaded.Value, loaded.FullPath, request).GetAwaiter().GetResult());

            var exitCode = result.Succeeded ? 0 : (result.ExitCode == 0 ? 1 : result.ExitCode);

            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "run",
                    Success = exitCode == 0,
                    ExitCode = exitCode,
                    Error = exitCode == 0 ? null : (string.IsNullOrWhiteSpace(result.StdErr) ? $"Run target '{target}' failed." : result.StdErr.Trim()),
                    Config = "runprofiles",
                    ConfigPath = loaded.FullPath,
                    Spec = CliJson.SerializeToElement(loaded.Value, CliJson.Context.RunProfileSpec),
                    Plan = CliJson.SerializeToElement(prepared, CliJson.Context.RunProfilePreparedCommand),
                    Result = CliJson.SerializeToElement(result, CliJson.Context.RunProfileExecutionResult),
                    Logs = LogsToJsonElement(logBuffer)
                });
                return exitCode;
            }

            if (exitCode != 0)
            {
                if (!string.IsNullOrWhiteSpace(result.StdErr))
                    cmdLogger.Error(result.StdErr.Trim());
                return exitCode;
            }

            return 0;
        }
        catch (Exception ex)
        {
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "run",
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
}
