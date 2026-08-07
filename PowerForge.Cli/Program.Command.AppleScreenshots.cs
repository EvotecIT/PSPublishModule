using System.Text.Json;
using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private const string AppleScreenshotsUsage =
        "Usage: powerforge apple-screenshots manifest --config <screenshots.json> " +
        "[--capture-provenance <json> --expected-repository <owner/repo> --expected-workflow-ref <workflow-ref> | --version <x.y|x.y.z> --source-commit <sha>] " +
        "--approved-by <identity> --allowed-root <reviewed-capture-root> [--out <manifest.json>] " +
        "[--write-root <trusted-output-root>] " +
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
            var allowedRoot = Path.GetFullPath(RequiredOption(argv, "--allowed-root"));
            var provenance = ResolveScreenshotCaptureProvenance(argv);
            var manifest = new AppStoreConnectScreenshotApprovalService().Create(
                new AppStoreConnectScreenshotApprovalRequest
                {
                    Spec = spec,
                    AppId = appId,
                    BaseDirectory = baseDirectory,
                    AllowedRoot = allowedRoot,
                    VersionString = ResolveProvenanceBoundOption(argv, "--version", provenance?.MarketingVersion)!,
                    SourceCommit = ResolveProvenanceBoundOption(argv, "--source-commit", provenance?.SourceCommit)!,
                    CaptureRunId = provenance?.CaptureRunId,
                    CaptureRepository = provenance?.Repository,
                    CaptureWorkflowRef = provenance?.WorkflowRef,
                    ApprovedBy = RequiredOption(argv, "--approved-by"),
                    InitiatedBy = TryGetOptionValue(argv, "--initiated-by"),
                    ApprovalEvidence = TryGetOptionValue(argv, "--approval-evidence"),
                    XcodeVersion = ResolveProvenanceBoundOption(argv, "--xcode-version", provenance?.XcodeVersion, required: false),
                    Runtime = ResolveProvenanceBoundOption(argv, "--runtime", provenance?.Runtime, required: false),
                    Device = ResolveProvenanceBoundOption(argv, "--device", provenance?.Device, required: false),
                    Theme = ResolveProvenanceBoundOption(argv, "--theme", provenance?.Theme, required: false),
                    Scenario = ResolveProvenanceBoundOption(argv, "--scenario", provenance?.Scenario, required: false)
                });
            if (provenance is not null)
                ValidateScreenshotCaptureInventory(provenance, manifest, baseDirectory, allowedRoot);
            var configuredOutput = spec.Quality?.ApprovalManifestPath;
            var outputPath = ResolvePathFromBase(
                baseDirectory,
                TryGetOptionValue(argv, "--out") ??
                configuredOutput ??
                Path.GetFileNameWithoutExtension(configPath) + ".approval.json");
            var writeRoot = TryGetOptionValue(argv, "--write-root");
            if (!string.IsNullOrWhiteSpace(writeRoot))
                EnsureTrustedScreenshotManifestPath(outputPath, Path.GetFullPath(writeRoot));
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            if (!string.IsNullOrWhiteSpace(writeRoot))
                EnsureTrustedScreenshotManifestPath(outputPath, Path.GetFullPath(writeRoot));
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

    private static ScreenshotCaptureProvenance? ResolveScreenshotCaptureProvenance(string[] argv)
    {
        var configuredPath = TryGetOptionValue(argv, "--capture-provenance");
        if (string.IsNullOrWhiteSpace(configuredPath)) return null;

        var path = Path.GetFullPath(configuredPath);
        if (!File.Exists(path)) throw new FileNotFoundException("Screenshot capture provenance was not found.", path);
        EnsureTrustedScreenshotManifestPath(path, Path.GetDirectoryName(path)!);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("schemaVersion", out var schema) || schema.GetInt32() != 2)
            throw new InvalidOperationException("Screenshot capture provenance must use schemaVersion 2.");

        static string RequiredString(JsonElement value, string name)
        {
            if (!value.TryGetProperty(name, out var property))
                throw new InvalidOperationException($"Screenshot capture provenance is missing '{name}'.");
            var text = property.ValueKind == JsonValueKind.String ? property.GetString() : property.ToString();
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException($"Screenshot capture provenance has an empty '{name}'.");
            return text.Trim();
        }

        var repository = RequiredString(root, "repository");
        var workflowRef = RequiredString(root, "workflowRef");
        var expectedRepository = RequiredOption(argv, "--expected-repository").Trim();
        var expectedWorkflowRef = RequiredOption(argv, "--expected-workflow-ref").Trim();
        if (!repository.Equals(expectedRepository, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Screenshot capture repository '{repository}' does not match expected repository '{expectedRepository}'.");
        if (!workflowRef.Equals(expectedWorkflowRef, StringComparison.Ordinal))
            throw new InvalidOperationException($"Screenshot capture workflow '{workflowRef}' does not match expected workflow '{expectedWorkflowRef}'.");
        var captureRunId = RequiredString(root, "captureRunId");

        var sourceCommit = RequiredString(root, "sourceCommit").ToLowerInvariant();
        if (sourceCommit.Length != 40 || !sourceCommit.All(Uri.IsHexDigit))
            throw new InvalidOperationException("Screenshot capture provenance SourceCommit must be an exact 40-character Git commit SHA.");

        if (!root.TryGetProperty("screenshots", out var screenshotsElement) || screenshotsElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("Screenshot capture provenance must contain an exact screenshots inventory.");
        var screenshots = screenshotsElement.EnumerateArray().Select(item =>
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("Screenshot capture provenance contains an invalid screenshot inventory entry.");
            var relativePath = RequiredString(item, "path").Replace('\\', '/');
            if (relativePath.Length == 0 || relativePath[0] == '/' ||
                Path.IsPathRooted(relativePath) || relativePath.Split('/').Any(part => part is "." or ".."))
                throw new InvalidOperationException($"Screenshot capture provenance contains unsafe screenshot path '{relativePath}'.");
            var sha256 = RequiredString(item, "sha256").ToLowerInvariant();
            if (sha256.Length != 64 || !sha256.All(Uri.IsHexDigit))
                throw new InvalidOperationException($"Screenshot capture provenance contains invalid SHA-256 for '{relativePath}'.");
            if (!item.TryGetProperty("width", out var widthElement) || !widthElement.TryGetInt32(out var width) || width <= 0 ||
                !item.TryGetProperty("height", out var heightElement) || !heightElement.TryGetInt32(out var height) || height <= 0)
                throw new InvalidOperationException($"Screenshot capture provenance contains invalid dimensions for '{relativePath}'.");
            return new ScreenshotCaptureInventoryEntry(relativePath, sha256, width, height);
        }).ToArray();
        if (screenshots.Length == 0 || screenshots.Select(static item => item.Path).Distinct(StringComparer.Ordinal).Count() != screenshots.Length)
            throw new InvalidOperationException("Screenshot capture provenance must contain a non-empty inventory with unique paths.");

        return new ScreenshotCaptureProvenance(
            repository,
            captureRunId,
            workflowRef,
            RequiredString(root, "marketingVersion"),
            sourceCommit,
            RequiredString(root, "xcodeVersion"),
            RequiredString(root, "runtime"),
            RequiredString(root, "device"),
            RequiredString(root, "theme"),
            RequiredString(root, "scenario"),
            screenshots);
    }

    private static void ValidateScreenshotCaptureInventory(
        ScreenshotCaptureProvenance provenance,
        AppStoreConnectScreenshotApprovalManifest manifest,
        string baseDirectory,
        string allowedRoot)
    {
        var expected = provenance.Screenshots
            .OrderBy(static item => item.Path, StringComparer.Ordinal)
            .ToArray();
        var actual = manifest.Screenshots.Select(item =>
        {
            var fullPath = Path.GetFullPath(Path.Combine(baseDirectory, item.File));
            var relative = Path.GetRelativePath(allowedRoot, fullPath).Replace('\\', '/');
            if (Path.IsPathRooted(relative) || relative.Equals("..", StringComparison.Ordinal) || relative.StartsWith("../", StringComparison.Ordinal))
                throw new InvalidOperationException($"Approved screenshot '{item.File}' escapes the capture inventory root.");
            return new ScreenshotCaptureInventoryEntry(relative, item.Sha256.ToLowerInvariant(), item.Width, item.Height);
        }).OrderBy(static item => item.Path, StringComparer.Ordinal).ToArray();

        if (expected.Length != actual.Length || !expected.SequenceEqual(actual))
            throw new InvalidOperationException("Selected screenshots do not exactly match the retained capture provenance byte inventory.");
    }

    private static string? ResolveProvenanceBoundOption(
        string[] argv,
        string option,
        string? provenanceValue,
        bool required = true)
    {
        var explicitValue = TryGetOptionValue(argv, option)?.Trim();
        if (!string.IsNullOrWhiteSpace(provenanceValue))
        {
            if (!string.IsNullOrWhiteSpace(explicitValue) &&
                !explicitValue.Equals(provenanceValue, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{option} does not match the retained screenshot capture provenance.");
            return provenanceValue;
        }
        if (!string.IsNullOrWhiteSpace(explicitValue)) return explicitValue;
        if (required) throw new ArgumentException($"Missing required option '{option}'.");
        return null;
    }

    private sealed record ScreenshotCaptureProvenance(
        string Repository,
        string CaptureRunId,
        string WorkflowRef,
        string MarketingVersion,
        string SourceCommit,
        string XcodeVersion,
        string Runtime,
        string Device,
        string Theme,
        string Scenario,
        ScreenshotCaptureInventoryEntry[] Screenshots);

    private sealed record ScreenshotCaptureInventoryEntry(string Path, string Sha256, int Width, int Height);

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

    private static void EnsureTrustedScreenshotManifestPath(string outputPath, string writeRoot)
    {
        var root = Path.GetFullPath(writeRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var output = Path.GetFullPath(outputPath);
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var relative = Path.GetRelativePath(root, output);
        if (Path.IsPathRooted(relative) || relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Screenshot approval manifest output '{output}' escapes trusted write root '{root}'.");
        }

        FileSystemInfo? current = new FileInfo(output);
        while (current is not null)
        {
            if (current.LinkTarget is not null || (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0))
                throw new InvalidOperationException($"Screenshot approval manifest output traverses link or reparse point '{current.FullName}'.");
            if (string.Equals(Path.GetFullPath(current.FullName).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), root, pathComparison))
                break;
            current = current switch
            {
                FileInfo file => file.Directory,
                DirectoryInfo directory => directory.Parent,
                _ => null
            };
        }
    }
}
