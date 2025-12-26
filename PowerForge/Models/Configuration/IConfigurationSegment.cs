namespace PowerForge;

/// <summary>
/// Represents a configuration "segment" emitted by <c>New-Configuration*</c> cmdlets.
/// </summary>
public interface IConfigurationSegment
{
    /// <summary>
    /// Segment type name (legacy string values such as Manifest/Build/Formatting).
    /// </summary>
    string Type { get; }
}

