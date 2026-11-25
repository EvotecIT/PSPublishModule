using System.Globalization;
using System.Text.Json;
using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private const string BenchmarkCompareUsage =
        "Usage: powerforge benchmark compare --project <Benchmarks.csproj> --filter <BenchmarkDotNet filter> --framework <tfm> " +
        "[--baseline <revision>] [--candidate <revision>] [--repo <path>] [--rounds <n>] [--configuration <Release>] " +
        "[--scratch <folder>] [--keep-artifacts] [--lock <folder>] [--lock-timeout <minutes>] [--max-load <n>] [--load-timeout <minutes>] " +
        "[--run-timeout <minutes>] [--out <result.json>] [--output json] [-- <BenchmarkDotNet arguments>]";

    private static readonly string[] BenchmarkCompareValueOptions =
    {
        "--project", "--filter", "--framework", "--baseline", "--candidate", "--repo", "--rounds", "--configuration",
        "--scratch", "--lock", "--lock-timeout", "--max-load", "--load-timeout", "--run-timeout", "--out", "--output"
    };

    private static int CommandBenchmark(string[] filteredArgs, CliOptions cli, ILogger logger)
    {
        var argv = filteredArgs.Skip(1).ToArray();
        string? action = argv.FirstOrDefault();
        if (action is null or "-h" or "--help" || !string.Equals(action, "compare", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(BenchmarkCompareUsage);
            return action is null or "-h" or "--help" ? 0 : 2;
        }

        var arguments = argv.Skip(1).ToArray();
        int separator = Array.IndexOf(arguments, "--");
        var options = separator < 0 ? arguments : arguments.Take(separator).ToArray();
        var passthrough = separator < 0 ? Array.Empty<string>() : arguments.Skip(separator + 1).ToArray();
        bool json = IsJsonOutput(options);
        if (options.Any(value => value is "-h" or "--help"))
        {
            Console.WriteLine(BenchmarkCompareUsage);
            return 0;
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            var spec = ParseBenchmarkCompareSpec(options, passthrough);
            var commandLogger = json ? new NullLogger { IsVerbose = false } : logger;
            var result = new BenchmarkRevisionComparisonService(commandLogger).RunAsync(spec, cancellation.Token).GetAwaiter().GetResult();

            string? outPath = TryGetOptionValue(options, "--out");
            if (!string.IsNullOrWhiteSpace(outPath))
            {
                string full = Path.GetFullPath(outPath);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, JsonSerializer.Serialize(result, CliJson.Context.BenchmarkRevisionComparisonResult));
            }

            if (json)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "benchmark.compare",
                    Success = true,
                    ExitCode = 0,
                    Result = CliJson.SerializeToElement(result, CliJson.Context.BenchmarkRevisionComparisonResult)
                });
            }
            else
            {
                Console.WriteLine();
                Console.Write(BenchmarkRevisionComparison.RenderText(result));
                if (result.ArtifactsPath is not null) logger.Info("Artifacts kept in " + result.ArtifactsPath);
            }
            return 0;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return 130;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or IOException or TimeoutException or FormatException)
        {
            if (json)
                WriteJson(new CliJsonEnvelope { SchemaVersion = OutputSchemaVersion, Command = "benchmark.compare", Success = false, ExitCode = 2, Error = exception.Message });
            else
                logger.Error(exception.Message);
            return exception is ArgumentException or FormatException ? 2 : 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
    }

    internal static BenchmarkRevisionComparisonSpec ParseBenchmarkCompareSpec(string[] options, string[] passthrough)
    {
        for (int index = 0; index < options.Length; index++)
        {
            string option = options[index];
            if (option == "--keep-artifacts" || option is "--json" or "--output-json") continue;
            if (!BenchmarkCompareValueOptions.Contains(option, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException($"Unknown option {option}. {BenchmarkCompareUsage}");
            if (index + 1 >= options.Length || options[index + 1].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException($"{option} requires a value.");
            index++;
        }

        string Required(string name)
            => TryGetOptionValue(options, name) ?? throw new ArgumentException($"Missing {name}. {BenchmarkCompareUsage}");
        double? Number(string name)
            => TryGetOptionValue(options, name) is string text
                ? double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value > 0
                    ? value
                    : throw new ArgumentException($"{name} must be a positive number.")
                : null;
        TimeSpan Minutes(string name, TimeSpan fallback)
            => Number(name) is double minutes ? TimeSpan.FromMinutes(minutes) : fallback;

        var spec = new BenchmarkRevisionComparisonSpec
        {
            RepositoryPath = TryGetOptionValue(options, "--repo") ?? Directory.GetCurrentDirectory(),
            Project = Required("--project"),
            Filter = Required("--filter"),
            Framework = Required("--framework"),
            Baseline = TryGetOptionValue(options, "--baseline"),
            Candidate = TryGetOptionValue(options, "--candidate"),
            Configuration = TryGetOptionValue(options, "--configuration") ?? "Release",
            ScratchRoot = TryGetOptionValue(options, "--scratch"),
            KeepArtifacts = options.Contains("--keep-artifacts", StringComparer.OrdinalIgnoreCase),
            LockPath = TryGetOptionValue(options, "--lock"),
            MaxLoadAverage = Number("--max-load"),
            BenchmarkArguments = passthrough
        };
        spec.LockTimeout = Minutes("--lock-timeout", spec.LockTimeout);
        spec.LoadWaitTimeout = Minutes("--load-timeout", spec.LoadWaitTimeout);
        spec.RunTimeout = Minutes("--run-timeout", spec.RunTimeout);
        if (TryGetOptionValue(options, "--rounds") is string rounds)
        {
            if (!int.TryParse(rounds, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count) || count < 1)
                throw new ArgumentException("--rounds must be a positive whole number.");
            spec.Rounds = count;
        }
        return spec;
    }
}
