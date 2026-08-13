using PowerForge;
using PowerForgeStudio.Orchestrator.Host;

namespace PowerForgeStudio.Orchestrator.Queue;

public sealed partial class ReleasePublishExecutionService
{
    internal static PowerForgeReleaseRequest CreateUnifiedPublishRequest(
        string configPath,
        PowerForgeReleaseSpec spec,
        PowerForgeReleaseResult builtResult,
        CancellationToken cancellationToken = default)
        => CreateUnifiedPublishRequest(
            configPath,
            builtResult,
            cancellationToken,
            HasEnabledModulePublisher(configPath, spec));

    internal static PowerForgeReleaseRequest CreateUnifiedPublishRequest(
        string configPath,
        PowerForgeReleaseResult builtResult,
        CancellationToken cancellationToken = default,
        bool modulePublisherActive = false)
    {
        var applePlan = builtResult.AppleAppPlan;
        return new PowerForgeReleaseRequest
        {
            ConfigPath = configPath,
            ModuleHostPath = PowerForgeStudioHostPaths.ResolvePSPublishModulePath(),
            ModuleRunMode = ConfigurationGateMode.Publish,
            ModulePublisherActive = modulePublisherActive,
            AppleMarketingVersion = applePlan?.RequestedMarketingVersion,
            AppleSourceCommit = applePlan?.SourceCommit,
            RequireImmutableAppleSourceSnapshot =
                applePlan?.RequireImmutableSourceSnapshot == true ||
                !string.IsNullOrWhiteSpace(applePlan?.SourceCommit),
            AppleExpectedPlanSha256 = builtResult.AppleReceipt?.PlanSha256,
            AppleExpectedArchiveSha256ByTarget = applePlan?.Apps
                .Where(static app => !string.IsNullOrWhiteSpace(app.ExpectedArchiveSha256))
                .ToDictionary(
                    static app => app.Name,
                    static app => app.ExpectedArchiveSha256!,
                    StringComparer.OrdinalIgnoreCase)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            AppleAdoptExistingBuild = applePlan?.AdoptExistingBuild == true,
            AppleResume = applePlan?.Automation.Resume,
            AppleWaitForProcessing = applePlan?.Automation.WaitForProcessing,
            AppleProcessingTimeoutSeconds = applePlan?.Automation.ProcessingTimeoutSeconds,
            ApplePollIntervalSeconds = applePlan?.Automation.PollIntervalSeconds,
            AppleActionConfirmed = true,
            CancellationToken = cancellationToken
        };
    }
}
