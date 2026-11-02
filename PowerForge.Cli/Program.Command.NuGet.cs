using PowerForge;

internal static partial class Program
{
    private static int CommandNuGet(string[] args, CliOptions cli, ILogger logger)
    {
        const string usage = "Usage: powerforge nuget stage --config <project.build.json> | powerforge nuget <verify|publish> --manifest <path> --source-commit <sha> --author-fingerprints <sha256[,sha256]> [--api-key-env NUGET_API_KEY]";
        if (args.Length < 2 || args.Any(a => a is "--help" or "-h"))
        {
            Console.WriteLine(usage);
            return args.Length < 2 ? 2 : 0;
        }
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, value) => { value.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        try
        {
            if (args[1] == "stage")
            {
                var config = TryGetOptionValue(args, "--config") ?? throw new ArgumentException("--config is required.");
                var result = new ProjectBuildHostService(logger, DotNetAssemblySigningCallbackFactory.Create(logger),
                    DotNetAssemblySigningCallbackFactory.CreatePreflight(logger)).Execute(new ProjectBuildHostRequest
                {
                    ConfigPath = config, ExecuteBuild = true, StageNuget = true, CancellationToken = cancellation.Token
                });
                if (!result.Success) throw new InvalidOperationException(result.ErrorMessage);
                return 0;
            }
            if (args[1] is not ("verify" or "publish")) throw new ArgumentException(usage);
            var manifest = TryGetOptionValue(args, "--manifest") ?? throw new ArgumentException("--manifest is required.");
            var commit = TryGetOptionValue(args, "--source-commit") ?? throw new ArgumentException("--source-commit is required.");
            var fingerprints = (TryGetOptionValue(args, "--author-fingerprints") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var service = new NuGetTrustedPublishingService(logger);
            if (args[1] == "verify") service.VerifyAsync(manifest, commit, fingerprints, cancellation.Token).GetAwaiter().GetResult();
            else service.PublishAsync(manifest, commit, fingerprints, TryGetOptionValue(args, "--api-key-env") ?? "NUGET_API_KEY", cancellation.Token).GetAwaiter().GetResult();
            logger.Success("Signed NuGet release assets verified.");
            return 0;
        }
        catch (OperationCanceledException) { logger.Error("NuGet operation canceled."); return 130; }
        catch (Exception ex)
        {
            logger.Error(ex.Message);
            return 1;
        }
        finally { Console.CancelKeyPress -= handler; }
    }
}
