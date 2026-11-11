using PowerForge;
using PowerForge.Cli;
using System.Text.Json;

internal static partial class Program
{
    private static int CommandSigning(string[] args, ILogger logger)
    {
        const string usage = "Usage: powerforge signing prepare --thumbprint <approved-certificate> --out <new-local-directory> [--timestamp-url <url>] [--module-path <installed-manifest>] [--output json]";
        var outputJson = IsJsonOutput(args);
        if (args.Length < 2 || args.Any(argument => argument is "--help" or "-h"))
        {
            if (outputJson) WriteJson(new CliJsonEnvelope {
                SchemaVersion = OutputSchemaVersion, Command = "signing prepare", Success = true, ExitCode = 0,
                Result = JsonSerializer.SerializeToElement(new { usage })
            });
            else Console.WriteLine(usage);
            return 0;
        }
        try
        {
            if (!args[1].Equals("prepare", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(usage);
            var request = new SigningPreparationRequest();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 2; index < args.Length; index++)
            {
                var option = args[index].ToLowerInvariant();
                if (option is "--json" or "--output-json") continue;
                if (option is not ("--thumbprint" or "--out" or "--timestamp-url" or "--module-path" or "--output"))
                    throw new ArgumentException($"Unknown signing preparation option '{option}'.");
                if (!seen.Add(option)) throw new ArgumentException($"Duplicate signing preparation option '{option}'.");
                if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]) || args[index].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"Missing value for '{option}'.");
                switch (option)
                {
                    case "--thumbprint": request.Thumbprint = args[index]; break;
                    case "--out": request.OutputDirectory = args[index]; break;
                    case "--timestamp-url": request.TimestampUrl = args[index]; break;
                    case "--module-path": request.ModulePath = args[index]; break;
                    case "--output":
                        if (!args[index].Equals("json", StringComparison.OrdinalIgnoreCase) &&
                            !args[index].Equals("text", StringComparison.OrdinalIgnoreCase))
                            throw new ArgumentException("--output requires json or text.");
                        break;
                }
            }
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Signing preparation requires the configured interactive Windows signing session.");
            const string notice = "Signing one generated local proof. Enter any PIN only in the provider's normal dialog. No release is published.";
            if (outputJson) Console.Error.WriteLine(notice);
            else logger.Info(notice);
            var result = new SigningPreparationService().Prepare(request);
            if (outputJson) WriteJson(new CliJsonEnvelope {
                SchemaVersion = OutputSchemaVersion, Command = "signing prepare", Success = true, ExitCode = 0,
                Result = JsonSerializer.SerializeToElement(result)
            });
            else
            {
                logger.Success($"Signature verified at {result.VerifiedAtUtc:O}; certificate {result.Thumbprint}.");
                logger.Info($"Proof: {result.ArtifactPath}");
                logger.Info("Later signing remains provider-managed; cache scope and expiry are unknown. Locking, token removal or provider policy may require another prompt.");
            }
            return 0;
        }
        catch (Exception error)
        {
            return WriteReleaseError(outputJson, "signing prepare", error is ArgumentException ? 2 : 1, error.Message, logger);
        }
    }
}
