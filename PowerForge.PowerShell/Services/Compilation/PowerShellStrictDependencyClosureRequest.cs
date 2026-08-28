namespace PowerForge;

/// <summary>Exact target and reviewed graph consumed by Strict delivered-artifact certification.</summary>
internal sealed class PowerShellStrictDependencyClosureRequest
{
    internal PowerShellStrictDependencyClosureRequest(
        IEnumerable<PowerShellCompilationArtifactFile> files,
        string targetFramework,
        string? runtimeIdentifier,
        PowerShellCompilationDependencyGraph dependencyGraph,
        PowerShellCompilationExecutableOptimization optimization = PowerShellCompilationExecutableOptimization.None)
    {
        Files = files?.ToArray() ?? throw new ArgumentNullException(nameof(files));
        TargetFramework = string.IsNullOrWhiteSpace(targetFramework)
            ? throw new ArgumentException("Strict dependency certification requires an explicit target framework.", nameof(targetFramework))
            : targetFramework.Trim();
        RuntimeIdentifier = runtimeIdentifier?.Trim() ?? string.Empty;
        DependencyGraph = dependencyGraph ?? throw new ArgumentNullException(nameof(dependencyGraph));
        Optimization = optimization;
    }

    internal PowerShellCompilationArtifactFile[] Files { get; }
    internal string TargetFramework { get; }
    internal string RuntimeIdentifier { get; }
    internal PowerShellCompilationDependencyGraph DependencyGraph { get; }
    internal PowerShellCompilationExecutableOptimization Optimization { get; }
}
