using System.Text.Json;
using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private const string AppleGovernanceUsage =
        "Usage: powerforge apple-governance <snapshot|validate|plan|apply> [--config <governance.json>] " +
        "[--app-id <id> --out <governance.json> [--force]] " +
        "[--release-config <release.json>] [--key-path <AuthKey.p8> --key-id <id> --issuer-id <id>] " +
        "[--receipt <path>] [--reviewed-plan <path>] [--confirm] [--max-changes <N>] [--fail-on-drift] [--summary] [--output json]";

    private static int CommandAppleGovernance(string[] filteredArgs, CliOptions cli, ILogger logger)
    {
        var argv = filteredArgs.Skip(1).ToArray();
        var outputJson = IsJsonOutput(argv);
        var summary = argv.Any(value => value.Equals("--summary", StringComparison.OrdinalIgnoreCase));
        if (argv.Length == 0 || argv.Any(value => value is "-h" or "--help"))
        {
            if (outputJson) WriteAppleGovernanceJson("help", true, 0, JsonSerializer.SerializeToElement(new { usage = AppleGovernanceUsage }), null);
            else Console.WriteLine(AppleGovernanceUsage);
            return argv.Length == 0 ? 2 : 0;
        }

        var operation = argv[0].Trim().ToLowerInvariant();
        try
        {
            ValidateAppleGovernanceArguments(argv.Skip(1).ToArray());
            if (operation == "snapshot")
            {
                var appId = TryGetOptionValue(argv, "--app-id") ?? throw new ArgumentException("snapshot requires --app-id.");
                var outputPath = TryGetOptionValue(argv, "--out") ?? throw new ArgumentException("snapshot requires --out.");
                var fullOutputPath = Path.GetFullPath(outputPath);
                if (File.Exists(fullOutputPath) && !argv.Any(value => value.Equals("--force", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException($"Snapshot output already exists: {fullOutputPath}. Use --force to replace it after preserving any reviewed edits.");
                var snapshotCredential = ResolveAppleGovernanceCredential(argv, Path.Combine(Directory.GetCurrentDirectory(), "governance.json"));
                using var snapshotClient = new AppStoreConnectClient(snapshotCredential);
                var snapshot = new AppStoreConnectGovernanceService(snapshotClient).SnapshotAsync(appId).GetAwaiter().GetResult();
                var snapshotOptions = new JsonSerializerOptions(CliJson.Options) { WriteIndented = true };
                WriteGovernanceReceipt(fullOutputPath, JsonSerializer.Serialize(snapshot, snapshotOptions));
                WriteAppleGovernanceOutput(operation, snapshot, true, outputJson, logger, fullOutputPath, summary);
                return 0;
            }
            var configPath = TryGetOptionValue(argv, "--config") ?? throw new ArgumentException("--config is required.");
            var fullConfigPath = ResolveExistingFilePath(configPath);
            var configuration = new AppStoreConnectGovernanceConfiguration();
            var spec = configuration.Load(fullConfigPath);

            if (operation == "validate")
            {
                var findings = configuration.Validate(spec);
                var success = findings.All(finding => !finding.IsError);
                var validation = new AppStoreConnectGovernancePlan
                {
                    AppId = spec.AppId,
                    CheckedAtUtc = DateTimeOffset.UtcNow,
                    Findings = findings
                };
                WriteAppleGovernanceOutput(operation, validation, success, outputJson, logger, summary: summary);
                return success ? 0 : 2;
            }
            if (operation is not ("plan" or "apply"))
                throw new ArgumentException($"Unknown apple-governance operation '{operation}'.");

            if (operation == "apply" && !argv.Any(value => value.Equals("--confirm", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("apple-governance apply requires --confirm after reviewing a plan receipt.");

            AppStoreConnectGovernancePlan? reviewedPlan = null;
            if (operation == "apply")
            {
                var reviewedPlanPath = TryGetOptionValue(argv, "--reviewed-plan")
                    ?? throw new InvalidOperationException("apple-governance apply requires --reviewed-plan pointing to the approved plan receipt.");
                var fullReviewedPlanPath = ResolveExistingFilePath(reviewedPlanPath);
                reviewedPlan = JsonSerializer.Deserialize(
                    File.ReadAllText(fullReviewedPlanPath),
                    CliJson.Context.AppStoreConnectGovernancePlan)
                    ?? throw new InvalidOperationException("The reviewed governance plan receipt is empty or invalid.");
            }

            var credential = ResolveAppleGovernanceCredential(argv, fullConfigPath);
            using var client = new AppStoreConnectClient(credential);
            var service = new AppStoreConnectGovernanceService(client);
            if (operation == "plan")
            {
                var plan = service.PlanAsync(spec).GetAwaiter().GetResult();
                var receiptPath = ResolveGovernanceReceiptPath(argv, fullConfigPath, "governance-plan.json");
                WriteGovernanceReceipt(receiptPath, JsonSerializer.Serialize(plan, CliJson.Context.AppStoreConnectGovernancePlan));
                WriteAppleGovernanceOutput(operation, plan, plan.Findings.All(finding => !finding.IsError), outputJson, logger, receiptPath, summary);
                if (plan.Findings.Any(finding => finding.IsError)) return 2;
                return argv.Any(value => value.Equals("--fail-on-drift", StringComparison.OrdinalIgnoreCase)) && !plan.IsConverged ? 3 : 0;
            }

            var maximumChanges = ParseAppleGovernanceMaximumChanges(TryGetOptionValue(argv, "--max-changes"));
            var result = service.ApplyAsync(new AppStoreConnectGovernanceApplyRequest
            {
                Spec = spec,
                ConfirmApply = true,
                MaximumChanges = maximumChanges,
                ReviewedPlan = reviewedPlan
            }).GetAwaiter().GetResult();
            var applyReceiptPath = ResolveGovernanceReceiptPath(argv, fullConfigPath, "governance-receipt.json");
            WriteGovernanceReceipt(applyReceiptPath, JsonSerializer.Serialize(result, CliJson.Context.AppStoreConnectGovernanceApplyResult));
            WriteAppleGovernanceOutput(operation, result, result.Success, outputJson, logger, applyReceiptPath, summary);
            return result.Success ? 0 : 1;
        }
        catch (Exception ex)
        {
            if (outputJson) WriteAppleGovernanceJson(operation, false, 2, null, ex.Message);
            else logger.Error(ex.Message);
            return 2;
        }
    }

    private static AppStoreConnectApiCredential ResolveAppleGovernanceCredential(string[] argv, string governanceConfigPath)
    {
        var keyPath = TryGetOptionValue(argv, "--key-path");
        var keyId = TryGetOptionValue(argv, "--key-id");
        var issuerId = TryGetOptionValue(argv, "--issuer-id");
        var baseDirectory = Path.GetDirectoryName(governanceConfigPath) ?? Directory.GetCurrentDirectory();
        var releaseConfig = TryGetOptionValue(argv, "--release-config");
        if (!string.IsNullOrWhiteSpace(releaseConfig))
        {
            var (release, fullReleasePath) = LoadPowerForgeReleaseSpecWithPath(releaseConfig!);
            var apple = release.AppleApps ?? throw new InvalidOperationException("Release config has no AppleApps section.");
            var releaseDirectory = Path.GetDirectoryName(fullReleasePath) ?? Directory.GetCurrentDirectory();
            var projectRoot = Path.GetFullPath(Path.Combine(releaseDirectory, string.IsNullOrWhiteSpace(apple.ProjectRoot) ? "." : apple.ProjectRoot!));
            baseDirectory = projectRoot;
            keyPath ??= apple.AppStoreConnectApiKeyPath;
            keyId ??= apple.AppStoreConnectApiKeyId;
            issuerId ??= apple.AppStoreConnectApiIssuerId;
        }

        keyPath ??= Environment.GetEnvironmentVariable("APP_STORE_CONNECT_PRIVATE_KEY_PATH") ?? Environment.GetEnvironmentVariable("ASC_PRIVATE_KEY_PATH");
        keyId ??= Environment.GetEnvironmentVariable("APP_STORE_CONNECT_KEY_ID") ?? Environment.GetEnvironmentVariable("ASC_KEY_ID");
        issuerId ??= Environment.GetEnvironmentVariable("APP_STORE_CONNECT_ISSUER_ID") ?? Environment.GetEnvironmentVariable("ASC_ISSUER_ID");
        if (string.IsNullOrWhiteSpace(keyPath) || string.IsNullOrWhiteSpace(keyId) || string.IsNullOrWhiteSpace(issuerId))
            throw new InvalidOperationException("Complete App Store Connect credentials are required via --key-path/--key-id/--issuer-id, the release config, or APP_STORE_CONNECT_/ASC_ environment variables.");
        var fullKeyPath = Path.IsPathRooted(keyPath) ? Path.GetFullPath(keyPath) : Path.GetFullPath(Path.Combine(baseDirectory, keyPath));
        if (!File.Exists(fullKeyPath)) throw new FileNotFoundException("App Store Connect private key was not found.", fullKeyPath);
        return new AppStoreConnectApiCredential
        {
            KeyId = keyId.Trim(),
            IssuerId = issuerId.Trim(),
            PrivateKey = File.ReadAllText(fullKeyPath).Trim()
        };
    }

    private static string ResolveGovernanceReceiptPath(string[] argv, string configPath, string fileName)
    {
        var configured = TryGetOptionValue(argv, "--receipt");
        var baseDirectory = Path.GetDirectoryName(configPath) ?? Directory.GetCurrentDirectory();
        return Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(baseDirectory, ".powerforge", "apple", fileName)
            : Path.IsPathRooted(configured) ? configured : Path.Combine(baseDirectory, configured));
    }

    private static void WriteGovernanceReceipt(string path, string json)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temporaryPath, json + Environment.NewLine);
        File.Move(temporaryPath, path, overwrite: true);
    }

    private static void WriteAppleGovernanceOutput(string operation, object result, bool success, bool outputJson, ILogger logger, string? receiptPath = null, bool summary = false)
    {
        if (outputJson)
        {
            var element = summary ? CreateAppleGovernanceSummary(result, receiptPath) : result switch
            {
                AppStoreConnectGovernancePlan plan => CliJson.SerializeToElement(plan, CliJson.Context.AppStoreConnectGovernancePlan),
                AppStoreConnectGovernanceApplyResult apply => CliJson.SerializeToElement(apply, CliJson.Context.AppStoreConnectGovernanceApplyResult),
                AppStoreConnectGovernanceSpec spec => CliJson.SerializeToElement(spec, CliJson.Context.AppStoreConnectGovernanceSpec),
                _ => JsonSerializer.SerializeToElement(result)
            };
            WriteAppleGovernanceJson(operation, success, success ? 0 : 1, element, success ? null : "Apple governance did not converge.");
            return;
        }

        if (result is AppStoreConnectGovernancePlan planResult)
        {
            logger.Info($"Apple governance {operation}: drift={planResult.DriftCount}, blocked={planResult.BlockedCount}, errors={planResult.Findings.Count(finding => finding.IsError)}");
            foreach (var change in planResult.Changes.Take(20)) logger.Info($" -> {change.Action} {change.ResourceType} {change.Key}: {change.Summary}");
            foreach (var finding in planResult.Findings.Take(20)) logger.Warn($"{finding.Code} {finding.Path}: {finding.Message}");
        }
        else if (result is AppStoreConnectGovernanceApplyResult applyResult)
        {
            logger.Info($"Apple governance apply: {(applyResult.Success ? "converged" : "failed")}; applied={applyResult.AppliedChanges.Length}; remaining={applyResult.FinalPlan.DriftCount}; blocked={applyResult.FinalPlan.BlockedCount}");
            foreach (var action in applyResult.NextActions) logger.Info("Next: " + action);
        }
        else if (result is AppStoreConnectGovernanceSpec snapshot)
            logger.Info($"Apple governance snapshot: app={snapshot.AppId}, accessibility={snapshot.Accessibility.Length}, subscriptions={snapshot.SubscriptionGroups.Sum(group => group.Subscriptions.Length)}");
        if (!string.IsNullOrWhiteSpace(receiptPath)) logger.Info("Receipt: " + receiptPath);
    }

    private static JsonElement CreateAppleGovernanceSummary(object result, string? receiptPath)
    {
        object summary = result switch
        {
            AppStoreConnectGovernancePlan plan => new
            {
                appId = plan.AppId,
                checkedAtUtc = plan.CheckedAtUtc,
                driftCount = plan.DriftCount,
                blockedCount = plan.BlockedCount,
                isConverged = plan.IsConverged,
                canApply = plan.CanApply,
                findingCount = plan.Findings.Length,
                errorCount = plan.Findings.Count(finding => finding.IsError),
                changeCounts = plan.Changes.GroupBy(change => new { change.Action, change.ResourceType })
                    .Select(group => new { action = group.Key.Action.ToString(), resourceType = group.Key.ResourceType, count = group.Count() })
                    .OrderBy(group => group.resourceType).ThenBy(group => group.action).ToArray(),
                sampleChanges = plan.Changes.Take(10).Select(change => new { action = change.Action.ToString(), resourceType = change.ResourceType, summary = change.Summary }).ToArray(),
                findings = plan.Findings.Take(10).Select(finding => new { code = finding.Code, path = finding.Path, message = finding.Message, isError = finding.IsError }).ToArray(),
                receiptPath
            },
            AppStoreConnectGovernanceApplyResult apply => new
            {
                appId = apply.AppId,
                startedAtUtc = apply.StartedAtUtc,
                completedAtUtc = apply.CompletedAtUtc,
                success = apply.Success,
                appliedCount = apply.AppliedChanges.Length,
                driftCount = apply.FinalPlan.DriftCount,
                blockedCount = apply.FinalPlan.BlockedCount,
                isConverged = apply.FinalPlan.IsConverged,
                canApply = apply.FinalPlan.CanApply,
                nextActions = apply.NextActions,
                receiptPath
            },
            AppStoreConnectGovernanceSpec spec => new
            {
                appId = spec.AppId,
                accessibilityCount = spec.Accessibility.Length,
                encryptionDeclarationCount = spec.EncryptionDeclarations.Length,
                subscriptionGroupCount = spec.SubscriptionGroups.Length,
                subscriptionCount = spec.SubscriptionGroups.Sum(group => group.Subscriptions.Length),
                receiptPath
            },
            _ => result
        };
        return JsonSerializer.SerializeToElement(summary);
    }

    private static void WriteAppleGovernanceJson(string operation, bool success, int exitCode, JsonElement? result, string? error)
    {
        WriteJson(new CliJsonEnvelope { SchemaVersion = OutputSchemaVersion, Command = "apple-governance " + operation, Success = success, ExitCode = exitCode, Result = result, Error = error });
    }

    private static int ParseAppleGovernanceMaximumChanges(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 500;
        if (!int.TryParse(value, out var count) || count is < 1 or > 1000)
            throw new ArgumentException("--max-changes must be an integer between 1 and 1000.");
        return count;
    }

    private static void ValidateAppleGovernanceArguments(string[] argv)
    {
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "--confirm", "--fail-on-drift", "--force", "--summary" };
        var options = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "--config", "--app-id", "--out", "--release-config", "--key-path", "--key-id", "--issuer-id", "--receipt", "--reviewed-plan", "--max-changes", "--output"
        };
        for (var index = 0; index < argv.Length; index++)
        {
            var value = argv[index];
            if (flags.Contains(value)) continue;
            if (!options.Contains(value)) throw new ArgumentException($"Unknown apple-governance option '{value}'.");
            if (++index >= argv.Length || argv[index].StartsWith("-", StringComparison.Ordinal))
                throw new ArgumentException($"Missing value for apple-governance option '{value}'.");
        }
    }
}
