using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private const string ReleaseCatalogUsage = "Usage: powerforge release prepare-catalog --config <release.json> --manifest <release-manifest.json> --checksums <SHA256SUMS.txt> --asset-root <downloads> --out <new-directory> [--output json]";

    private static int CommandReleaseCatalog(string[] args, ILogger logger)
    {
        var json = IsJsonOutput(args);
        try
        {
            if (args.Any(arg => arg is "-h" or "--help"))
            {
                if (json) WriteJson(new CliJsonEnvelope { Command = "release.prepare-catalog", Success = true, Result = CliJson.SerializeToElement(ReleaseCatalogUsage, CliJson.Context.String) });
                else Console.WriteLine(ReleaseCatalogUsage);
                return 0;
            }
            string Required(string name) => TryGetOptionValue(args, name) ?? throw new ArgumentException("Missing " + name + ". " + ReleaseCatalogUsage);
            var loaded = LoadPowerForgeReleaseSpecWithPath(Required("--config"));
            var result = new ReleaseCatalogPreparationService().Prepare(loaded.Value,
                Required("--manifest"), Required("--checksums"), Required("--asset-root"), Required("--out"));
            if (json) WriteJson(new CliJsonEnvelope { SchemaVersion = OutputSchemaVersion, Command = "release.prepare-catalog", Success = true, ExitCode = 0, Result = CliJson.SerializeToElement(result, CliJson.Context.ReleaseCatalogPreparationResult) });
            else
            {
                foreach (var path in result.WingetManifestPaths) logger.Success(path);
                logger.Success(result.DesktopPackagesPath);
            }
            return 0;
        }
        catch (Exception exception)
        {
            if (json) WriteJson(new CliJsonEnvelope { SchemaVersion = OutputSchemaVersion, Command = "release.prepare-catalog", Success = false, ExitCode = 2, Error = exception.Message });
            else logger.Error(exception.Message);
            return 2;
        }
    }
}
