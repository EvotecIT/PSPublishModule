using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private static int CommandDotNetTest(string[] arguments, ILogger logger)
    {
        var argv = arguments.Skip(1).ToArray();
        var config = TryGetOptionValue(argv, "--config");
        if (string.IsNullOrWhiteSpace(config))
        {
            logger.Error("Usage: powerforge test-dotnet --config <tests.json> [--profile test] [--framework net8.0] [--runtime linux-x64] [--results-path TestResults]");
            return 2;
        }
        foreach (var option in new[] { "--config", "--profile", "--framework", "--runtime", "--results-path" })
        {
            var index = Array.IndexOf(argv, option);
            if (index >= 0 && (index + 1 == argv.Length || argv[index + 1].StartsWith("--", StringComparison.Ordinal)))
            {
                logger.Error($"{option} requires a value.");
                return 2;
            }
        }
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            var path = Path.GetFullPath(config);
            var spec = CliJson.DeserializeOrThrow(File.ReadAllText(path), CliJson.Context.DotNetTestSuiteSpec, path);
            var result = new DotNetTestSuiteService().RunAsync(spec, path,
                TryGetOptionValue(argv, "--profile") ?? "test",
                TryGetOptionValue(argv, "--framework"), TryGetOptionValue(argv, "--runtime"),
                TryGetOptionValue(argv, "--results-path") ?? "TestResults", cancellation.Token).GetAwaiter().GetResult();
            if (!result.Succeeded) logger.Error(result.ErrorMessage ?? ".NET tests failed.");
            return cancellation.IsCancellationRequested ? 130 : result.Succeeded ? 0 : 1;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { return 130; }
        catch (Exception exception)
        {
            logger.Error(exception.Message);
            return 1;
        }
        finally { Console.CancelKeyPress -= cancel; }
    }
}
