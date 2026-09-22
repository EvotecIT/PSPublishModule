using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private const string PowerShellProjectPackUsage =
        "Usage: powerforge powershell project pack <project> [--format <zip|nuget>] [--target <name>] [--output json]";

    private static int CommandPowerShellProjectPack(string[] args, bool outputJson, ILogger logger)
    {
        if (!TryValidatePowerShellArguments(args, new[] { "--project", "--target", "--format", "--output" },
                new[] { "--json", "--output-json" }, out var positionalProject, out var error))
            return WritePowerShellError(outputJson, 2, error, logger, "powershell.project.pack");
        var projectPath = TryGetOptionValue(args, "--project") ?? positionalProject;
        if (string.IsNullOrWhiteSpace(projectPath))
            return WritePowerShellError(outputJson, 2, "A PowerShell compilation project path is required.", logger, "powershell.project.pack");
        var format = TryGetOptionValue(args, "--format") ?? "zip";
        if (!format.Equals("zip", StringComparison.OrdinalIgnoreCase) && !format.Equals("nuget", StringComparison.OrdinalIgnoreCase))
            return WritePowerShellError(outputJson, 2, "Package format must be zip or nuget.", logger, "powershell.project.pack");
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
        var nuget = format.Equals("nuget", StringComparison.OrdinalIgnoreCase);
        if (nuget) Console.CancelKeyPress += cancel;
        try
        {
            var service = new PowerShellCompilationProjectWorkflowService();
            var targets = GetOptionValues(args, "--target").ToArray();
            var result = nuget ? service.PackNuGet(projectPath, targets, cancellation.Token) : service.Pack(projectPath, targets);
            return WritePowerShellProjectResult(result, outputJson, logger);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return WritePowerShellError(outputJson, 130, "NuGet packaging was canceled.", logger, "powershell.project.pack");
        }
        catch (Exception exception)
        {
            return WritePowerShellError(outputJson, 1, exception.Message, logger, "powershell.project.pack");
        }
        finally { if (nuget) Console.CancelKeyPress -= cancel; }
    }
}
