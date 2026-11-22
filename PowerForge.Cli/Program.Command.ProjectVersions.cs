using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private const string ProjectVersionsUsage =
        "Usage: powerforge project versions [--project-root <directory>] [--module-name <name>] [--output json]";

    private static bool IsProjectVersionsInvocation(string[] args)
        => args.Length >= 2 && args[0].Equals("project", StringComparison.OrdinalIgnoreCase)
            && args[1].Equals("versions", StringComparison.OrdinalIgnoreCase);

    private static int CommandProjectVersions(string[] args, CliOptions cli, ILogger logger)
    {
        const string command = "project.versions";
        var outputJson = IsJsonOutput(args);
        var (_, logBuffer) = CreateCommandLogger(outputJson, cli, logger);
        try
        {
            if (args.Any(argument => argument is "--help" or "-h"))
            {
                if (outputJson)
                    WriteModuleCommandResult(command, ProjectVersionsUsage, ModuleCliJson.Default.String, logBuffer);
                else
                    Console.WriteLine(ProjectVersionsUsage);
                return 0;
            }

            var (options, _) = ParseStandaloneCommandOptions(args, "project versions");
            var items = new ProjectVersionService().Discover(new ProjectVersionQueryRequest
            {
                RootPath = options.GetValueOrDefault("--project-root") ?? Directory.GetCurrentDirectory(),
                ModuleName = options.GetValueOrDefault("--module-name")
            }).ToArray();
            if (outputJson)
                WriteModuleCommandResult(command, items, ModuleCliJson.Default.ProjectVersionInfoArray, logBuffer);
            else
                foreach (var item in items)
                    Console.WriteLine($"{item.Version}\t{item.Source}");
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            var exitCode = exception is ArgumentException ? 2 : 1;
            if (outputJson)
                WriteJson(new CliJsonEnvelope { SchemaVersion = OutputSchemaVersion, Command = command, Success = false, ExitCode = exitCode, Error = exception.Message });
            else
                logger.Error(exception.Message);
            return exitCode;
        }
    }
}
