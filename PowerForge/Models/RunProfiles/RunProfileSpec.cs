#pragma warning disable CS1591
namespace PowerForge;

/// <summary>
/// Supported reusable run-profile kinds.
/// </summary>
public enum RunProfileKind
{
    /// <summary>
    /// Launch a .NET project via <c>dotnet run</c>.
    /// </summary>
    Project = 0,

    /// <summary>
    /// Launch a PowerShell script via <c>pwsh</c> or Windows PowerShell.
    /// </summary>
    Script = 1,

    /// <summary>
    /// Launch an arbitrary executable directly.
    /// </summary>
    Command = 2
}

/// <summary>
/// Root specification for reusable run profiles.
/// </summary>
public sealed class RunProfileSpec
{
    /// <summary>
    /// Gets or sets the schema version.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Gets or sets the repository/project root used to resolve relative paths.
    /// </summary>
    public string? ProjectRoot { get; set; }

    /// <summary>
    /// Gets or sets the available run profiles.
    /// </summary>
    public RunProfile[] Profiles { get; set; } = Array.Empty<RunProfile>();
}

/// <summary>
/// Reusable run profile definition.
/// </summary>
public sealed class RunProfile
{
    public string Name { get; set; } = string.Empty;
    public RunProfileKind Kind { get; set; }
    public string? Description { get; set; }
    public string? Example { get; set; }
    public string? WorkingDirectory { get; set; }
    public string? Path { get; set; }
    public string? ProjectPath { get; set; }
    public string? Executable { get; set; }
    public string? Framework { get; set; }
    public string[] Arguments { get; set; } = Array.Empty<string>();
    public Dictionary<string, string?>? EnvironmentVariables { get; set; }
    public Dictionary<string, string?>? MsBuildProperties { get; set; }
    public bool PreferPwsh { get; set; } = true;
    public bool NoLaunchProfile { get; set; }
    public bool PassConfiguration { get; set; }
    public bool PassFramework { get; set; }
    public bool PassNoBuild { get; set; }
    public bool PassNoRestore { get; set; }
    public bool PassAllowRoot { get; set; }
    public bool PassIncludePrivateToolPacks { get; set; }
    public bool PassTestimoXRoot { get; set; }
    public bool PassExtraArgs { get; set; }
    public bool PassExtraArgsDirect { get; set; }
}

/// <summary>
/// Request to list or execute a run profile.
/// </summary>
public sealed class RunProfileExecutionRequest
{
    public string TargetName { get; set; } = string.Empty;
    public string Configuration { get; set; } = "Release";
    public string? Framework { get; set; }
    public bool NoBuild { get; set; }
    public bool NoRestore { get; set; }
    public string[] AllowRoot { get; set; } = Array.Empty<string>();
    public bool IncludePrivateToolPacks { get; set; }
    public string? TestimoXRoot { get; set; }
    public string[] ExtraArgs { get; set; } = Array.Empty<string>();
    public bool CaptureOutput { get; set; }
    public bool CaptureError { get; set; }
}

/// <summary>
/// Summary view used when listing reusable run profiles.
/// </summary>
public sealed class RunProfileSummary
{
    public string Name { get; set; } = string.Empty;
    public RunProfileKind Kind { get; set; }
    public string? Description { get; set; }
    public string? Example { get; set; }
    public string? Framework { get; set; }
}

/// <summary>
/// Fully prepared command for a run-profile launch.
/// </summary>
public sealed class RunProfilePreparedCommand
{
    public string TargetName { get; set; } = string.Empty;
    public RunProfileKind Kind { get; set; }
    public string? Description { get; set; }
    public string WorkingDirectory { get; set; } = string.Empty;
    public string Executable { get; set; } = string.Empty;
    public string[] Arguments { get; set; } = Array.Empty<string>();
    public string DisplayCommand { get; set; } = string.Empty;
    public bool CaptureOutput { get; set; }
    public bool CaptureError { get; set; }
}

/// <summary>
/// Result of executing a reusable run profile.
/// </summary>
public sealed class RunProfileExecutionResult
{
    public RunProfilePreparedCommand PreparedCommand { get; set; } = new();
    public int ExitCode { get; set; }
    public string StdOut { get; set; } = string.Empty;
    public string StdErr { get; set; } = string.Empty;
    public string Executable { get; set; } = string.Empty;
    public bool TimedOut { get; set; }
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}
#pragma warning restore CS1591
