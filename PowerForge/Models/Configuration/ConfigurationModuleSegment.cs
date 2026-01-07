namespace PowerForge;

/// <summary>
/// Configuration segment that declares a module dependency entry (required/external/approved).
/// </summary>
public sealed class ConfigurationModuleSegment : IConfigurationSegment
{
    /// <summary>
    /// Dependency kind.
    /// </summary>
    public ModuleDependencyKind Kind { get; set; } = ModuleDependencyKind.RequiredModule;

    /// <inheritdoc />
    public string Type => Kind.ToString();

    /// <summary>
    /// Dependency configuration payload.
    /// </summary>
    public ModuleDependencyConfiguration Configuration { get; set; } = new();
}

/// <summary>
/// Dependency configuration payload for <see cref="ConfigurationModuleSegment"/>.
/// </summary>
public sealed class ModuleDependencyConfiguration
{
    /// <summary>Dependency module name.</summary>
    public string ModuleName { get; set; } = string.Empty;

    /// <summary>
    /// Minimum module version (legacy key: ModuleVersion). Use <see cref="RequiredVersion"/> for exact matches.
    /// </summary>
    public string? ModuleVersion { get; set; }

    /// <summary>Minimum module version (preferred). Use <see cref="RequiredVersion"/> for exact matches.</summary>
    public string? MinimumVersion { get; set; }

    /// <summary>Exact required version.</summary>
    public string? RequiredVersion { get; set; }

    /// <summary>Module GUID (optional; legacy field).</summary>
    public string? Guid { get; set; }
}

