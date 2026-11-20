namespace PowerForge;

/// <summary>Coarse stages shared by compiler hosts.</summary>
public enum PowerShellCompilationHostPhase
{
    /// <summary>Resolving the input and target selection.</summary>
    Resolving,
    /// <summary>Collecting and validating dependencies.</summary>
    PlanningDependencies,
    /// <summary>Parsing and checking PowerShell source.</summary>
    Analyzing,
    /// <summary>Determining or generating the artifact's final code.</summary>
    Shaping,
    /// <summary>Restoring and compiling the generated project.</summary>
    Compiling,
    /// <summary>Verifying and staging the delivered artifact.</summary>
    Publishing
}

/// <summary>Progress observed at a compiler phase boundary.</summary>
public sealed class PowerShellCompilationProgress
{
    /// <summary>Creates a progress report for a phase the compiler has entered.</summary>
    public PowerShellCompilationProgress(PowerShellCompilationHostPhase phase) => Phase = phase;

    /// <summary>Phase entered.</summary>
    public PowerShellCompilationHostPhase Phase { get; }

    internal static void Report(IProgress<PowerShellCompilationProgress>? progress, PowerShellCompilationHostPhase phase)
    {
        if (progress is null) return;
        try { progress.Report(new PowerShellCompilationProgress(phase)); }
        catch { /* An observer cannot change build behavior. */ }
    }
}
