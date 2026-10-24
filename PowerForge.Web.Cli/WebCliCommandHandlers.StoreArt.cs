using System.Text.Json;
using PowerForge.Web;
using static PowerForge.Web.Cli.WebCliHelpers;

namespace PowerForge.Web.Cli;

internal static partial class WebCliCommandHandlers
{
    private static int HandleStoreArt(string[] args, bool outputJson, WebConsoleLogger logger, int schemaVersion)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h")
        {
            Console.WriteLine("Usage: powerforge-web apple-store-art compose --config <store-art.json> --out <fresh-directory> [--browser-executable <chromium>] [--output json]");
            return args.Length == 0 ? 2 : 0;
        }
        if (args[0] != "compose") throw new ArgumentException("Apple store art operation must be compose.");
        var config = TryGetOptionValue(args, "--config") ?? throw new ArgumentException("--config is required.");
        var output = TryGetOptionValue(args, "--out") ?? throw new ArgumentException("--out is required.");
        var result = new AppleStoreArtComposer().ComposeAsync(config, output, TryGetOptionValue(args, "--browser-executable")).GetAwaiter().GetResult();
        if (outputJson)
            WebCliJsonWriter.Write(new WebCliJsonEnvelope { SchemaVersion = schemaVersion, Command = "apple-store-art.compose",
                Success = true, ExitCode = 0, Result = JsonSerializer.SerializeToElement(new { outputPath = Path.GetFullPath(output), frames = result }) });
        else logger.Success($"Composed {result.Length} store frame(s): {Path.GetFullPath(output)}");
        return 0;
    }
}
