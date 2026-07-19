using PowerForge;

namespace PowerForge.Cli;

internal sealed class AppleReleaseCliPlanSummary
{
    public PowerForgeAppleReleaseAction Action { get; set; }

    public bool PlanOnly { get; set; }

    public bool ValidateOnly { get; set; }

    public string ReceiptPath { get; set; } = string.Empty;

    public bool Resume { get; set; }

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

    public string? BundleId { get; set; }

    public string? AppId { get; set; }

    public string Scheme { get; set; } = string.Empty;

    public bool GenerateProjectIfMissing { get; set; }
}
