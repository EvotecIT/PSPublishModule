namespace PowerForge;

/// <summary>Stable high-level phases in a unified PowerForge release.</summary>
public enum PowerForgeReleaseProgressPhase
{
    /// <summary>Package planning and shared release version resolution.</summary>
    Versioning,
    /// <summary>PowerShell module build and publish lane.</summary>
    Module,
    /// <summary>NuGet project build and publish lane.</summary>
    Packages,
    /// <summary>Portable executable, installer, and store packaging lane.</summary>
    Tools,
    /// <summary>Unified GitHub release and asset upload lane.</summary>
    GitHub
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
