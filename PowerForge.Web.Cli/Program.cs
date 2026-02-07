using PowerForge.Web.Cli;
using static PowerForge.Web.Cli.WebCliHelpers;

const int OutputSchemaVersion = 1;

var argv = args ?? Array.Empty<string>();
if (argv.Length == 0 || argv[0].Equals("-h", StringComparison.OrdinalIgnoreCase) || argv[0].Equals("--help", StringComparison.OrdinalIgnoreCase))
{
    PrintUsage();
    return 0;
}

var subCommand = argv[0].ToLowerInvariant();
var subArgs = argv.Skip(1).ToArray();
var outputJson = IsJsonOutput(subArgs);
EnsureUtf8ConsoleEncoding();
var logger = new WebConsoleLogger();

try
{
    return WebCliCommandHandlers.HandleSubCommand(subCommand, subArgs, outputJson, logger, OutputSchemaVersion);
}
catch (Exception ex)
{
    if (outputJson)
    {
        WebCliJsonWriter.Write(new WebCliJsonEnvelope
        {
            SchemaVersion = OutputSchemaVersion,
            Command = "web",
            Success = false,
            ExitCode = 1,
            Error = ex.Message
        });
        return 1;
    }

    logger.Error(ex.Message);
    return 1;
}


