namespace PowerForge;

/// <summary>
/// Stable JSON context written for module pipeline lifecycle actions.
/// </summary>
public sealed class ModulePipelineActionContext
{
    /// <summary>Context schema version for external scripts.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Lifecycle stage that triggered the action.</summary>
    public ModulePipelineActionStage Stage { get; set; }

    /// <summary>Friendly action name.</summary>
    public string ActionName { get; set; } = string.Empty;

    /// <summary>Module name resolved by the pipeline plan.</summary>
    public string ModuleName { get; set; } = string.Empty;

    /// <summary>Project root used by the pipeline.</summary>
    public string ProjectRoot { get; set; } = string.Empty;

    /// <summary>Version requested by configuration before stepping.</summary>
    public string ExpectedVersion { get; set; } = string.Empty;

    /// <summary>Resolved module version for this run.</summary>
    public string ResolvedVersion { get; set; } = string.Empty;

    /// <summary>Optional prerelease tag.</summary>
    public string? PreRelease { get; set; }

    /// <summary>Current staging root when available.</summary>
    public string? StagingPath { get; set; }

    /// <summary>Current module manifest path when available.</summary>
    public string? ManifestPath { get; set; }

    /// <summary>Current module root. This is normally the staging root once staging exists.</summary>
    public string? ModuleRoot { get; set; }

    /// <summary>Documentation output path when configured.</summary>
    public string? DocumentationPath { get; set; }

    /// <summary>Documentation readme path when configured.</summary>
    public string? DocumentationReadmePath { get; set; }

    /// <summary>Artifact paths produced so far.</summary>
    public string[] ArtefactPaths { get; set; } = Array.Empty<string>();

    /// <summary>Publish destination summaries produced so far.</summary>
    public string[] PublishDestinations { get; set; } = Array.Empty<string>();

    /// <summary>Context file path written for the current action.</summary>
    public string ContextPath { get; set; } = string.Empty;
}

/// <summary>
/// Result of a module pipeline lifecycle action.
/// </summary>
public sealed class ModulePipelineActionResult
{
    /// <summary>Action name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Lifecycle stage where the action ran.</summary>
    public ModulePipelineActionStage Stage { get; set; }

    /// <summary>Whether the action process exited successfully.</summary>
    public bool Succeeded { get; set; }

    /// <summary>Process exit code.</summary>
    public int ExitCode { get; set; }

    /// <summary>PowerShell executable used to run the action.</summary>
    public string Executable { get; set; } = string.Empty;

    /// <summary>Script file path when the action used a file.</summary>
    public string? FilePath { get; set; }

    /// <summary>Whether the action used inline script text.</summary>
    public bool Inline { get; set; }

    /// <summary>Working directory used for the action process.</summary>
    public string WorkingDirectory { get; set; } = string.Empty;

    /// <summary>Path to the JSON context file provided through POWERFORGE_CONTEXT.</summary>
    public string ContextPath { get; set; } = string.Empty;

    /// <summary>Captured stdout.</summary>
    public string StdOut { get; set; } = string.Empty;

    /// <summary>Captured stderr.</summary>
    public string StdErr { get; set; } = string.Empty;

    /// <summary>Whether the pipeline continued after a failed action.</summary>
    public bool ContinuedOnError { get; set; }
}
