using PowerForge;
using PowerForge.Cli;
using System;
using System.IO;

internal static partial class Program
{
    private static int CommandGitHubRunnerStorage(string[] argv, CliOptions cli, ILogger logger)
    {
        var outputJson = IsJsonOutput(argv);
        if (argv.Length > 0 && IsHelpArg(argv[0]))
        {
            Console.WriteLine(GitHubRunnerStorageUsage);
            return 2;
        }

        MacOsRunnerStorageProvisioningSpec spec;
        try
        {
            spec = ParseGitHubRunnerStorageArgs(argv);
        }
        catch (Exception ex)
        {
            return WriteGitHubCommandArgumentError(
                outputJson,
                "github.runner.storage",
                ex.Message,
                GitHubRunnerStorageUsage,
                logger);
        }

        try
        {
            var (cmdLogger, logBuffer) = CreateCommandLogger(outputJson, cli, logger);
            var service = new MacOsRunnerStorageProvisioningService(cmdLogger);
            var statusText = spec.DryRun
                ? "Planning macOS runner storage"
                : "Provisioning macOS runner storage";
            var result = RunWithStatus(outputJson, cli, statusText, () => service.Provision(spec));
            var exitCode = 0;

            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "github.runner.storage",
                    Success = true,
                    ExitCode = exitCode,
                    Result = CliJson.SerializeToElement(
                        result,
                        CliJson.Context.MacOsRunnerStorageProvisioningResult),
                    Logs = LogsToJsonElement(logBuffer)
                });
                return exitCode;
            }

            logger.Info(result.DryRun ? "Runner storage plan:" : "Runner storage applied:");
            logger.Info($"  state: {result.StateRootPath}");
            logger.Info($"  work: {result.WorkRootPath}");
            logger.Info($"  CoreSimulator: {result.CoreSimulatorImagePath}");
            logger.Info($"  wrapper: {result.RunnerWrapperPath}");
            logger.Info($"  already configured: {(result.AlreadyConfigured ? "yes" : "no")}");
            if (!result.DryRun)
                logger.Info($"  recoverable backups: {result.BackupRootPath}");
            return exitCode;
        }
        catch (Exception ex)
        {
            return WriteGitHubCommandFailure(
                outputJson,
                "github.runner.storage",
                ex.Message,
                logger);
        }
    }

    private static MacOsRunnerStorageProvisioningSpec ParseGitHubRunnerStorageArgs(string[] argv)
    {
        var spec = new MacOsRunnerStorageProvisioningSpec
        {
            RunnerRootPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "actions-runner")
        };

        for (var i = 0; i < argv.Length; i++)
        {
            var arg = argv[i];
            switch (arg.ToLowerInvariant())
            {
                case "--runner-root":
                    spec.RunnerRootPath = ReadValue(argv, ref i, arg);
                    break;
                case "--state-root":
                    spec.StateRootPath = ReadValue(argv, ref i, arg);
                    break;
                case "--work-root":
                    spec.WorkRootPath = ReadValue(argv, ref i, arg);
                    break;
                case "--core-simulator-path":
                    spec.CoreSimulatorPath = ReadValue(argv, ref i, arg);
                    break;
                case "--launch-agent":
                    spec.LaunchAgentPath = ReadValue(argv, ref i, arg);
                    break;
                case "--core-simulator-size-gb":
                    spec.CoreSimulatorImageSizeGb = ParseRequiredInt(argv, ref i, arg, minimum: 20);
                    break;
                case "--external-storage-wait-seconds":
                    spec.ExternalStorageWaitSeconds = ParseRequiredInt(argv, ref i, arg, minimum: 0);
                    if (spec.ExternalStorageWaitSeconds > 900)
                        throw new ArgumentOutOfRangeException(arg, "External storage wait must not exceed 900 seconds.");
                    break;
                case "--dry-run":
                    spec.DryRun = true;
                    break;
                case "--apply":
                    spec.DryRun = false;
                    break;
                case "--output":
                    i++;
                    break;
                case "--output-json":
                case "--json":
                    break;
                default:
                    ThrowOnUnknownOption(arg);
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(spec.StateRootPath))
            throw new ArgumentException("--state-root is required.");
        if (string.IsNullOrWhiteSpace(spec.WorkRootPath))
            throw new ArgumentException("--work-root is required.");
        return spec;
    }

    private static string ReadValue(string[] argv, ref int index, string option)
    {
        if (++index >= argv.Length || string.IsNullOrWhiteSpace(argv[index]))
            throw new ArgumentException($"{option} requires a value.");
        return argv[index];
    }
}
