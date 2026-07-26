using System;

#if !NET472
namespace PowerForge;

/// <summary>
/// Describes a durable external-storage layout for a self-hosted macOS GitHub Actions runner.
/// </summary>
public sealed class MacOsRunnerStorageProvisioningSpec
{
    /// <summary>
    /// GitHub Actions runner installation directory containing <c>.runner</c> and <c>runsvc.sh</c>.
    /// </summary>
    public string RunnerRootPath { get; set; } = string.Empty;

    /// <summary>
    /// Runner-specific state directory on an external APFS volume.
    /// </summary>
    public string StateRootPath { get; set; } = string.Empty;

    /// <summary>
    /// Runner work directory on the external volume.
    /// </summary>
    public string WorkRootPath { get; set; } = string.Empty;

    /// <summary>
    /// Optional CoreSimulator directory. Defaults to <c>~/Library/Developer/CoreSimulator</c>.
    /// </summary>
    public string? CoreSimulatorPath { get; set; }

    /// <summary>
    /// Optional runner LaunchAgent property-list path. Defaults to the path recorded in <c>.service</c>.
    /// </summary>
    public string? LaunchAgentPath { get; set; }

    /// <summary>
    /// Maximum virtual CoreSimulator sparse-bundle size in GiB.
    /// </summary>
    public int CoreSimulatorImageSizeGb { get; set; } = 120;

    /// <summary>
    /// Seconds the generated runner wrapper waits for the external state directory to appear.
    /// </summary>
    public int ExternalStorageWaitSeconds { get; set; } = 120;

    /// <summary>
    /// Plans the operation without changing files or invoking macOS tools.
    /// </summary>
    public bool DryRun { get; set; } = true;
}

/// <summary>
/// One planned or applied runner-storage provisioning step.
/// </summary>
public sealed class MacOsRunnerStorageProvisioningStep
{
    /// <summary>Stable step identifier.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Human-readable operation description.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Whether the step changed state.</summary>
    public bool Changed { get; set; }

    /// <summary>Whether the step was skipped because the desired state already existed.</summary>
    public bool Skipped { get; set; }

    /// <summary>Paths governed by the step.</summary>
    public string[] Paths { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Result of planning or applying macOS runner external-storage provisioning.
/// </summary>
public sealed class MacOsRunnerStorageProvisioningResult
{
    /// <summary>Runner installation directory.</summary>
    public string RunnerRootPath { get; set; } = string.Empty;

    /// <summary>External runner state directory.</summary>
    public string StateRootPath { get; set; } = string.Empty;

    /// <summary>External runner work directory.</summary>
    public string WorkRootPath { get; set; } = string.Empty;

    /// <summary>External APFS volume mount root.</summary>
    public string ExternalVolumeRootPath { get; set; } = string.Empty;

    /// <summary>Expected external APFS volume UUID used by the runner wrapper.</summary>
    public string ExternalVolumeUuid { get; set; } = string.Empty;

    /// <summary>CoreSimulator sparse-bundle path.</summary>
    public string CoreSimulatorImagePath { get; set; } = string.Empty;

    /// <summary>CoreSimulator mount path expected by Apple tooling.</summary>
    public string CoreSimulatorMountPath { get; set; } = string.Empty;

    /// <summary>Generated runner wrapper path.</summary>
    public string RunnerWrapperPath { get; set; } = string.Empty;

    /// <summary>Recoverable backup directory used by an applied migration.</summary>
    public string BackupRootPath { get; set; } = string.Empty;

    /// <summary>Whether the operation was a dry-run plan.</summary>
    public bool DryRun { get; set; }

    /// <summary>Whether the desired storage layout was already fully configured.</summary>
    public bool AlreadyConfigured { get; set; }

    /// <summary>Provisioning steps.</summary>
    public MacOsRunnerStorageProvisioningStep[] Steps { get; set; } = Array.Empty<MacOsRunnerStorageProvisioningStep>();
}
#endif
