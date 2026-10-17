using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private const string DotNetNoticesUsage =
        "Usage: powerforge dotnet notices --project-root <path> --deps-json <path> --manifest <path> --out <path> [--output json]";

    private static int CommandDotNetNotices(string[] args, ILogger logger)
    {
        var json = IsJsonOutput(args);
        if (args.Any(IsHelpArg)) { Console.WriteLine(DotNetNoticesUsage); return 0; }
        try
        {
            string Required(string name) => TryGetOptionValue(args, name)
                ?? throw new ArgumentException($"Missing {name}. {DotNetNoticesUsage}");
            DotNetPublishThirdPartyNotices.Generate(
                Path.GetFullPath(Required("--project-root")), Path.GetFullPath(Required("--deps-json")),
                Path.GetFullPath(Required("--manifest")), Path.GetFullPath(Required("--out")));
            if (json) WriteJson(new CliJsonEnvelope
            {
                SchemaVersion = OutputSchemaVersion, Command = "dotnet.notices", Success = true, ExitCode = 0
            });
            else logger.Success("Exact runtime package notices generated.");
            return 0;
        }
        catch (Exception exception)
        {
            return WriteReleaseError(json, "dotnet.notices", exception is ArgumentException ? 2 : 1, exception.Message, logger);
        }
    }
}
