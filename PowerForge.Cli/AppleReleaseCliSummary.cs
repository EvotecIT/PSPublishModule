using PowerForge;

namespace PowerForge.Cli;

internal sealed class AppleReleaseCliPlanSummary
{
    public PowerForgeAppleReleaseAction Action { get; set; }

    public bool PlanOnly { get; set; }

    public bool ValidateOnly { get; set; }

    public string ReceiptPath { get; set; } = string.Empty;

    public string? PlanSha256 { get; set; }

    public bool Resume { get; set; }

    public bool AdoptExistingBuild { get; set; }

    public PowerForgeAppleShipPhase? ShipPhase { get; set; }

    public bool ReuseRemoteScreenshots { get; set; }

    public bool WaitForProcessing { get; set; }

    public int ProcessingTimeoutSeconds { get; set; }

    public int PollIntervalSeconds { get; set; }

    public string[] EnabledSteps { get; set; } = Array.Empty<string>();

    public AppleReleaseCliTargetSummary[] Targets { get; set; } = Array.Empty<AppleReleaseCliTargetSummary>();

    public bool RequiresConfirmation { get; set; }
}

internal sealed class AppleReleaseCliTargetSummary
{
    public string Name { get; set; } = string.Empty;

    public ApplePlatform Platform { get; set; }

    public AppleDistributionRoute DistributionRoute { get; set; }

    public AppleProductRole ProductRole { get; set; }

    public string? ParentTarget { get; set; }

    public string[] Capabilities { get; set; } = Array.Empty<string>();

    public AppleTestFlightPolicy TestFlightPolicy { get; set; }

    public bool ShipToTestFlight { get; set; }

    public bool ShipToAppStoreReview { get; set; }

    public string? BundleId { get; set; }

    public string? AppId { get; set; }

    public bool AppIdDiscovered { get; set; }

    public string Scheme { get; set; } = string.Empty;

    public string? MarketingVersion { get; set; }

    public string? BuildNumber { get; set; }

    public bool GenerateProjectIfMissing { get; set; }
}
