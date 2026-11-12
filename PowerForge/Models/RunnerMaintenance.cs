using System;
using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>Machine-local policy for recurring maintenance of registered GitHub runners.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RunnerMaintenanceSpec
{
    /// <summary>Persistent policy, lock, journal and receipt directory outside runner workspaces.</summary>
    public string StateRootPath { get; set; } = string.Empty;

    /// <summary>Prebuilt CLI application host installed inside the state directory.</summary>
    public string ExecutablePath { get; set; } = string.Empty;

    /// <summary>Runner installations participating in the host admission lock.</summary>
    public RunnerMaintenanceRegistration[] Runners { get; set; } = Array.Empty<RunnerMaintenanceRegistration>();

    /// <summary>Expanded NuGet cache shared by the enrolled service identity.</summary>
    public string? NuGetPackagesPath { get; set; }

    /// <summary>Legacy NuGet HTTP cache eligible for expired download cleanup.</summary>
    public string? NuGetHttpCachePath { get; set; }

    /// <summary>Shared NuGet scratch directory used by the maintenance process and enrolled runners.</summary>
    public string? NuGetScratchPath { get; set; }

    /// <summary>Service-identity NuGet configuration files in which enrollment enables package last-access metadata.</summary>
    public string[] NuGetConfigPaths { get; set; } = Array.Empty<string>();

    /// <summary>Source-qualified legacy fixture layouts approved for this machine.</summary>
    public RunnerMaintenanceTemporaryRoot[] LegacyTemporaryRoots { get; set; } = Array.Empty<RunnerMaintenanceTemporaryRoot>();

    /// <summary>Free-space floor in GiB that requests pressure recovery.</summary>
    public int MinimumFreeGiB { get; set; } = 20;

    /// <summary>Free-space target in GiB retained across deferred pressure cleanup.</summary>
    public int TargetFreeGiB { get; set; } = 30;

    /// <summary>Days of inactivity before an unchanged recorded CI checkout can be retired.</summary>
    public int WorkspaceRetentionDays { get; set; } = 3;

    /// <summary>Days of package disuse before ordinary public-package retirement.</summary>
    public int NuGetRetentionDays { get; set; } = 30;

    /// <summary>Hours between successful deep retention passes.</summary>
    public int DeepIntervalHours { get; set; } = 24;

    /// <summary>Execution budget checked between filesystem operations.</summary>
    public int MaximumRunSeconds { get; set; } = 90;

    /// <summary>Maximum time a job hook waits for the host admission lock.</summary>
    public int AdmissionTimeoutSeconds { get; set; } = 120;

    /// <summary>Optional retained Docker builder-cache budget. Images, containers and volumes are outside this policy.</summary>
    public int? DockerBuilderCacheGiB { get; set; }
}

/// <summary>One installed runner and its private job temporary directory.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RunnerMaintenanceRegistration
{
    /// <summary>Installation directory containing the non-secret .runner registration.</summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>Agent identifier captured during enrollment.</summary>
    public long AgentId { get; set; }

    /// <summary>Agent name captured during enrollment.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Organization or repository URL captured during enrollment.</summary>
    public string GitHubUrl { get; set; } = string.Empty;

    /// <summary>Physical work root resolved from the installed registration.</summary>
    public string WorkRootPath { get; set; } = string.Empty;

    /// <summary>Private temporary directory reserved for this runner.</summary>
    public string TempPath { get; set; } = string.Empty;

    /// <summary>Top-level workspace families that must survive maintenance.</summary>
    public string[] ProtectedWorkspaces { get; set; } = Array.Empty<string>();
}

/// <summary>Explicit source-proven layout for old generated fixture directories.</summary>
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class RunnerMaintenanceTemporaryRoot
{
    /// <summary>Physical parent directory containing the fixture directories.</summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>Literal producer prefix followed by a 32-character GUID.</summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>Allowed top-level generated file extensions.</summary>
    public string[] FileExtensions { get; set; } = Array.Empty<string>();

    /// <summary>Allowed top-level file names, including explicitly disposable synthetic databases.</summary>
    public string[] FileNames { get; set; } = Array.Empty<string>();

    /// <summary>Allowed generated top-level subdirectories.</summary>
    public string[] DirectoryNames { get; set; } = Array.Empty<string>();

    /// <summary>Minimum age of every entry before fixture retirement.</summary>
    public int MinimumAgeHours { get; set; } = 24;
}

/// <summary>Lifecycle phase supplied by the installed hook or host scheduler.</summary>
public enum RunnerMaintenancePhase
{
    /// <summary>Periodic host-local execution.</summary>
    Scheduled,
    /// <summary>Admission gate before checkout and job steps.</summary>
    Starting,
    /// <summary>Completion hook after job steps and post-actions.</summary>
    Completed
}

/// <summary>Observable maintenance result, also written to a rotating local receipt.</summary>
public sealed class RunnerMaintenanceResult
{
    /// <summary>True when admission and maintenance finished without an unrecovered error.</summary>
    public bool Success { get; set; } = true;
    /// <summary>True when cleanup was planned without changing output.</summary>
    public bool DryRun { get; set; }
    /// <summary>True when active work deferred shared cleanup.</summary>
    public bool Deferred { get; set; }
    /// <summary>True when a job hook acquired its admission gate.</summary>
    public bool Admitted { get; set; }
    /// <summary>Logical length of files actually removed during this execution.</summary>
    public long RemovedBytes { get; set; }
    /// <summary>Logical payload length planned during a dry run.</summary>
    public long PlannedBytes { get; set; }
    /// <summary>Paths selected during a dry run.</summary>
    public string[] PlannedPaths { get; set; } = Array.Empty<string>();
    /// <summary>Free bytes measured before the pass.</summary>
    public long FreeBytesBefore { get; set; }
    /// <summary>Free bytes measured after the pass.</summary>
    public long FreeBytesAfter { get; set; }
    /// <summary>Elapsed time including admission wait and cleanup.</summary>
    public long ElapsedMilliseconds { get; set; }
    /// <summary>Paths removed by this execution.</summary>
    public string[] RemovedPaths { get; set; } = Array.Empty<string>();
    /// <summary>Retained targets and actionable reasons.</summary>
    public string[] Retained { get; set; } = Array.Empty<string>();
    /// <summary>Errors requiring operator attention.</summary>
    public string[] Errors { get; set; } = Array.Empty<string>();
    /// <summary>UTC completion timestamp.</summary>
    public DateTime CompletedUtc { get; set; }
}
