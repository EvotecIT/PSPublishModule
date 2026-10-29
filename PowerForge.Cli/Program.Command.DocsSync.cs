using PowerForge;
using PowerForge.Cli;
using System.Text.Json;

internal static partial class Program
{
    private static int CommandDocsSync(string[] argv, ILogger logger)
    {
        const string usage = "Usage: powerforge docs sync --config <docs-sync.json> [--apply] [--output json]";
        argv = argv.TakeWhile(value => value != "--").ToArray();
        if (argv.Contains("--help", StringComparer.OrdinalIgnoreCase) || argv.Contains("-h", StringComparer.OrdinalIgnoreCase)) { Console.WriteLine(usage); return 0; }
        var outputJson = IsJsonOutput(argv);
        try
        {
            var config = TryGetOptionValue(argv, "--config");
            if (string.IsNullOrWhiteSpace(config)) throw new ArgumentException(usage);
            var fullPath = Path.GetFullPath(config);
            var spec = JsonSerializer.Deserialize(File.ReadAllText(fullPath), CliJson.Context.MarkdownCodeSyncSpec)
                ?? throw new InvalidDataException("Documentation synchronization configuration is empty.");
            spec.RootPath = ResolvePathFromBase(Path.GetDirectoryName(fullPath)!, spec.RootPath);
            var apply = argv.Contains("--apply", StringComparer.OrdinalIgnoreCase);
            var result = MarkdownCodeSynchronizer.Synchronize(spec, apply);
            var exitCode = apply || result.ChangedFiles.Length == 0 ? 0 : 1;
            if (outputJson)
                WriteJson(new CliJsonEnvelope { SchemaVersion = OutputSchemaVersion, Command = "docs.sync", Success = exitCode == 0,
                    ExitCode = exitCode, Result = CliJson.SerializeToElement(result, CliJson.Context.MarkdownCodeSyncResult) });
            else
            {
                logger.Info($"Checked {result.BlockCount} code blocks in {result.FileCount} Markdown files; {result.ChangedFiles.Length} {(apply ? "updated" : "stale")} files.");
                foreach (var file in result.ChangedFiles) logger.Info(file);
            }
            return exitCode;
        }
        catch (Exception exception)
        {
            if (outputJson) WriteJson(new CliJsonEnvelope { SchemaVersion = OutputSchemaVersion, Command = "docs.sync", Success = false, ExitCode = 2, Error = exception.Message });
            else logger.Error(exception.Message);
            return 2;
        }
    }
}
