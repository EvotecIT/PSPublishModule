using System.Text.Json;
using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private const string RunnerMaintenanceUsage = "powerforge github runner maintenance --config <policy.json> [--install | --apply] [--json]\n" +
        "powerforge github runner maintenance --hook-config <enrolled-hook.json>";

    private static int CommandGitHubRunnerMaintenance(string[] args, CliOptions cli, ILogger logger)
    {
        string? configuration = null;
        string? hook = null;
        var apply = false;
        var install = false;
        var json = args.Contains("--json", StringComparer.OrdinalIgnoreCase);
        try
        {
            for (var index = 0; index < args.Length; index++)
            {
                switch (args[index].ToLowerInvariant())
                {
                    case "--config": configuration = ReadValue(args, ref index, args[index]); break;
                    case "--hook-config": hook = ReadValue(args, ref index, args[index]); break;
                    case "--apply": apply = true; break;
                    case "--install": install = true; break;
                    case "--json": break;
                    default: throw new ArgumentException("Unknown maintenance argument '" + args[index] + "'.");
                }
            }
            if ((configuration is null) == (hook is null) || (hook is not null && (apply || install)) || (install && apply))
                throw new ArgumentException("Choose one policy operation or an enrolled hook.");
            var service = new RunnerHousekeepingService(logger);
            RunnerMaintenanceResult result;
            if (hook is not null) result = service.InvokeMaintenanceHook(Path.GetFullPath(hook));
            else
            {
                var path = Path.GetFullPath(configuration!);
                var spec = JsonSerializer.Deserialize(File.ReadAllText(path), PowerForgeCliJsonContext.Default.RunnerMaintenanceSpec)
                    ?? throw new ArgumentException("Empty maintenance policy.");
                Environment.SetEnvironmentVariable("NUGET_SCRATCH", spec.NuGetScratchPath);
                if (install)
                {
                    service.InstallMaintenance(spec);
                    Console.WriteLine("Runner hooks enrolled. Register the native scheduler before restarting the enrolled listeners.");
                    return 0;
                }
                result = service.Maintain(spec, dryRun: !apply);
            }
            if (hook is not null)
                Console.WriteLine($"Runner maintenance removed {result.RemovedPaths.Length} targets ({result.RemovedBytes} logical bytes), deferred {result.Deferred}, errors {result.Errors.Length}. Detailed receipts are in the private maintenance state directory.");
            else Console.WriteLine(JsonSerializer.Serialize(result, PowerForgeCliJsonContext.Default.RunnerMaintenanceResult));
            // A cleanup error is observable in its receipt. Admission failure must stop a new job from entering the gate.
            return result.Success || (hook is not null && result.Admitted) ? 0 : 1;
        }
        catch (Exception error)
        {
            return WriteGitHubCommandArgumentError(json, "github.runner.maintenance", error.Message, RunnerMaintenanceUsage, logger);
        }
    }
}
