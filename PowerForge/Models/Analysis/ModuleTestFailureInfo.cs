namespace PowerForge;

/// <summary>
/// Represents a single failed test case discovered during module test execution.
/// </summary>
public sealed class ModuleTestFailureInfo
{
    /// <summary>Test name (human-readable identifier).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Failure message extracted from the test result.</summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>Optional stack trace extracted from the test result.</summary>
    public string? StackTrace { get; set; }

    /// <summary>Optional test duration.</summary>
    public TimeSpan? Duration { get; set; }
}

