using PowerForge;
using PowerForge.Cli;
using System.Text.Json;

internal static partial class Program
{
    private const int OutputSchemaVersion = 1;

    static CliOptions ParseCliOptions(string[]? args, out string? error)
    {
        error = null;
        if (args is null) return new CliOptions(verbose: false, quiet: false, diagnostics: false, noColor: false, view: ConsoleView.Auto);

        bool verbose = false;
        bool quiet = false;
        bool diagnostics = false;
        bool noColor = false;
        ConsoleView view = ConsoleView.Auto;
        bool viewExplicit = false;

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a == "--") break;
            if (a.Equals("-Verbose", StringComparison.OrdinalIgnoreCase) || a.Equals("--verbose", StringComparison.OrdinalIgnoreCase))
            {
                verbose = true;
                continue;
            }

            if (a.Equals("--quiet", StringComparison.OrdinalIgnoreCase) || a.Equals("-q", StringComparison.OrdinalIgnoreCase))
            {
                quiet = true;
                continue;
            }

            if (a.Equals("--diagnostics", StringComparison.OrdinalIgnoreCase) || a.Equals("--diag", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics = true;
                continue;
            }

            if (a.Equals("--no-color", StringComparison.OrdinalIgnoreCase) || a.Equals("--nocolor", StringComparison.OrdinalIgnoreCase))
            {
                noColor = true;
                continue;
            }

            if (a.Equals("--view", StringComparison.OrdinalIgnoreCase))
            {
                viewExplicit = true;
                if (++i >= args.Length)
                {
                    error = "Missing value for --view. Expected: auto|standard|ansi.";
                    return new CliOptions(verbose, quiet, diagnostics, noColor, view);
                }

                if (!TryParseConsoleView(args[i], out view))
                {
                    error = $"Invalid value for --view: '{args[i]}'. Expected: auto|standard|ansi.";
                    return new CliOptions(verbose, quiet, diagnostics, noColor, view);
                }

                continue;
            }
        }

        if (!viewExplicit)
        {
            var envView = Environment.GetEnvironmentVariable("POWERFORGE_VIEW");
            if (!string.IsNullOrWhiteSpace(envView) && TryParseConsoleView(envView, out var parsed))
            {
                view = parsed;
            }
        }

        return new CliOptions(verbose, quiet, diagnostics, noColor, view);
    }

    static string[] StripGlobalArgs(string[] args)
    {
        if (args is null || args.Length == 0) return Array.Empty<string>();

        var list = new List<string>(args.Length);
        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a == "--")
            {
                list.AddRange(args.Skip(i));
                break;
            }
            if (IsGlobalArg(a)) continue;

            if (a.Equals("--view", StringComparison.OrdinalIgnoreCase))
            {
                i++; // skip value
                continue;
            }

            list.Add(a);
        }
        return list.ToArray();
    }

    static bool IsVersionInvocation(string[] args)
    {
        if (args is null || args.Length == 0) return false;

        var remaining = new List<string>(args.Length);
        for (var i = 0; i < args.Length; i++)
        {
            var argument = args[i];
            if (IsGlobalArg(argument) ||
                argument.Equals("--output-json", StringComparison.OrdinalIgnoreCase) ||
                argument.Equals("--json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (argument.Equals("--view", StringComparison.OrdinalIgnoreCase) ||
                argument.Equals("--output", StringComparison.OrdinalIgnoreCase))
            {
                if (++i < args.Length)
                    continue;

                return false;
            }

            remaining.Add(argument);
        }

        return remaining.Count == 1 &&
               remaining[0].Equals("--version", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsGlobalArg(string arg)
    {
        if (string.IsNullOrWhiteSpace(arg)) return false;

        return arg.Equals("-Verbose", StringComparison.OrdinalIgnoreCase) ||
               arg.Equals("--verbose", StringComparison.OrdinalIgnoreCase) ||
               arg.Equals("--quiet", StringComparison.OrdinalIgnoreCase) ||
               arg.Equals("-q", StringComparison.OrdinalIgnoreCase) ||
               arg.Equals("--diagnostics", StringComparison.OrdinalIgnoreCase) ||
               arg.Equals("--diag", StringComparison.OrdinalIgnoreCase) ||
               arg.Equals("--no-color", StringComparison.OrdinalIgnoreCase) ||
               arg.Equals("--nocolor", StringComparison.OrdinalIgnoreCase);
    }

    static string? TryGetOptionValue(string[] argv, string optionName)
    {
        for (int i = 0; i < argv.Length; i++)
        {
            if (!argv[i].Equals(optionName, StringComparison.OrdinalIgnoreCase)) continue;
            return ++i < argv.Length ? argv[i] : null;
        }
        return null;
    }

    static bool TryParseConsoleView(string? value, out ConsoleView view)
    {
        view = ConsoleView.Auto;
        if (string.IsNullOrWhiteSpace(value)) return false;

        switch (value.Trim().ToLowerInvariant())
        {
            case "auto":
                view = ConsoleView.Auto;
                return true;
            case "standard":
            case "interactive":
                view = ConsoleView.Standard;
                return true;
            case "ansi":
            case "plain":
                view = ConsoleView.Ansi;
                return true;
            default:
                return false;
        }
    }

    static bool IsJsonOutput(string[] argv)
    {
        argv = argv.TakeWhile(static argument => argument != "--").ToArray();
        foreach (var a in argv)
        {
            if (a.Equals("--output-json", StringComparison.OrdinalIgnoreCase) || a.Equals("--json", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var output = TryGetOptionValue(argv, "--output");
        return string.Equals(output, "json", StringComparison.OrdinalIgnoreCase);
    }

    static (ILogger Logger, BufferedLogger? Buffer) CreateCommandLogger(bool outputJson, CliOptions cli, ILogger textLogger)
    {
        if (!outputJson) return (textLogger, null);

        if (cli.Diagnostics)
        {
            var buffer = new BufferedLogger { IsVerbose = cli.Verbose };
            return (buffer, buffer);
        }

        return (new NullLogger { IsVerbose = cli.Verbose }, null);
    }

    static JsonElement? LogsToJsonElement(BufferedLogger? logBuffer)
    {
        if (logBuffer is null) return null;
        if (logBuffer.Entries.Count == 0) return null;
        return JsonSerializer.SerializeToElement(logBuffer.Entries.ToArray(), ModuleCliJson.Default.BufferedLogEntryArray);
    }

    static void WriteJson(
        CliJsonEnvelope envelope,
        Action<Utf8JsonWriter>? writeAdditionalProperties = null,
        Func<string, string>? redactOutput = null)
    {
        CliJsonWriter.Write(envelope, writeAdditionalProperties, redactOutput);
    }
}

sealed class CliOptions
{
    public bool Verbose { get; }
    public bool Quiet { get; }
    public bool Diagnostics { get; }
    public bool NoColor { get; }
    public ConsoleView View { get; }

    public CliOptions(bool verbose, bool quiet, bool diagnostics, bool noColor, ConsoleView view)
    {
        Verbose = verbose;
        Quiet = quiet;
        Diagnostics = diagnostics;
        NoColor = noColor;
        View = view;
    }
}

sealed class QuietLogger : ILogger
{
    private readonly ILogger _inner;

    public QuietLogger(ILogger inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public bool IsVerbose => _inner.IsVerbose;

    public void Info(string message) { }
    public void Success(string message) { }
    public void Warn(string message) => _inner.Warn(message);
    public void Error(string message) => _inner.Error(message);
    public void Verbose(string message) { }
}
