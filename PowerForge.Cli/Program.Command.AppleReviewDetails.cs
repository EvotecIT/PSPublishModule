using System.Text.Json;
using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private const string AppleReviewDetailsUsage =
        "Usage: powerforge apple-review-details <validate|plan|apply> --config <review-details.json> " +
        "[--key-path <AuthKey.p8> --key-id <id> --issuer-id <id>] [--receipt <path>] " +
        "[--reviewed-plan <path> --confirm] [--output json]";

    private static int CommandAppleReviewDetails(string[] filteredArgs, CliOptions cli, ILogger logger)
    {
        var argv = filteredArgs.Skip(1).ToArray();
        var outputJson = IsJsonOutput(argv);
        if (argv.Length == 0 || argv.Any(value => value is "-h" or "--help"))
        {
            if (outputJson) WriteAppleReviewDetailsJson("help", true, 0, JsonSerializer.SerializeToElement(new { usage = AppleReviewDetailsUsage }), null);
            else Console.WriteLine(AppleReviewDetailsUsage);
            return argv.Length == 0 ? 2 : 0;
        }

        var operation = argv[0].Trim().ToLowerInvariant();
        try
        {
            ValidateAppleReviewDetailsArguments(argv.Skip(1).ToArray());
            if (operation is not ("validate" or "plan" or "apply"))
                throw new ArgumentException($"Unknown apple-review-details operation '{operation}'.");

            var configPath = TryGetOptionValue(argv, "--config") ?? throw new ArgumentException("--config is required.");
            var fullConfigPath = ResolveExistingFilePath(configPath);
            var spec = CliJson.DeserializeOrThrow(
                File.ReadAllText(fullConfigPath),
                CliJson.Context.AppStoreConnectReviewDetailsCopySpec,
                fullConfigPath);
            if (operation == "validate")
            {
                ValidateReviewDetailsSpec(spec);
                WriteAppleReviewDetailsOutput(operation, new
                {
                    valid = true,
                    source = new { spec.Source.AppId, spec.Source.VersionString, spec.Source.Platform },
                    target = new { spec.Target.AppId, spec.Target.VersionString, spec.Target.Platform }
                }, true, outputJson, logger);
                return 0;
            }

            var credential = ResolveAppleGovernanceCredential(argv, fullConfigPath);
            using var client = new AppStoreConnectClient(credential);
            var service = new AppStoreConnectReviewDetailsCopyService(client);
            if (operation == "plan")
            {
                var plan = service.PlanAsync(spec).GetAwaiter().GetResult();
                var receipt = ResolveAppleReviewDetailsReceiptPath(argv, fullConfigPath, "review-details-plan.json");
                WriteJsonFile(receipt, plan, CliJson.Context.AppStoreConnectReviewDetailsCopyPlan);
                WriteAppleReviewDetailsOutput(operation, plan, true, outputJson, logger, receipt);
                return 0;
            }

            if (!argv.Any(value => value.Equals("--confirm", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("apple-review-details apply requires --confirm after reviewing a plan receipt.");
            var reviewedPath = TryGetOptionValue(argv, "--reviewed-plan")
                ?? throw new InvalidOperationException("apple-review-details apply requires --reviewed-plan.");
            var fullReviewedPath = ResolveExistingFilePath(reviewedPath);
            var reviewed = CliJson.DeserializeOrThrow(
                File.ReadAllText(fullReviewedPath),
                CliJson.Context.AppStoreConnectReviewDetailsCopyPlan,
                fullReviewedPath);
            var result = service.ApplyAsync(spec, reviewed, confirmApply: true).GetAwaiter().GetResult();
            var applyReceipt = ResolveAppleReviewDetailsReceiptPath(argv, fullConfigPath, "review-details-receipt.json");
            WriteJsonFile(applyReceipt, result, CliJson.Context.AppStoreConnectReviewDetailsCopyResult);
            WriteAppleReviewDetailsOutput(operation, result, result.Success, outputJson, logger, applyReceipt);
            return result.Success ? 0 : 1;
        }
        catch (Exception ex)
        {
            if (outputJson) WriteAppleReviewDetailsJson(operation, false, 2, null, ex.Message);
            else logger.Error(ex.Message);
            return 2;
        }
    }

    private static void ValidateReviewDetailsSpec(AppStoreConnectReviewDetailsCopySpec spec)
    {
        if (spec.SchemaVersion != 1)
            throw new InvalidOperationException($"Unsupported App Review details schemaVersion '{spec.SchemaVersion}'.");
        foreach (var (name, value) in new[]
                 {
                     ("source.appId", spec.Source?.AppId),
                     ("source.versionString", spec.Source?.VersionString),
                     ("target.appId", spec.Target?.AppId),
                     ("target.versionString", spec.Target?.VersionString)
                 })
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException($"App Review details {name} is required.");
        }
    }

    private static string ResolveAppleReviewDetailsReceiptPath(string[] argv, string configPath, string fileName)
    {
        var configured = TryGetOptionValue(argv, "--receipt");
        var baseDirectory = Path.GetDirectoryName(configPath) ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(baseDirectory, ".powerforge", "apple", fileName)
            : Path.IsPathRooted(configured) ? configured : Path.Combine(baseDirectory, configured));
    }

    private static void WriteJsonFile<T>(
        string path,
        T value,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporary = path + ".tmp." + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, typeInfo));
        File.Move(temporary, path, overwrite: true);
    }

    private static void WriteAppleReviewDetailsOutput(
        string operation,
        object result,
        bool success,
        bool outputJson,
        ILogger logger,
        string? receiptPath = null)
    {
        if (outputJson)
        {
            var element = result switch
            {
                AppStoreConnectReviewDetailsCopyPlan plan => CliJson.SerializeToElement(plan, CliJson.Context.AppStoreConnectReviewDetailsCopyPlan),
                AppStoreConnectReviewDetailsCopyResult apply => CliJson.SerializeToElement(apply, CliJson.Context.AppStoreConnectReviewDetailsCopyResult),
                _ => JsonSerializer.SerializeToElement(result)
            };
            WriteAppleReviewDetailsJson(operation, success, success ? 0 : 1, element, success ? null : "App Review details did not converge.");
            return;
        }

        if (result is AppStoreConnectReviewDetailsCopyPlan planResult)
            logger.Info($"App Review details plan: target={planResult.AppId}/{planResult.Platform}/{planResult.VersionString}; converged={planResult.IsConverged}; versionExists={planResult.TargetVersionExists}; detailsExist={planResult.TargetExists}; demoAccountRequired={planResult.DemoAccountRequired}.");
        else if (result is AppStoreConnectReviewDetailsCopyResult applyResult)
            logger.Info($"App Review details apply: converged={applyResult.Success}; createdVersion={applyResult.CreatedVersion}; createdDetails={applyResult.Created}; updatedDetails={applyResult.Updated}.");
        else
            logger.Info("App Review details config is valid.");
        if (!string.IsNullOrWhiteSpace(receiptPath)) logger.Info("Receipt: " + receiptPath);
    }

    private static void WriteAppleReviewDetailsJson(string operation, bool success, int exitCode, JsonElement? result, string? error)
        => WriteJson(new CliJsonEnvelope
        {
            SchemaVersion = OutputSchemaVersion,
            Command = "apple-review-details " + operation,
            Success = success,
            ExitCode = exitCode,
            Result = result,
            Error = error
        });

    private static void ValidateAppleReviewDetailsArguments(string[] argv)
    {
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "--confirm" };
        var options = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "--config", "--key-path", "--key-id", "--issuer-id", "--receipt", "--reviewed-plan", "--output"
        };
        for (var index = 0; index < argv.Length; index++)
        {
            var value = argv[index];
            if (flags.Contains(value)) continue;
            if (!options.Contains(value)) throw new ArgumentException($"Unknown apple-review-details option '{value}'.");
            if (++index >= argv.Length || argv[index].StartsWith("-", StringComparison.Ordinal))
                throw new ArgumentException($"Missing value for apple-review-details option '{value}'.");
        }
    }
}
