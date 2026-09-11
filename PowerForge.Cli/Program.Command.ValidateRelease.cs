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
            var config = TryGetOptionValue(args, "--config");
            if (string.IsNullOrWhiteSpace(config)) { logger.Error(usage); return 2; }
            var request = new ReleaseValidationRequest
            {
                ProjectRoot = TryGetOptionValue(args, "--project-root"), Version = TryGetOptionValue(args, "--version")
            };
            for (var index = 1; index < args.Length; index++)
            {
                if (args[index] != "--variable") continue;
                if (++index >= args.Length) throw new ArgumentException("--variable requires Name=Value.");
                var separator = args[index].IndexOf('=');
                if (separator <= 0) throw new ArgumentException("--variable requires Name=Value.");
                request.Variables.Add(args[index].Substring(0, separator), args[index].Substring(separator + 1));
            }
            var report = new ReleaseValidationService().RunAsync(ReleaseValidationService.Load(config!), config, request, cancellation.Token).GetAwaiter().GetResult();
            if (IsJsonOutput(args)) Console.WriteLine(ReleaseValidationService.SerializeReport(report));
            else
            {
                foreach (var check in report.Checks) logger.Success(check);
                foreach (var error in report.Errors) logger.Error(error);
            }
            return report.Success ? 0 : 1;
        }
        catch (OperationCanceledException) { logger.Error("Release validation canceled."); return 130; }
        catch (Exception exception) { logger.Error(exception.Message); return 1; }
        finally { Console.CancelKeyPress -= handler; }
    }
}
