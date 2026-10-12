using PowerForge;
using PowerForge.Cli;
using System.Text.Json;

internal static partial class Program
{
    private static int CommandAppleTestFlight(string[] arguments, ILogger logger)
    {
        var argv = arguments.Skip(1).ToArray();
        var outputJson = IsJsonOutput(argv);
        try
        {
            if (argv.Length == 0 || argv[0] is not ("run" or "status" or "pause" or "resume" or "retire" or "checkpoint-evidence"))
                throw new ArgumentException("Usage: powerforge apple-testflight <run|status|pause|resume|retire> --policy <absolute-local-policy.json> --config <release.json> [--output json]");
            string? policyPath = null;
            string? config = null;
            string? job = null;
            var reconciled = false;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 1; index < argv.Length; index++)
            {
                var name = argv[index];
                if (!seen.Add(name)) throw new ArgumentException("Duplicate apple-testflight option: " + name);
                if (name == "--confirm-reconciled") { reconciled = true; continue; }
                if (name is not ("--policy" or "--config" or "--output" or "--job") || ++index >= argv.Length)
                    throw new ArgumentException("Unknown or incomplete apple-testflight option.");
                if (name == "--policy") policyPath = argv[index];
                if (name == "--config") config = argv[index];
                if (name == "--job") job = argv[index];
                if (name == "--output" && argv[index] != "json") throw new ArgumentException("apple-testflight output must be json.");
            }
            if (policyPath is null || !Path.IsPathRooted(policyPath) || config is null)
                throw new ArgumentException("An absolute machine-local --policy and explicit --config are required.");
            AppleReleaseAppState.AssertUnlinked(policyPath);
            var policy = JsonSerializer.Deserialize<AppleTestFlightWorkerPolicy>(File.ReadAllText(policyPath), AppleTestFlightWorker.Json)
                         ?? throw new InvalidOperationException("TestFlight worker policy is invalid.");
            if (policy.ConfigPath != config) throw new ArgumentException("Configuration must match the enrolled worker policy.");
            if (argv[0] == "retire" ? job is null || !reconciled : job is not null || reconciled)
                throw new ArgumentException("Only retire accepts --job <id> and requires --confirm-reconciled after inspecting canonical receipts and remote delivery.");
            var worker = new AppleTestFlightWorker(policy);
            if (argv[0] == "checkpoint-evidence")
            {
                WriteJson(new CliJsonEnvelope { SchemaVersion = OutputSchemaVersion, Command = "apple-testflight", Success = true,
                    ExitCode = 0, Result = JsonSerializer.SerializeToElement(worker.PublisherCheckpointEvidence()) });
                return 0;
            }
            if (argv[0] is "pause" or "resume") worker.SetPaused(argv[0] == "pause");
            var state = worker.Run(inspectOnly: argv[0] != "run", retireReconciledJob: argv[0] == "retire" ? job : null);
            WriteJson(new CliJsonEnvelope
            {
                SchemaVersion = OutputSchemaVersion, Command = "apple-testflight", Success = true, ExitCode = 0,
                Result = JsonSerializer.SerializeToElement(state, AppleTestFlightWorker.Json)
            });
            return 0;
        }
        catch (Exception exception) { return WriteReleaseError(outputJson, "apple-testflight", 2, exception.Message, logger); }
    }
}
