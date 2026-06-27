namespace PowerForge;

/// <summary>
/// Policy describing a related set of modules that should stay version-coherent.
/// </summary>
public sealed class ManagedModuleFamilyPolicy
{
    /// <summary>
    /// Friendly policy name used in plans and diagnostics.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Module name prefix used to discover installed family members.
    /// </summary>
    public string? ModuleNamePrefix { get; set; }

    /// <summary>
    /// Exact module names that belong to the family.
    /// </summary>
    public IReadOnlyList<string> ModuleNames { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Requires discovered family members to align to the selected target version.
    /// </summary>
    public bool RequireSameVersion { get; set; } = true;
}
