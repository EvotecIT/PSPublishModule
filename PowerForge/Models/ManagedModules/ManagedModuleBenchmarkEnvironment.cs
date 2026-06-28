using System.Runtime.InteropServices;

namespace PowerForge;

/// <summary>
/// Runtime and host metadata attached to benchmark evidence.
/// </summary>
public sealed class ManagedModuleBenchmarkEnvironment
{
    /// <summary>
    /// PowerShell version that produced the benchmark result, when invoked through a PowerShell host.
    /// </summary>
    public string? PowerShellVersion { get; set; }

    /// <summary>
    /// PowerShell edition that produced the benchmark result, when available.
    /// </summary>
    public string? PowerShellEdition { get; set; }

    /// <summary>
    /// PowerShell host name, when invoked through a PowerShell host.
    /// </summary>
    public string? PowerShellHostName { get; set; }

    /// <summary>
    /// PowerShell host version, when invoked through a PowerShell host.
    /// </summary>
    public string? PowerShellHostVersion { get; set; }

    /// <summary>
    /// .NET runtime description used by the benchmark process.
    /// </summary>
    public string RuntimeDescription { get; set; } = string.Empty;

    /// <summary>
    /// Runtime identifier reported by the benchmark process.
    /// </summary>
    public string RuntimeIdentifier { get; set; } = string.Empty;

    /// <summary>
    /// Operating system description reported by the benchmark process.
    /// </summary>
    public string OperatingSystemDescription { get; set; } = string.Empty;

    /// <summary>
    /// Process architecture reported by the benchmark process.
    /// </summary>
    public string ProcessArchitecture { get; set; } = string.Empty;

    /// <summary>
    /// Captures runtime metadata for the current process.
    /// </summary>
    public static ManagedModuleBenchmarkEnvironment Capture()
        => new()
        {
            RuntimeDescription = RuntimeInformation.FrameworkDescription,
#if NETFRAMEWORK
            RuntimeIdentifier = string.Empty,
#else
            RuntimeIdentifier = RuntimeInformation.RuntimeIdentifier,
#endif
            OperatingSystemDescription = RuntimeInformation.OSDescription,
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString()
        };
}
