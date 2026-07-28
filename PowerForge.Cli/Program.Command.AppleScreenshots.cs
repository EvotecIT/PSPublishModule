using System.Text.Json;
using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private const string AppleScreenshotsUsage =
        "Usage: powerforge apple-screenshots manifest --config <screenshots.json> --version <x.y.z> " +
        "--source-commit <sha> --approved-by <identity> --allowed-root <reviewed-capture-root> [--out <manifest.json>] " +
        "[--app-id <asc-app-id> | --release-config <powerforge.release.json> [--target <name-or-scheme>]] " +
        "[--initiated-by <identity>] [--approval-evidence <url-or-id>] " +
        "[--xcode-version <value>] [--runtime <value>] [--device <value>] [--theme <value>] [--scenario <value>] [--output json]";

    private static int CommandAppleScreenshots(string[] filteredArgs, CliOptions cli, ILogger logger)
    {
        var argv = filteredArgs.Skip(1).ToArray();
        var outputJson = IsJsonOutput(argv);
        if (argv.Length == 0 || IsHelpArg(argv[0]))
        {
            Console.WriteLine(AppleScreenshotsUsage);
            return argv.Length == 0 ? 2 : 0;
        }

        try
        {
            if (!argv[0].Equals("manifest", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Apple screenshot command must be 'manifest'.");
            var configPath = Path.GetFullPath(RequiredOption(argv, "--config"));
            var baseDirectory = Path.GetDirectoryName(configPath) ?? Directory.GetCurrentDirectory();
            var spec = CliJson.DeserializeOrThrow(
                File.ReadAllText(configPath),
                CliJson.Context.AppStoreConnectScreenshotSyncSpec,
                configPath);
            var appId = ResolveScreenshotApprovalAppId(argv, spec);
            var manifest = new AppStoreConnectScreenshotApprovalService().Create(
                new AppStoreConnectScreenshotApprovalRequest
                {
                    Spec = spec,
                    AppId = appId,
                    BaseDirectory = baseDirectory,
                    AllowedRoot = Path.GetFullPath(RequiredOption(argv, "--allowed-root")),
                    VersionString = RequiredOption(argv, "--version"),
                    SourceCommit = RequiredOption(argv, "--source-commit"),
                    ApprovedBy = RequiredOption(argv, "--approved-by"),
                    InitiatedBy = TryGetOptionValue(argv, "--initiated-by"),
                    ApprovalEvidence = TryGetOptionValue(argv, "--approval-evidence"),
                    XcodeVersion = TryGetOptionValue(argv, "--xcode-version"),
                    Runtime = TryGetOptionValue(argv, "--runtime"),
                    Device = TryGetOptionValue(argv, "--device"),
                    Theme = TryGetOptionValue(argv, "--theme"),
                    Scenario = TryGetOptionValue(argv, "--scenario")
                });
            var configuredOutput = spec.Quality?.ApprovalManifestPath;
            var outputPath = ResolvePathFromBase(
                baseDirectory,
                TryGetOptionValue(argv, "--out") ??
                configuredOutput ??
                Path.GetFileNameWithoutExtension(configPath) + ".approval.json");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            File.WriteAllText(
                outputPath,
                JsonSerializer.Serialize(manifest, CliJson.Context.AppStoreConnectScreenshotApprovalManifest) + Environment.NewLine);

            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = "apple-screenshots.manifest",
                    Success = true,
                    ExitCode = 0,
                    Result = JsonSerializer.SerializeToElement(new
                    {
                        outputPath,
                        screenshotCount = manifest.Screenshots.Length,
                        manifest.VersionString,
                        manifest.SourceCommit,
                        manifest.ApprovedBy
                    })
                });
            }
            else
            {
                logger.Success($"Approved {manifest.Screenshots.Length} screenshot(s): {outputPath}");
            }
            return 0;
        }
        catch (Exception exception)
        {
            return WriteReleaseError(outputJson, "apple-screenshots.manifest", 1, exception.Message, logger);
        }
    }

    private static string? ResolveScreenshotApprovalAppId(
        string[] argv,
        AppStoreConnectScreenshotSyncSpec spec)
    {
        var explicitAppId = TryGetOptionValue(argv, "--app-id");
        if (!string.IsNullOrWhiteSpace(explicitAppId) || !string.IsNullOrWhiteSpace(spec.AppId))
            return explicitAppId;

        var releaseConfigPath = TryGetOptionValue(argv, "--release-config");
        if (string.IsNullOrWhiteSpace(releaseConfigPath))
            return null;

        var (releaseSpec, _) = LoadPowerForgeReleaseSpecWithPath(releaseConfigPath);
        var candidates = (releaseSpec.AppleApps?.Apps ?? Array.Empty<AppleAppConfiguration>())
            .Where(app => app.Enabled &&
                          app.Platform == spec.Platform &&
                          !string.IsNullOrWhiteSpace(app.AppStoreConnectAppId))
            .ToArray();
        var selectedTargets = (TryGetOptionValue(argv, "--target") ?? string.Empty)
            .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(static value => value.Trim())
            .Where(static value => value.Length > 0)
            .ToArray();
        if (selectedTargets.Length > 0)
        {
            candidates = candidates
                .Where(app => selectedTargets.Any(selected => ScreenshotApprovalTargetMatches(app, selected)))
                .ToArray();
        }

        var appIds = candidates
            .Select(app => app.AppStoreConnectAppId!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (appIds.Length != 1)
        {
            throw new InvalidOperationException(
                $"Screenshot approval could not resolve exactly one App Store Connect app id for platform '{spec.Platform}'" +
                (selectedTargets.Length == 0 ? "." : $" and target '{string.Join(",", selectedTargets)}'.") +
                " Set AppId in the screenshot config, select one release target, or pass --app-id explicitly.");
        }

        return appIds[0];
    }

    private static bool ScreenshotApprovalTargetMatches(AppleAppConfiguration app, string selected)
        => string.Equals(app.Name?.Trim(), selected.Trim(), StringComparison.OrdinalIgnoreCase) ||
           string.Equals(app.Scheme?.Trim(), selected.Trim(), StringComparison.OrdinalIgnoreCase) ||
           string.Equals(app.BundleId?.Trim(), selected.Trim(), StringComparison.OrdinalIgnoreCase);
}
