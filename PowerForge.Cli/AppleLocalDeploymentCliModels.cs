using PowerForge;

namespace PowerForge.Cli;

internal sealed class AppleLocalDeploymentCliResult
{
    public bool Planned { get; set; }

    public bool Success { get; set; }

    public double TotalDurationSeconds { get; set; }

    public string Target { get; set; } = string.Empty;

    public ApplePlatform Platform { get; set; }

    public AppleArchiveVariant ArchiveVariant { get; set; }

    public string Configuration { get; set; } = "Debug";

    public bool OptimizeSwift { get; set; }

    public string? Profile { get; set; }

    public string ProjectPath { get; set; } = string.Empty;

    public string Scheme { get; set; } = string.Empty;

    public string DerivedDataPath { get; set; } = string.Empty;

    public string? SourceRevision { get; set; }

    public bool? SourceDirty { get; set; }

    public string? Device { get; set; }

    public string? InstallRoot { get; set; }

    public bool Launch { get; set; }

    public bool UseBuildMirror { get; set; }

    public string? BuildMirrorPath { get; set; }

    public string? AppPath { get; set; }

    public string? InstalledAppPath { get; set; }

    public string? DeviceIdentifier { get; set; }

    public bool? BuildSucceeded { get; set; }

    public bool? InstallSucceeded { get; set; }

    public bool? LaunchSucceeded { get; set; }

    public bool DeviceLocked { get; set; }

    public string? Warning { get; set; }

    public string? Diagnostic { get; set; }

    public AppleLocalDeploymentCliStage[] Stages { get; set; } = Array.Empty<AppleLocalDeploymentCliStage>();
}

internal sealed class AppleLocalDeploymentCliStage
{
    public string Name { get; set; } = string.Empty;
    public double DurationSeconds { get; set; }
    public int ExitCode { get; set; }
    public bool Succeeded { get; set; }
    public bool TimedOut { get; set; }
    public bool StartFailed { get; set; }
}
