using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private const string AgentPluginUsage = "Usage: powerforge agent-plugin <validate|sync|pack> --source <plugin-directory> [--out <archive-directory>] [--output json]";

    private static int CommandAgentPlugin(string[] filteredArgs, CliOptions cli, ILogger logger)
    {
        var argv = filteredArgs.Skip(1).ToArray();
        var outputJson = IsJsonOutput(argv);
        if (argv.Length == 0 || argv.Any(a => a == "--help" || a == "-h"))
        {
            if (outputJson) WriteJson(new CliJsonEnvelope { SchemaVersion = OutputSchemaVersion, Command = "agent-plugin", Success = true, ExitCode = 0, Result = System.Text.Json.JsonSerializer.SerializeToElement(AgentPluginUsage, CliJson.Context.String) });
            else Console.WriteLine(AgentPluginUsage);
            return 0;
        }
        var command = "agent-plugin." + argv[0];
        try
        {
            var source = TryGetOptionValue(argv, "--source");
            if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("Missing --source plugin directory.");
            var service = new AgentPluginPackageService();
            AgentPluginPackageResult result;
            switch (argv[0])
            {
                case "validate": result = service.Validate(source!); break;
                case "sync": result = service.SyncCompatibility(source!); break;
                case "pack":
                    var output = TryGetOptionValue(argv, "--out");
                    if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("Packing requires --out archive directory.");
                    result = service.Pack(source!, output!);
                    break;
                default: throw new ArgumentException("Unknown agent-plugin subcommand: " + argv[0]);
            }
            if (outputJson)
                WriteJson(new CliJsonEnvelope { SchemaVersion = OutputSchemaVersion, Command = command, Success = true, ExitCode = 0, Result = CliJson.SerializeToElement(result, CliJson.Context.AgentPluginPackageResult) });
            else
            {
                logger.Success($"{result.Name}@{result.Version}: {result.FileCount} package files.");
                if (result.ArchivePath is not null) { logger.Info(result.ArchivePath); logger.Info("SHA-256: " + result.Sha256); }
            }
            return 0;
        }
        catch (Exception exception)
        {
            if (outputJson) WriteJson(new CliJsonEnvelope { SchemaVersion = OutputSchemaVersion, Command = command, Success = false, ExitCode = 2, Error = exception.Message });
            else logger.Error(exception.Message);
            return 2;
        }
    }
}
