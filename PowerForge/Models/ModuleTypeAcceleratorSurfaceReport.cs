namespace PowerForge;

/// <summary>
/// Describes the type accelerator public surface that a module build requested for AssemblyLoadContext-backed dependencies.
/// </summary>
public sealed class ModuleTypeAcceleratorSurfaceReport
{
    /// <summary>Creates a new type accelerator surface report.</summary>
    public ModuleTypeAcceleratorSurfaceReport(
        AssemblyTypeAcceleratorExportMode mode,
        string reportPath,
        string[]? requestedTypes = null,
        string[]? requestedAssemblies = null,
        ModuleTypeAcceleratorAssemblyReport[]? assemblies = null,
        string[]? explicitTypesFound = null,
        string[]? explicitTypesMissing = null,
        string[]? warnings = null)
    {
        Mode = mode;
        ReportPath = reportPath ?? string.Empty;
        RequestedTypes = requestedTypes ?? Array.Empty<string>();
        RequestedAssemblies = requestedAssemblies ?? Array.Empty<string>();
        Assemblies = assemblies ?? Array.Empty<ModuleTypeAcceleratorAssemblyReport>();
        ExplicitTypesFound = explicitTypesFound ?? Array.Empty<string>();
        ExplicitTypesMissing = explicitTypesMissing ?? Array.Empty<string>();
        Warnings = warnings ?? Array.Empty<string>();
    }

    /// <summary>Effective type accelerator export mode.</summary>
    public AssemblyTypeAcceleratorExportMode Mode { get; }

    /// <summary>Path to the human-readable report file written during the build.</summary>
    public string ReportPath { get; }

    /// <summary>Explicit type names requested by the build configuration.</summary>
    public string[] RequestedTypes { get; }

    /// <summary>Assembly names requested by the build configuration.</summary>
    public string[] RequestedAssemblies { get; }

    /// <summary>Per-assembly type exposure details.</summary>
    public ModuleTypeAcceleratorAssemblyReport[] Assemblies { get; }

    /// <summary>Explicit requested type names that were found.</summary>
    public string[] ExplicitTypesFound { get; }

    /// <summary>Explicit requested type names that were not found.</summary>
    public string[] ExplicitTypesMissing { get; }

    /// <summary>Warnings captured while building the report.</summary>
    public string[] Warnings { get; }

    /// <summary>Total unique type accelerator names that the build intends to expose.</summary>
    public int TotalRegisteredTypeCount
        => Assemblies.SelectMany(static assembly => assembly.RegisteredTypes)
            .Concat(ExplicitTypesFound)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

    /// <summary>Total type names contributed by requested assemblies before duplicate removal.</summary>
    public int AssemblyRegisteredTypeCount
        => Assemblies.Sum(static assembly => assembly.RegisteredTypes.Length);

    /// <summary>Total public non-enum types skipped when enum-only mode is used.</summary>
    public int SkippedNonEnumTypeCount
        => Assemblies.Sum(static assembly => assembly.SkippedNonEnumTypeCount);
}

/// <summary>
/// Describes type accelerator exposure for one requested assembly.
/// </summary>
public sealed class ModuleTypeAcceleratorAssemblyReport
{
    /// <summary>Creates a new per-assembly type accelerator surface report.</summary>
    public ModuleTypeAcceleratorAssemblyReport(
        string assemblyName,
        string? assemblyPath = null,
        int exportedTypeCount = 0,
        string[]? registeredTypes = null,
        int skippedNonEnumTypeCount = 0,
        string? error = null)
    {
        AssemblyName = assemblyName ?? string.Empty;
        AssemblyPath = assemblyPath ?? string.Empty;
        ExportedTypeCount = exportedTypeCount;
        RegisteredTypes = registeredTypes ?? Array.Empty<string>();
        SkippedNonEnumTypeCount = skippedNonEnumTypeCount;
        Error = error ?? string.Empty;
    }

    /// <summary>Requested assembly simple name.</summary>
    public string AssemblyName { get; }

    /// <summary>Resolved assembly path when it was found in the staged module.</summary>
    public string AssemblyPath { get; }

    /// <summary>Number of public exported types discovered in the assembly.</summary>
    public int ExportedTypeCount { get; }

    /// <summary>Type names that match the configured exposure mode.</summary>
    public string[] RegisteredTypes { get; }

    /// <summary>Number of public non-enum types skipped by enum-only mode.</summary>
    public int SkippedNonEnumTypeCount { get; }

    /// <summary>Error captured while resolving or enumerating the assembly.</summary>
    public string Error { get; }
}
