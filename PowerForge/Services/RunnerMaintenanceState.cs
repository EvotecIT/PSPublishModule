using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PowerForge;

internal sealed class RunnerMaintenanceState
{
    public int SchemaVersion { get; set; } = 1;
    public DateTime LastDeepSweepUtc { get; set; }
    public long PressureTargetBytes { get; set; }
    public bool Due { get; set; }
    public Dictionary<string, DateTime> PackageUse { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<RunnerMaintenanceJob> Jobs { get; set; } = new();
    public Dictionary<string, RunnerMaintenanceProcessIdentity> Listeners { get; set; } = new();
}

internal sealed class RunnerMaintenanceJob
{
    public string RunnerRoot { get; set; } = string.Empty;
    public string Workspace { get; set; } = string.Empty;
    public string SourceSha { get; set; } = string.Empty;
    public int WorkerId { get; set; }
    public long WorkerStartTicks { get; set; }
    public DateTime CompletedUtc { get; set; }
    public List<RunnerMaintenanceProcessIdentity> Processes { get; set; } = new();
    public List<string> TrackingIds { get; set; } = new();
}

internal sealed class RunnerMaintenanceProcessIdentity
{
    public int Id { get; set; }
    public long StartTicks { get; set; }
}

internal sealed class RunnerMaintenanceInstallationBackup
{
    public string Path { get; set; } = string.Empty;
    public byte[]? Bytes { get; set; }
}

internal sealed class RunnerMaintenanceInstallationRecovery
{
    public int SchemaVersion { get; set; } = 1;
    public string[] RunnerRoots { get; set; } = Array.Empty<string>();
    public string[] ConfigurationPaths { get; set; } = Array.Empty<string>();
    public List<RunnerMaintenanceInstallationBackup> Files { get; set; } = new();
}

internal sealed class RunnerMaintenanceReady
{
    public bool CleanupAllowed { get; set; } = true;
    public int WorkerId { get; set; }
    public long WorkerStartTicks { get; set; }
    public int HookId { get; set; }
    public long HookStartTicks { get; set; }
    public string RunnerRoot { get; set; } = string.Empty;
}

internal sealed class RunnerMaintenanceJournal
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = string.Empty;
    public string Root { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public bool Directory { get; set; }
    public long Bytes { get; set; }
    public bool Moved { get; set; }
    public bool Repository { get; set; }
    public bool RuntimeIpc { get; set; }
}

internal sealed class RunnerMaintenanceHook
{
    public string Executable { get; set; } = string.Empty;
    public string Policy { get; set; } = string.Empty;
    public string RunnerRoot { get; set; } = string.Empty;
    public string Phase { get; set; } = string.Empty;
    public string? PreviousHook { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true,
    PropertyNameCaseInsensitive = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(RunnerMaintenanceSpec))]
[JsonSerializable(typeof(RunnerMaintenanceResult))]
[JsonSerializable(typeof(RunnerMaintenanceState))]
[JsonSerializable(typeof(RunnerMaintenanceReady))]
[JsonSerializable(typeof(RunnerMaintenanceJournal))]
[JsonSerializable(typeof(RunnerMaintenanceHook))]
[JsonSerializable(typeof(RunnerMaintenanceInstallationRecovery))]
internal sealed partial class RunnerMaintenanceJsonContext : JsonSerializerContext
{
}
