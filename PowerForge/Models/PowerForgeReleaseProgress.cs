namespace PowerForge;

/// <summary>Stable high-level phases in a unified PowerForge release.</summary>
public enum PowerForgeReleaseProgressPhase
{
    /// <summary>Package planning and shared release version resolution.</summary>
    Versioning = 0,
    /// <summary>PowerShell module build and publish lane.</summary>
    Module = 1,
    /// <summary>NuGet project build and publish lane.</summary>
    Packages = 2,
    /// <summary>Portable executable, installer, and store packaging lane.</summary>
    Tools = 3,
    /// <summary>Unified GitHub release and asset upload lane.</summary>
    GitHub = 4,
    /// <summary>Opt-in VirusTotal Monitor publisher registration lane.</summary>
    VirusTotal = 5,
    /// <summary>Apple exact-source validation, archive, export, and release automation lane.</summary>
    AppleApps = 6,
    /// <summary>Consumer-owned checks against the complete staged release.</summary>
    Validation = 7
}

/// <summary>Receives structured high-level progress for a unified release.</summary>
public interface IPowerForgeReleaseProgressReporter
{
    /// <summary>Marks a release phase as started.</summary>
    void PhaseStarted(PowerForgeReleaseProgressPhase phase, int totalItems, string? detail = null);

    /// <summary>Marks a release phase as completed.</summary>
    void PhaseCompleted(PowerForgeReleaseProgressPhase phase, string? detail = null);

    /// <summary>Marks a release phase as failed.</summary>
    void PhaseFailed(PowerForgeReleaseProgressPhase phase, string? detail = null);
}

/// <summary>State of a concrete work item within a unified release phase.</summary>
public enum PowerForgeReleaseProgressItemState
{
    /// <summary>The item has not started.</summary>
    Planned,
    /// <summary>The item is currently running.</summary>
    Started,
    /// <summary>The item completed successfully.</summary>
    Completed,
    /// <summary>The item failed.</summary>
    Failed,
    /// <summary>The item was not required or was skipped after a failure.</summary>
    Skipped
}

/// <summary>A concrete planned work item within a unified release phase.</summary>
public sealed class PowerForgeReleaseProgressItem
{
    /// <summary>Owning release phase.</summary>
    public PowerForgeReleaseProgressPhase Phase { get; set; }

    /// <summary>Stable key within the owning phase.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Human-friendly work item title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Optional work item kind used by presentation hosts.</summary>
    public string? Kind { get; set; }

    /// <summary>Optional semantic destination or artifact target shown separately from the action title.</summary>
    public string? Target { get; set; }

    /// <summary>Optional presentation group key used to keep nested workflow stages distinct.</summary>
    public string? GroupKey { get; set; }

    /// <summary>Optional presentation group title used for nested workflow history.</summary>
    public string? GroupTitle { get; set; }

    /// <summary>Optional order within the owning release phase.</summary>
    public int? GroupOrder { get; set; }

    /// <summary>Optional counter label such as Project, Package, or Asset.</summary>
    public string? CounterLabel { get; set; }

    /// <summary>One-based position in the owning plan.</summary>
    public int Position { get; set; }

    /// <summary>Total number of items in the owning plan.</summary>
    public int Total { get; set; }

    /// <summary>Current numeric progress for presentation hosts; zero when unavailable.</summary>
    public double ProgressValue { get; set; }

    /// <summary>Maximum numeric progress for presentation hosts; zero when indeterminate.</summary>
    public double ProgressMaximum { get; set; }

    /// <summary>Measured duration after the item reaches a terminal state.</summary>
    public TimeSpan? Duration { get; set; }
}

/// <summary>
/// Optional detailed release progress contract. Hosts that implement it receive the real
/// module, package, and tool work items while older hosts retain high-level phase updates.
/// </summary>
public interface IPowerForgeReleaseProgressReporterV2 : IPowerForgeReleaseProgressReporter
{
    /// <summary>Registers the planned items for a release phase.</summary>
    void ItemsPlanned(PowerForgeReleaseProgressPhase phase, IReadOnlyList<PowerForgeReleaseProgressItem> items);

    /// <summary>Updates a concrete release work item.</summary>
    void ItemUpdated(
        PowerForgeReleaseProgressItem item,
        PowerForgeReleaseProgressItemState state,
        string? detail = null);
}
