using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private const string AppleSimulatorUsage =
        "Usage: powerforge apple-simulator run --owner <chat-or-run> --device <UUID> " +
        "[--state-root <shared-path>] [--device-set <existing-path>] [--require-shutdown] [--timeout-seconds <seconds>] " +
        "[--simulator-timeout-seconds <seconds>] [--cleanup-timeout-seconds <seconds>] " +
        "[--duo-pose closed|open [--duo-orientation any|portrait|landscape] [--duo-fold-tool <duoctl>] [--duo-pose-timeout-seconds <seconds>]] " +
        "[--output json] -- <executable> [args...]\n" +
        "       powerforge apple-simulator status [--state-root <shared-path>] [--output json]\n" +
        "       powerforge apple-simulator acknowledge --session <session-id> [--state-root <shared-path>] [--output json]";

    private static int CommandAppleSimulator(string[] args, ILogger logger)
    {
        var separator = Array.IndexOf(args, "--");
        var options = separator < 0 ? args : args.Take(separator).ToArray();
        var json = IsJsonOutput(options);
        if (options.Length < 2 || options.Any(IsHelpArg)) { Console.WriteLine(AppleSimulatorUsage); return options.Length < 2 ? 2 : 0; }
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, value) => { value.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        using var terminate = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM,
            context => { context.Cancel = true; cancellation.Cancel(); });
        try
        {
            var operation = options[1].ToLowerInvariant();
            var values = ParseAppleSimulatorOptions(options, operation);
            values.TryGetValue("--state-root", out var stateRoot);
            var service = new AppleSimulatorSessionService();
            if (operation == "status")
            {
                if (separator >= 0) throw new ArgumentException("status does not accept a child command.");
                var status = service.InspectAsync(stateRoot, cancellation.Token).GetAwaiter().GetResult();
                if (json) WriteAppleSimulatorJson("status", !status.NeedsAttention, status.NeedsAttention ? 1 : 0,
                    CliJson.SerializeToElement(status, CliJson.Context.AppleSimulatorSessionStatus));
                else
                {
                    PowerForge.ConsoleUi.ConsoleOutput.WriteLine($"Owner: {status.OwnerState}; device: {status.DeviceState ?? "none"}; needs attention: {status.NeedsAttention}; receipt: {status.ReceiptPath}");
                    if (status.DiscoveryError is not null) logger.Error(status.DiscoveryError);
                }
                return status.NeedsAttention ? 1 : 0;
            }
            if (operation == "acknowledge")
            {
                if (separator >= 0) throw new ArgumentException("acknowledge does not accept a child command.");
                var receipt = service.AcknowledgeAsync(RequiredAppleSimulatorValue(values, "--session"), stateRoot, cancellation.Token).GetAwaiter().GetResult();
                if (json) WriteAppleSimulatorJson("acknowledge", true, 0, CliJson.SerializeToElement(receipt, CliJson.Context.AppleSimulatorSessionReceipt));
                else PowerForge.ConsoleUi.ConsoleOutput.WriteLine($"Acknowledged {receipt.SessionId}; verified device state: {receipt.FinalState}.");
                return 0;
            }
            if (separator < 0 || separator + 1 >= args.Length) throw new ArgumentException("run requires -- followed by a foreground executable and arguments.");
            var shutdownPolicy = Environment.GetEnvironmentVariable("POWERFORGE_APPLE_SIMULATOR_REQUIRE_SHUTDOWN");
            if (shutdownPolicy is not (null or "" or "1"))
                throw new ArgumentException("POWERFORGE_APPLE_SIMULATOR_REQUIRE_SHUTDOWN must be 1 or unset.");
            var request = new AppleSimulatorSessionRequest
            {
                DeviceId = RequiredAppleSimulatorValue(values, "--device"), Owner = RequiredAppleSimulatorValue(values, "--owner"),
                StateRoot = stateRoot, DeviceSetPath = values.TryGetValue("--device-set", out var deviceSet) ? deviceSet : null,
                RequireInitialShutdown = values.ContainsKey("--require-shutdown") || shutdownPolicy == "1",
                SimulatorTimeout = AppleSimulatorTimeout(values, "--simulator-timeout-seconds", 120),
                CleanupTimeout = AppleSimulatorTimeout(values, "--cleanup-timeout-seconds", 30),
                Command = new ProcessRunRequest(args[separator + 1], Directory.GetCurrentDirectory(), args.Skip(separator + 2).ToArray(),
                    AppleSimulatorTimeout(values, "--timeout-seconds", 1800)) { RequireDirectStart = true },
                DuoPose = AppleSimulatorDuoPose(values)
            };
            var result = service.RunAsync(request, cancellation.Token).GetAwaiter().GetResult();
            if (json) WriteAppleSimulatorJson("run", result.Success, result.ExitCode,
                CliJson.SerializeToElement(result, CliJson.Context.AppleSimulatorSessionResult));
            else
            {
                if (result.CommandResult is not null)
                {
                    Console.Write(result.CommandResult.StdOut);
                    Console.Error.Write(result.CommandResult.StdErr);
                }
                if (result.Error is not null) logger.Error(result.Error);
                if (result.CleanupError is not null) logger.Error(result.CleanupError);
                PowerForge.ConsoleUi.ConsoleOutput.WriteLine($"Simulator {result.Receipt.DeviceId}: {result.Receipt.InitialState} -> {result.Receipt.FinalState}; release verified: {result.Receipt.CleanupSucceeded}; receipt: {result.ReceiptPath}");
            }
            return result.ExitCode;
        }
        catch (OperationCanceledException) { return WriteReleaseError(json, "apple-simulator", 130, "Simulator session canceled.", logger); }
        catch (Exception exception) { return WriteReleaseError(json, "apple-simulator", exception is ArgumentException ? 2 : 1, exception.Message, logger); }
        finally { Console.CancelKeyPress -= cancel; }
    }

    private static Dictionary<string, string> ParseAppleSimulatorOptions(string[] options, string operation)
    {
        if (operation is not ("run" or "status" or "acknowledge")) throw new ArgumentException("Expected run, status, or acknowledge.");
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 2; index < options.Length; index++)
        {
            var option = options[index].ToLowerInvariant();
            if (option is "--json" or "--output-json") continue;
            if (option == "--require-shutdown" && operation == "run")
            {
                if (!values.TryAdd(option, "true")) throw new ArgumentException($"Duplicate option '{option}'.");
                continue;
            }
            var common = option is "--state-root" or "--output";
            var run = operation == "run" && option is "--device" or "--owner" or "--device-set" or "--timeout-seconds" or "--simulator-timeout-seconds" or "--cleanup-timeout-seconds"
                or "--duo-pose" or "--duo-orientation" or "--duo-fold-tool" or "--duo-pose-timeout-seconds";
            if (!common && !run && !(operation == "acknowledge" && option == "--session")) throw new ArgumentException($"Unknown {operation} option '{option}'.");
            if (values.ContainsKey(option)) throw new ArgumentException($"Duplicate option '{option}'.");
            if (++index >= options.Length || string.IsNullOrWhiteSpace(options[index]) || options[index].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Missing value for '{option}'.");
            values.Add(option, options[index]);
            if (option == "--output" && options[index] is not ("json" or "text")) throw new ArgumentException("--output requires json or text.");
        }
        return values;
    }

    private static AppleDuoPoseRequest? AppleSimulatorDuoPose(Dictionary<string, string> values)
    {
        if (!values.TryGetValue("--duo-pose", out var poseValue))
        {
            if (values.Keys.Any(key => key.StartsWith("--duo-", StringComparison.Ordinal)))
                throw new ArgumentException("--duo-orientation, --duo-fold-tool and --duo-pose-timeout-seconds require --duo-pose.");
            return null;
        }
        var pose = poseValue.ToLowerInvariant() switch
        {
            "closed" => AppleDuoPose.Closed,
            "open" => AppleDuoPose.Open,
            _ => throw new ArgumentException("--duo-pose requires closed or open.")
        };
        var orientation = !values.TryGetValue("--duo-orientation", out var orientationValue) ? AppleDuoOrientation.Any
            : orientationValue.ToLowerInvariant() switch
            {
                "any" => AppleDuoOrientation.Any,
                "portrait" => AppleDuoOrientation.Portrait,
                "landscape" => AppleDuoOrientation.Landscape,
                _ => throw new ArgumentException("--duo-orientation requires any, portrait or landscape.")
            };
        // The fold tool is opt-in: an explicit option, or POWERFORGE_DUO_FOLD_TOOL for scripts that enable it per machine.
        var tool = values.TryGetValue("--duo-fold-tool", out var toolValue) ? toolValue
            : Environment.GetEnvironmentVariable("POWERFORGE_DUO_FOLD_TOOL");
        return new AppleDuoPoseRequest
        {
            Pose = pose, Orientation = orientation,
            FoldToolPath = string.IsNullOrWhiteSpace(tool) ? null : Path.GetFullPath(tool!),
            PromptTimeout = AppleSimulatorTimeout(values, "--duo-pose-timeout-seconds", 300)
        };
    }

    private static string RequiredAppleSimulatorValue(Dictionary<string, string> values, string option)
        => values.TryGetValue(option, out var value) ? value : throw new ArgumentException($"Missing required option '{option}'.");

    private static TimeSpan AppleSimulatorTimeout(Dictionary<string, string> values, string option, int fallback)
    {
        if (!values.TryGetValue(option, out var value)) return TimeSpan.FromSeconds(fallback);
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0 || seconds > 86400)
            throw new ArgumentException($"{option} requires positive whole seconds no greater than 86400.");
        return TimeSpan.FromSeconds(seconds);
    }

    private static void WriteAppleSimulatorJson(string operation, bool success, int exitCode, System.Text.Json.JsonElement result)
        => WriteJson(new CliJsonEnvelope { SchemaVersion = OutputSchemaVersion, Command = "apple-simulator." + operation,
            Success = success, ExitCode = exitCode, Result = result }, writer =>
        {
            writer.WriteStartObject("tool");
            writer.WriteString("cliVersion", typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
            writer.WriteString("engineVersion", typeof(AppleSimulatorSessionService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
            writer.WriteString("cliModuleId", AppleSimulatorModuleId(typeof(Program).Assembly));
            writer.WriteString("engineModuleId", AppleSimulatorModuleId(typeof(AppleSimulatorSessionService).Assembly));
            writer.WriteEndObject();
        });

    private static string? AppleSimulatorModuleId(Assembly assembly)
    {
        try { return assembly.ManifestModule.ModuleVersionId.ToString("D"); }
        catch (NotSupportedException) { return null; } // NativeAOT may not expose module metadata; guarded captures require it.
    }
}
