using System.Text.Json;
using PowerForge;
using PowerForge.Cli;

internal static partial class Program
{
    private const string AppleScreenshotsUsage =
        "Usage: powerforge apple-screenshots <manifest --config <screenshots.json> | manifests --release-config <powerforge.release.json>> " +
        "[--store-art-lineage <json>] [--capture-provenance <json> --expected-repository <owner/repo> --expected-workflow-ref <workflow-ref> | --version <x.y|x.y.z> --source-commit <sha>] " +
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
            var operation = argv[0];
            var configPaths = ResolveScreenshotManifestConfigPaths(operation, argv);
            if (configPaths.Length > 1 && !string.IsNullOrWhiteSpace(TryGetOptionValue(argv, "--out")))
                throw new ArgumentException("--out is valid only when generating one screenshot approval manifest.");
            var outputs = configPaths
                .Select(configPath => CreateScreenshotApprovalManifest(configPath, argv))
                .ToArray();

            if (outputJson)
            {
                WriteJson(new CliJsonEnvelope
                {
                    SchemaVersion = OutputSchemaVersion,
                    Command = operation.Equals("manifests", StringComparison.OrdinalIgnoreCase)
                        ? "apple-screenshots.manifests"
                        : "apple-screenshots.manifest",
                    Success = true,
                    ExitCode = 0,
                    Result = JsonSerializer.SerializeToElement(new
                    {
                        manifestCount = outputs.Length,
                        screenshotCount = outputs.Sum(static output => output.Manifest.Screenshots.Length),
                        manifests = outputs.Select(static output => new
                        {
                            output.OutputPath,
                            screenshotCount = output.Manifest.Screenshots.Length,
                            output.Manifest.VersionString,
                            output.Manifest.SourceCommit,
                            output.Manifest.ApprovedBy
                        }).ToArray()
                    })
                });
            }
            else
            {
                foreach (var output in outputs)
                    logger.Success($"Approved {output.Manifest.Screenshots.Length} screenshot(s): {output.OutputPath}");
            }
            return 0;
        }
        catch (Exception exception)
        {
            return WriteReleaseError(outputJson, "apple-screenshots.manifest", 1, exception.Message, logger);
        }
    }

    private static ScreenshotManifestOutput CreateScreenshotApprovalManifest(string configPath, string[] argv)
    {
        var baseDirectory = Path.GetDirectoryName(configPath) ?? Directory.GetCurrentDirectory();
        var spec = CliJson.DeserializeOrThrow(
            File.ReadAllText(configPath),
            CliJson.Context.AppStoreConnectScreenshotSyncSpec,
            configPath);
        var appId = ResolveScreenshotApprovalAppId(argv, spec);
        var allowedRoot = Path.GetFullPath(RequiredOption(argv, "--allowed-root"));
        var provenance = ResolveScreenshotCaptureProvenance(argv);
        if (provenance is null && !string.IsNullOrWhiteSpace(TryGetOptionValue(argv, "--store-art-lineage")))
            throw new InvalidOperationException("--store-art-lineage requires --capture-provenance.");
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
            ValidateScreenshotCaptureInventory(provenance, manifest, baseDirectory, allowedRoot, argv);
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
        return new ScreenshotManifestOutput(outputPath, manifest);
    }

    private static string[] ResolveScreenshotManifestConfigPaths(string operation, string[] argv)
    {
        if (operation.Equals("manifest", StringComparison.OrdinalIgnoreCase))
            return new[] { Path.GetFullPath(RequiredOption(argv, "--config")) };
        if (!operation.Equals("manifests", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Apple screenshot command must be 'manifest' or 'manifests'.");

        var releaseConfigValue = RequiredOption(argv, "--release-config");
        var (releaseSpec, releaseConfigPath) = LoadPowerForgeReleaseSpecWithPath(releaseConfigValue);
        var apple = releaseSpec.AppleApps ?? throw new InvalidOperationException("Release configuration does not contain AppleApps.");
        var releaseDirectory = Path.GetDirectoryName(releaseConfigPath) ?? Directory.GetCurrentDirectory();
        var projectRoot = ResolvePathFromBase(
            releaseDirectory,
            string.IsNullOrWhiteSpace(apple.ProjectRoot) ? "." : apple.ProjectRoot);
        var configured = new[] { apple.ScreenshotConfigPath }
            .Concat(apple.ScreenshotConfigPaths ?? Array.Empty<string>())
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(value => ResolvePathFromBase(projectRoot, value!))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (configured.Length == 0)
            throw new InvalidOperationException("AppleApps must configure ScreenshotConfigPath or ScreenshotConfigPaths.");
        return configured;
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
            !root.TryGetProperty("schemaVersion", out var schema) || schema.GetInt32() is not (2 or 3))
            throw new InvalidOperationException("Screenshot capture provenance must use schemaVersion 2 or 3.");

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
        if (!GitObjectId.IsFull(sourceCommit))
            throw new InvalidOperationException("Screenshot capture provenance SourceCommit must be a full SHA-1 or SHA-256 Git commit object id.");

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

        if (schema.GetInt32() == 3 && (!root.TryGetProperty("compositions", out var compositionInventory) ||
            compositionInventory.ValueKind != JsonValueKind.Array || compositionInventory.GetArrayLength() == 0))
            throw new InvalidOperationException("Screenshot capture provenance v3 requires a non-empty compositions inventory.");
        if (schema.GetInt32() == 2 && root.TryGetProperty("compositions", out _))
            throw new InvalidOperationException("Composition inventory requires screenshot capture provenance schemaVersion 3.");

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
            screenshots,
            schema.GetInt32() == 3 && root.TryGetProperty("compositions", out var compositions)
                ? JsonSerializer.Deserialize(compositions.GetRawText(), CliJson.Context.AppleStoreArtLineageArray) ?? throw new InvalidOperationException("Empty compositions inventory.")
                : Array.Empty<AppleStoreArtLineage>());
    }

    private static void ValidateScreenshotCaptureInventory(
        ScreenshotCaptureProvenance provenance,
        AppStoreConnectScreenshotApprovalManifest manifest,
        string baseDirectory,
        string allowedRoot,
        string[] argv)
    {
        var retained = provenance.Screenshots
            .ToHashSet();
        var actual = manifest.Screenshots.Select(item =>
        {
            var fullPath = Path.GetFullPath(Path.Combine(baseDirectory, item.File));
            var relative = Path.GetRelativePath(allowedRoot, fullPath).Replace('\\', '/');
            if (Path.IsPathRooted(relative) || relative.Equals("..", StringComparison.Ordinal) || relative.StartsWith("../", StringComparison.Ordinal))
                throw new InvalidOperationException($"Approved screenshot '{item.File}' escapes the capture inventory root.");
            return new ScreenshotCaptureInventoryEntry(relative, item.Sha256.ToLowerInvariant(), item.Width, item.Height);
        }).ToArray();

        var compositions = provenance.Compositions;
        var lineagePath = TryGetOptionValue(argv, "--store-art-lineage");
        if (!string.IsNullOrWhiteSpace(lineagePath))
        {
            if (compositions.Length != 0) throw new InvalidOperationException("Use retained v3 compositions or --store-art-lineage, not both.");
            EnsureTrustedScreenshotManifestPath(Path.GetFullPath(lineagePath), allowedRoot);
            compositions = JsonSerializer.Deserialize(File.ReadAllText(lineagePath), CliJson.Context.AppleStoreArtLineageArray)
                ?? throw new InvalidOperationException("Empty store art lineage.");
        }
        AppleStoreArtLineageValidator.Validate(compositions, provenance.Screenshots.Select(item => new AppleStoreArtInput
            { Path = item.Path, Sha256 = item.Sha256, Width = item.Width, Height = item.Height }).ToArray(), allowedRoot);
        var derived = compositions.ToDictionary(item => new ScreenshotCaptureInventoryEntry(item.Path, item.Sha256.ToLowerInvariant(), item.Width, item.Height));
        if (actual.Distinct().Count() != actual.Length || actual.Any(item => !retained.Contains(item) && !derived.ContainsKey(item)))
            throw new InvalidOperationException("Selected screenshots are not an exact byte-for-byte subset of the retained capture provenance inventory or its validated compositions.");
        for (var index = 0; index < actual.Length; index++)
        {
            if (!derived.TryGetValue(actual[index], out var composition)) continue;
            if (!string.Equals(composition.Locale, manifest.Locale, StringComparison.Ordinal))
                throw new InvalidOperationException($"Composed frame '{composition.Frame}' locale does not match the approved listing locale.");
            manifest.SchemaVersion = 3;
            manifest.Screenshots[index].Composition = composition;
            manifest.Screenshots[index].PerceptualHash = composition.PerceptualHash;
        }
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
        ScreenshotCaptureInventoryEntry[] Screenshots,
        AppleStoreArtLineage[] Compositions);

    private sealed record ScreenshotCaptureInventoryEntry(string Path, string Sha256, int Width, int Height);

    private sealed record ScreenshotManifestOutput(
        string OutputPath,
        AppStoreConnectScreenshotApprovalManifest Manifest);

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
