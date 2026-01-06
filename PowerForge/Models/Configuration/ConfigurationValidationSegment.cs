namespace PowerForge;

/// <summary>
/// Configuration segment that describes module validation checks.
/// </summary>
public sealed class ConfigurationValidationSegment : IConfigurationSegment
{
    /// <inheritdoc />
    public string Type => "Validation";

    /// <summary>Validation settings payload.</summary>
    public ModuleValidationSettings Settings { get; set; } = new();
}
