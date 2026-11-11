using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private const string ScoopUsage = "Usage: powerforge release prepare-scoop --config <release.json> --manifest <release-manifest.json> --checksums <SHA256SUMS.txt> --asset-root <downloads> --out <new-directory> [--output json]";

    private static int CommandScoop(string[] args, ILogger logger)
    {
        var json = IsJsonOutput(args);
        try
        {
            if (args.Any(arg => arg is "-h" or "--help"))
            {
                if (json) WriteJson(new CliJsonEnvelope { Command = "release.prepare-scoop", Success = true, Result = CliJson.SerializeToElement(ScoopUsage, CliJson.Context.String) });
                else Console.WriteLine(ScoopUsage);
                return 0;
            }
            string Required(string name) => TryGetOptionValue(args, name) ?? throw new ArgumentException("Missing " + name + ". " + ScoopUsage);
            var loaded = LoadPowerForgeReleaseSpecWithPath(Required("--config"));
            var path = new ScoopManifestPreparationService().Prepare(loaded.Value, Required("--manifest"),
                Required("--checksums"), Required("--asset-root"), Required("--out"));
            if (json) WriteJson(new CliJsonEnvelope { SchemaVersion = OutputSchemaVersion, Command = "release.prepare-scoop", Success = true, ExitCode = 0, Result = CliJson.SerializeToElement(path, CliJson.Context.String) });
            else logger.Success(path);
            return 0;
        }
        catch (Exception exception)
        {
            if (json) WriteJson(new CliJsonEnvelope { SchemaVersion = OutputSchemaVersion, Command = "release.prepare-scoop", Success = false, ExitCode = 2, Error = exception.Message });
            else logger.Error(exception.Message);
            return 2;
        }
    }
}
