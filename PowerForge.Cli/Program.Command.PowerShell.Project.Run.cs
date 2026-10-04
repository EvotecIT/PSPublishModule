using PowerForge;

internal static partial class Program
{
    private const string PowerShellProjectRunUsage =
        "Usage: powerforge powershell project <run|watch> <project> [--target <name>] [-- <application arguments...>]";

    private static int CommandPowerShellProjectRun(string operation, string[] args, bool outputJson)
    {
        var separator = Array.IndexOf(args, "--");
        var options = separator < 0 ? args : args.Take(separator).ToArray();
        var applicationArguments = separator < 0 ? Array.Empty<string>() : args.Skip(separator + 1).ToArray();
        if (outputJson)
            return Error("Run/watch preserves application streams and does not support --output json. Put application options after --.");
        if (!TryValidatePowerShellArguments(options, new[] { "--project", "--target" }, Array.Empty<string>(),
                out var positionalProject, out var error))
            return Error(error);
        var projectPath = TryGetOptionValue(options, "--project") ?? positionalProject;
        if (string.IsNullOrWhiteSpace(projectPath)) return Error("A PowerShell compilation project path is required.");
        var targets = GetOptionValues(options, "--target").ToArray();
        if (targets.Length > 1) return Error("Run/watch accepts exactly one --target.");
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancel;
        try
        {
            var service = new PowerShellCompilationProjectWorkflowService();
            var request = new PowerShellCompilationProjectRunOptions
            {
                TargetName = targets.SingleOrDefault(), Arguments = applicationArguments
            };
            if (operation == "watch")
            {
                service.WatchAsync(projectPath, request, Progress, cancellation.Token).GetAwaiter().GetResult();
                return 0;
            }
            return service.RunAsync(projectPath, request, Progress, cancellation.Token).GetAwaiter().GetResult().ExitCode;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return 130; }
        catch (Exception exception) { Console.Error.WriteLine(exception.Message); return 1; }
        finally { Console.CancelKeyPress -= cancel; }

        void Progress(PowerShellCompilationProjectRunEvent update)
        {
            // Run stdout remains exactly the application's stdout, including redirected binary data.
            if (operation == "watch" || update.State == "failed")
                Console.Error.WriteLine("powerforge: " + update.Message);
        }

        static int Error(string message) { Console.Error.WriteLine(message); return 2; }
    }
}
