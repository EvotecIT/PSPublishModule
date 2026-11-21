using PowerForge;
using PowerForge.ConsoleUi;

internal static partial class Program
{
    public static int Main(string[] args)
    {
        var cli = ParseCliOptions(args, out var error);
        var command = StripGlobalArgs(args);
        if (error is not null || !HandlesNativeCommand(command))
            return ManagedWorker.Run(args);

        using var encoding = ConsoleEncoding.UseUtf8(switchEncoding: true);
        if (cli.NoColor)
            Environment.SetEnvironmentVariable("NO_COLOR", "1");

        ILogger logger = new TerminalOutputLogger { IsVerbose = cli.Verbose };
        if (cli.Quiet)
            logger = new QuietLogger(logger);

        return CommandModule(command, cli, logger);
    }

    private static bool HandlesNativeCommand(string[] args)
        => args.Length > 0 && string.Equals(args[0], "module", StringComparison.OrdinalIgnoreCase)
           && (args.Length == 1 || IsModuleAction(args[1].ToLowerInvariant()));

    // The native module commands share the terminal renderer and can stream progress messages directly.
    private static T RunWithStatus<T>(bool outputJson, CliOptions cli, string statusText, Func<T> action)
        => action();
}
