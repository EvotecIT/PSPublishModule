namespace PowerForge;

/// <summary>
/// Configuration segment that describes running tests as part of a legacy build.
/// </summary>
public sealed class ConfigurationTestSegment : IConfigurationSegment
{
    /// <inheritdoc />
    public string Type => "TestsAfterMerge";

    /// <summary>Test configuration payload.</summary>
    public TestConfiguration Configuration { get; set; } = new();
}

/// <summary>
/// Test configuration payload for <see cref="ConfigurationTestSegment"/>.
/// </summary>
public sealed class TestConfiguration
{
    /// <summary>When to run the tests.</summary>
    public TestExecutionWhen When { get; set; } = TestExecutionWhen.AfterMerge;

    /// <summary>Path to the folder containing Pester tests.</summary>
    public string TestsPath { get; set; } = string.Empty;

    /// <summary>Force running tests even if caching would skip them.</summary>
    public bool Force { get; set; }
}

