using PowerForge;
using PowerForge.ConsoleUi;

internal static partial class Program
{
    public static int Main(string[] args)
    {
        var cli = ParseCliOptions(args, out var error);
        var command = StripGlobalArgs(args);
        var isVersion = IsVersionInvocation(args);
        if (error is not null || (!isVersion && !HandlesNativeCommand(command)))
            return ManagedWorker.Run(args);

        using var encoding = ConsoleEncoding.UseUtf8(switchEncoding: true);
        if (cli.NoColor)
            Environment.SetEnvironmentVariable("NO_COLOR", "1");

        // The module engine and managed CLI share a release version and source revision.
        if (isVersion)
            return CommandVersion(args, typeof(ModuleManifestMetadataReader).Assembly);

        ILogger logger = new TerminalOutputLogger { IsVerbose = cli.Verbose };
        if (cli.Quiet)
            logger = new QuietLogger(logger);

        if (IsNuGetVerifyInvocation(command))
            return CommandNuGetVerify(command.Skip(2).ToArray(), logger);
        return IsProjectVersionsInvocation(command)
            ? CommandProjectVersions(command.Skip(2).ToArray(), cli, logger)
            : CommandModule(command, cli, logger);
    }

    private static bool HandlesNativeCommand(string[] args)
        => IsProjectVersionsInvocation(args) || IsNuGetVerifyInvocation(args)
           || (args.Length > 0 && string.Equals(args[0], "module", StringComparison.OrdinalIgnoreCase)
           && (args.Length == 1 || IsModuleAction(args[1].ToLowerInvariant())));

    // The native module commands share the terminal renderer and can stream progress messages directly.
    private static T RunWithStatus<T>(bool outputJson, CliOptions cli, string statusText, Func<T> action)
        => action();
}
