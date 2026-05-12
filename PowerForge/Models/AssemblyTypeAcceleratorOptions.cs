namespace PowerForge;

/// <summary>
/// Shared helpers for resolving AssemblyLoadContext type accelerator settings.
/// </summary>
public static class AssemblyTypeAcceleratorOptions
{
    /// <summary>
    /// Resolves the effective type accelerator export mode from an optional explicit mode and configured lists.
    /// </summary>
    public static AssemblyTypeAcceleratorExportMode ResolveMode(
        AssemblyTypeAcceleratorExportMode? mode,
        IReadOnlyList<string>? typeNames,
        IReadOnlyList<string>? assemblyNames)
    {
        if (mode.HasValue)
            return mode.Value;

        if (HasAnyConfiguredValue(assemblyNames))
            return AssemblyTypeAcceleratorExportMode.Assembly;

        if (HasAnyConfiguredValue(typeNames))
            return AssemblyTypeAcceleratorExportMode.AllowList;

        return AssemblyTypeAcceleratorExportMode.None;
    }

    /// <summary>
    /// Returns true when the configured list contains at least one non-blank value.
    /// </summary>
    public static bool HasAnyConfiguredValue(IReadOnlyList<string>? values)
        => values is { Count: > 0 }
           && values.Any(static value => !string.IsNullOrWhiteSpace(value));
}
