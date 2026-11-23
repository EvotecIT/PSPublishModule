using PowerForge;

internal static partial class Program
{
    private const string NuGetVerifyUsage =
        "Usage: powerforge nuget verify --manifest <path> --source-commit <sha> --author-fingerprints <sha256[,sha256]>";

    private static bool IsNuGetVerifyInvocation(string[] args)
        => args.Length >= 2 && args[0].Equals("nuget", StringComparison.OrdinalIgnoreCase)
            && args[1] == "verify";

    private static int CommandNuGetVerify(string[] args, ILogger logger)
    {
        if (args.Any(argument => argument is "--help" or "-h"))
        {
            Console.WriteLine(NuGetVerifyUsage);
            return 0;
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, args) =>
        {
            args.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancel;
        try
        {
            var manifest = TryGetOptionValue(args, "--manifest") ?? throw new ArgumentException("--manifest is required.");
            var commit = TryGetOptionValue(args, "--source-commit") ?? throw new ArgumentException("--source-commit is required.");
            var fingerprints = (TryGetOptionValue(args, "--author-fingerprints") ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            NuGetReleaseVerifier.VerifyAsync(manifest, commit, fingerprints, new ProcessRunner(), "dotnet",
                TimeSpan.FromMinutes(10), cancellation.Token).GetAwaiter().GetResult();
            logger.Success("Signed NuGet release assets verified.");
            return 0;
        }
        catch (OperationCanceledException)
        {
            logger.Error("NuGet operation canceled.");
            return 130;
        }
        catch (Exception exception)
        {
            logger.Error(exception.Message);
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
    }
}
