using PowerForge.Web;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static int HandleStoreArt(string[] args, bool outputJson, WebConsoleLogger logger, int schemaVersion)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            Console.WriteLine("Usage: powerforge-web apple-store-art compose --config <store-art.json> --out <fresh-directory> [--locales en-US,pl] [--devices iphone-69,watch] [--verify-existing] [--browser-executable <chromium>] [--output json]");
            return args.Length == 0 ? 2 : 0;
        }
        if (args[0] != "compose") throw new ArgumentException("Apple store art operation must be compose.");
        var config = TryGetOptionValue(args, "--config") ?? throw new ArgumentException("--config is required.");
        var output = TryGetOptionValue(args, "--out") ?? throw new ArgumentException("--out is required.");
        var localeOption = TryGetOptionValue(args, "--locales");
        var locales = localeOption is null or "all" ? null : localeOption.Split(',');
        var devices = TryGetOptionValue(args, "--devices")?.Split(',');
        var composer = new AppleStoreArtComposer();
        var result = args.Contains("--verify-existing", StringComparer.Ordinal)
            ? composer.Verify(config, output, locales, devices)
            : composer.ComposeAsync(config, output, TryGetOptionValue(args, "--browser-executable"), CancellationToken.None,
                locales: locales, devices: devices).GetAwaiter().GetResult();
        if (outputJson)
            WebCliJsonWriter.Write(new WebCliJsonEnvelope { SchemaVersion = schemaVersion, Command = "apple-store-art.compose",
                Success = true, ExitCode = 0, Result = WebCliJson.SerializeToElement(
                    new WebStoreArtComposeResult(Path.GetFullPath(output), result), WebCliJson.Context.WebStoreArtComposeResult) });
        else logger.Success($"Validated {result.Length} store frame(s): {Path.GetFullPath(output)}");
        return 0;
    }
}
