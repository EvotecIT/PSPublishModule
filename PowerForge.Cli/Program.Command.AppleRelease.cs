using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private const string AppleReleaseUsage =
        "Usage: powerforge apple-release <Status|Archive|Upload|UploadExisting|Prepare|Screenshots|TestFlight|SubmitTestFlightReview|SubmitAppReview|Release|Cleanup> " +
        "[--config <release.json>] [--plan] [--validate] [--confirm-apple-action] " +
        "[--apple-resume|--no-apple-resume] [--apple-wait|--no-apple-wait] " +
        "[--apple-timeout-seconds <seconds>] [--apple-poll-seconds <seconds>] " +
        "[--target <Name[,Name...]>] [--summary] [--output json]";

    private static int CommandAppleRelease(string[] filteredArgs, CliOptions cli, ILogger logger)
    {
        var argv = filteredArgs.Skip(1).ToArray();
        var outputJson = IsJsonOutput(argv);
        if (argv.Length == 0 ||
            argv.Any(static value =>
                value.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
                value.Equals("--help", StringComparison.OrdinalIgnoreCase)))
        {
            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "apple-release",
                    Success = true,
                    ExitCode = 0,
                    Result = System.Text.Json.JsonSerializer.SerializeToElement(new { usage = AppleReleaseUsage })
                });
            }
            else
            {
                Console.WriteLine(AppleReleaseUsage);
            }

            return argv.Length == 0 ? 2 : 0;
        }

        var action = argv[0];
        if (action.StartsWith("-", StringComparison.Ordinal))
            return WriteReleaseError(outputJson, "apple-release", 2, $"Missing Apple release action. {AppleReleaseUsage}", logger);

        try
        {
            ValidateAppleReleaseArguments(argv.Skip(1).ToArray());
            var parsedAction = ParseAppleReleaseAction(action);
            if (parsedAction == PowerForgeAppleReleaseAction.Configured)
            {
                throw new ArgumentException(
                    "The dedicated apple-release command requires an explicit named action. " +
                    "Use the general release command only when intentionally running the legacy Configured workflow.");
            }

            var releaseArgs = new[] { "release", "--apple-action", parsedAction.ToString() }
                .Concat(argv.Skip(1))
                .ToArray();
            return CommandRelease(releaseArgs, cli, logger, commandName: "apple-release");
        }
        catch (Exception exception)
        {
            return WriteReleaseError(outputJson, "apple-release", 2, exception.Message, logger);
        }
    }

    private static void ValidateAppleReleaseArguments(string[] argv)
    {
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "--plan",
            "--dry-run",
            "--validate",
            "--confirm-apple-action",
            "--apple-resume",
            "--no-apple-resume",
            "--apple-wait",
            "--no-apple-wait",
            "--summary"
        };
        var options = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "--config",
            "--target",
            "--apple-timeout-seconds",
            "--apple-poll-seconds",
            "--output"
        };

        for (var index = 0; index < argv.Length; index++)
        {
            var argument = argv[index];
            if (flags.Contains(argument))
                continue;
            if (!options.Contains(argument))
                throw new ArgumentException($"Unknown apple-release option '{argument}'.");
            if (++index >= argv.Length || argv[index].StartsWith("-", StringComparison.Ordinal))
                throw new ArgumentException($"Missing value for apple-release option '{argument}'.");
        }
    }
}
