namespace PowerForge;

/// <summary>
/// Evidence from importing a benchmark-delivered module in a PowerShell host.
/// </summary>
public sealed class ManagedModuleImportValidationResult
{
    /// <summary>
    /// Host requested for the validation.
    /// </summary>
    public ManagedModuleImportValidationHost Host { get; set; }

    /// <summary>
    /// Executable used by the validation host, when one was started.
    /// </summary>
    public string? HostExecutable { get; set; }

    /// <summary>
    /// True when the host imported the module and reported the expected version.
    /// </summary>
    public bool Succeeded { get; set; }

    /// <summary>
    /// Version expected from the benchmark delivery result.
    /// </summary>
    public string? ExpectedVersion { get; set; }

    /// <summary>
    /// Version reported by the imported module.
    /// </summary>
    public string? ImportedVersion { get; set; }

    /// <summary>
    /// Validation elapsed time.
    /// </summary>
    public TimeSpan Elapsed { get; set; }

    /// <summary>
    /// Human-readable validation evidence.
    /// </summary>
    public string? Message { get; set; }
}
