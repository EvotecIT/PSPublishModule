namespace PowerForge;

/// <content>
/// Publishes a signed, checkpointed module directory through the normal module publisher.
/// </content>
public sealed partial class ModulePublisher
{
    internal ModulePublishResult PublishCheckpointed(ModuleCheckpointPublishRequest request)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (request.Publish is null) throw new ArgumentNullException(nameof(request.Publish));
        if (string.IsNullOrWhiteSpace(request.ProjectRoot)) throw new ArgumentException("ProjectRoot is required.", nameof(request.ProjectRoot));
        if (string.IsNullOrWhiteSpace(request.ModuleName)) throw new ArgumentException("ModuleName is required.", nameof(request.ModuleName));
        if (string.IsNullOrWhiteSpace(request.ModuleVersion)) throw new ArgumentException("ModuleVersion is required.", nameof(request.ModuleVersion));
        if (string.IsNullOrWhiteSpace(request.ModulePath)) throw new ArgumentException("ModulePath is required.", nameof(request.ModulePath));

        if (request.Publish.Destination != PublishDestination.PowerShellGallery)
            throw new InvalidOperationException("Checkpointed module publication only supports PowerShell repository destinations.");

        var modulePath = Path.GetFullPath(request.ModulePath);
        var manifestPath = Path.Combine(modulePath, $"{request.ModuleName}.psd1");
        if (!File.Exists(manifestPath))
            throw new FileNotFoundException($"Checkpointed module manifest was not found: {manifestPath}", manifestPath);

        var metadata = new ModuleManifestMetadataReader().Read(manifestPath);
        if (!string.Equals(metadata.ModuleName, request.ModuleName, StringComparison.OrdinalIgnoreCase) ||
            !VersionsMatch(metadata.ModuleVersion, request.ModuleVersion) ||
            !string.Equals(
                NormalizePreRelease(metadata.PreRelease),
                NormalizePreRelease(request.PreRelease),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Checkpointed module identity '{metadata.ModuleName} {metadata.ModuleVersion}{FormatPreRelease(metadata.PreRelease)}' " +
                $"does not match the approved release identity '{request.ModuleName} {request.ModuleVersion}{FormatPreRelease(request.PreRelease)}'.");
        }

        var plan = CreateCheckpointedPublishPlan(request);
        var buildResult = new ModuleBuildResult(
            modulePath,
            manifestPath,
            new ExportSet([], [], []));
        return Publish(
            request.Publish,
            plan,
            buildResult,
            [],
            includeScriptFolders: true);
    }

    internal static ModulePipelinePlan CreateCheckpointedPublishPlan(
        ModuleCheckpointPublishRequest request)
        => new(
            moduleName: request.ModuleName,
            projectRoot: Path.GetFullPath(request.ProjectRoot),
            expectedVersion: request.ModuleVersion,
            resolvedVersion: request.ModuleVersion,
            preRelease: NormalizePreRelease(request.PreRelease),
            manifest: null,
            buildSpec: new ModuleBuildSpec
            {
                Name = request.ModuleName,
                SourcePath = Path.GetFullPath(request.ProjectRoot),
                StagingPath = Path.GetFullPath(request.ModulePath),
                Version = request.ModuleVersion,
                PreReleaseTag = NormalizePreRelease(request.PreRelease)
            },
            resolvedCsprojPath: null,
            syncNETProjectVersion: false,
            compatiblePSEditions: [],
            requiredModules: [],
            externalModuleDependencies: [],
            requiredModulesForPackaging: [],
            information: request.Information,
            documentation: null,
            delivery: request.Delivery,
            documentationBuild: null,
            compatibilitySettings: null,
            fileConsistencySettings: null,
            validationSettings: null,
            formatting: null,
            importModules: null,
            placeHolders: [],
            placeHolderOption: null,
            commandModuleDependencies: new Dictionary<string, string[]>(),
            testsAfterMerge: [],
            actions: [],
            mergeModule: false,
            mergeMissing: false,
            doNotAttemptToFixRelativePaths: false,
            approvedModules: [],
            moduleSkip: null,
            signModule: false,
            signing: null,
            publishes: [],
            artefacts: [],
            installEnabled: false,
            installStrategy: InstallationStrategy.AutoRevision,
            installKeepVersions: 0,
            installRoots: [],
            installLegacyFlatHandling: LegacyFlatModuleHandling.Warn,
            installPreserveVersions: [],
            installMissingModules: false,
            installMissingModulesForce: false,
            installMissingModulesPrerelease: false,
            installMissingModulesRepository: null,
            installMissingModulesCredential: null,
            stagingWasGenerated: false,
            deleteGeneratedStagingAfterRun: false);

    private static bool VersionsMatch(string? left, string? right)
        => Version.TryParse(left, out var leftVersion) &&
           Version.TryParse(right, out var rightVersion)
            ? leftVersion.Equals(rightVersion)
            : string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string? NormalizePreRelease(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : value!.Trim().TrimStart('-');

    private static string FormatPreRelease(string? value)
        => NormalizePreRelease(value) is { Length: > 0 } normalized
            ? $"-{normalized}"
            : string.Empty;
}

internal sealed class ModuleCheckpointPublishRequest
{
    internal PublishConfiguration Publish { get; set; } = new();

    internal string ProjectRoot { get; set; } = string.Empty;

    internal string ModuleName { get; set; } = string.Empty;

    internal string ModuleVersion { get; set; } = string.Empty;

    internal string? PreRelease { get; set; }

    internal string ModulePath { get; set; } = string.Empty;

    internal InformationConfiguration? Information { get; set; }

    internal DeliveryOptionsConfiguration? Delivery { get; set; }
}
