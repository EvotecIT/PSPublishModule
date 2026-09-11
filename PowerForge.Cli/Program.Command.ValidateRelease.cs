using PowerForge;

internal static partial class Program
{
    private static int CommandValidateRelease(string[] args, ILogger logger)
    {
        const string usage = "Usage: powerforge validate-release --config <validation.json> [--project-root <path>] [--version <version>] [--variable <Name=Value>] [--output json]";
        if (args.Any(value => value is "--help" or "-h")) { Console.WriteLine(usage); return 0; }
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, value) => { value.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        try
        {
            var (config, request) = ParseReleaseValidationArguments(args);
            if (string.IsNullOrWhiteSpace(config)) return WriteReleaseError(IsJsonOutput(args), "validate-release", 2, usage, logger);
            var report = new ReleaseValidationService().RunAsync(ReleaseValidationService.Load(config!), config, request, cancellation.Token).GetAwaiter().GetResult();
            if (IsJsonOutput(args)) Console.WriteLine(ReleaseValidationService.SerializeReport(report));
            else
            {
                foreach (var check in report.Checks) logger.Success(check);
                foreach (var error in report.Errors) logger.Error(error);
            }
            return report.Success ? 0 : 1;
        }
        catch (OperationCanceledException) { return WriteReleaseError(IsJsonOutput(args), "validate-release", 130, "Release validation canceled.", logger); }
        catch (Exception exception) { return WriteReleaseError(IsJsonOutput(args), "validate-release", 1, exception.Message, logger); }
        finally { Console.CancelKeyPress -= handler; }
    }

    private static (string? Config, ReleaseValidationRequest Request) ParseReleaseValidationArguments(string[] args)
    {
        string? config = null;
        var request = new ReleaseValidationRequest();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; index < args.Length; index++)
        {
            var option = args[index].ToLowerInvariant();
            if (option is "--json" or "--output-json") continue;
            if (option is not ("--config" or "--project-root" or "--version" or "--variable" or "--output"))
                throw new ArgumentException($"Unknown validate-release option '{args[index]}'.");
            if (option != "--variable" && !seen.Add(option))
                throw new ArgumentException($"Duplicate validate-release option '{option}'.");
            if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]) || args[index].StartsWith("-", StringComparison.Ordinal))
                throw new ArgumentException($"Missing value for validate-release option '{option}'.");
            var value = args[index];
            switch (option)
            {
                case "--config": config = value; break;
                case "--project-root": request.ProjectRoot = value; break;
                case "--version": request.Version = value; break;
                case "--output":
                    if (!value.Equals("json", StringComparison.OrdinalIgnoreCase) && !value.Equals("text", StringComparison.OrdinalIgnoreCase))
                        throw new ArgumentException("--output requires json or text.");
                    break;
                case "--variable":
                    var separator = value.IndexOf('=');
                    if (separator <= 0 || string.IsNullOrWhiteSpace(value.Substring(0, separator)))
                        throw new ArgumentException("--variable requires Name=Value.");
                    request.Variables.Add(value.Substring(0, separator), value.Substring(separator + 1));
                    break;
            }
        }
        return (config, request);
    }
}
