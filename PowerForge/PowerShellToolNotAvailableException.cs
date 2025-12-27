namespace PowerForge;

/// <summary>
/// Indicates that a PowerShell tool/module (such as PSResourceGet or PowerShellGet) is not available
/// in the out-of-process PowerShell session used by PowerForge.
/// </summary>
public sealed class PowerShellToolNotAvailableException : InvalidOperationException
{
    /// <summary>Name of the missing tool/module.</summary>
    public string ToolName { get; }

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public PowerShellToolNotAvailableException(string toolName, string message)
        : base(message)
    {
        ToolName = toolName ?? string.Empty;
    }

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public PowerShellToolNotAvailableException(string toolName, string message, Exception innerException)
        : base(message, innerException)
    {
        ToolName = toolName ?? string.Empty;
    }
}

