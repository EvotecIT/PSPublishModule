namespace PowerForge.Web;

/// <summary>Options for validating imported PowerShell example scripts used by API docs.</summary>
public sealed class WebApiDocsPowerShellExampleValidationOptions
{
    /// <summary>Path to PowerShell help XML or folder.</summary>
    public string HelpPath { get; set; } = string.Empty;
    /// <summary>Optional path to a PowerShell module manifest used to discover example roots.</summary>
    public string? PowerShellModuleManifestPath { get; set; }
    /// <summary>Optional explicit path to a PowerShell examples folder or script file.</summary>
    public string? PowerShellExamplesPath { get; set; }
    /// <summary>Maximum allowed validation time in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 60;
    /// <summary>When true, prefer pwsh over Windows PowerShell when both are available.</summary>
    public bool PreferPwsh { get; set; } = true;
    /// <summary>When true, execute matched example scripts after syntax validation.</summary>
    public bool ExecuteMatchedExamples { get; set; }
    /// <summary>Maximum allowed execution time in seconds for each matched example script.</summary>
    public int ExecutionTimeoutSeconds { get; set; } = 60;
}

/// <summary>Validation result for PowerShell example scripts.</summary>
public sealed class WebApiDocsPowerShellExampleValidationResult
{
    /// <summary>Resolved PowerShell help XML file used to infer documented commands.</summary>
    public string? HelpPath { get; set; }
    /// <summary>Resolved module manifest path, when one was found.</summary>
    public string? ManifestPath { get; set; }
    /// <summary>Executable used to perform syntax validation, when validation ran.</summary>
    public string? Executable { get; set; }
    /// <summary>True when the PowerShell parser process completed successfully.</summary>
    public bool ValidationSucceeded { get; set; }
    /// <summary>Number of documented commands inferred from help metadata.</summary>
    public int KnownCommandCount { get; set; }
    /// <summary>Known documented command names discovered from the help source.</summary>
    public string[] KnownCommands { get; set; } = Array.Empty<string>();
    /// <summary>Number of example files discovered for validation.</summary>
    public int FileCount { get; set; }
    /// <summary>Number of example files that parsed without syntax errors.</summary>
    public int ValidSyntaxFileCount { get; set; }
    /// <summary>Number of example files that failed syntax validation.</summary>
    public int InvalidSyntaxFileCount { get; set; }
    /// <summary>Number of example files that reference at least one documented command.</summary>
    public int MatchedFileCount { get; set; }
    /// <summary>Number of example files that do not reference any documented commands.</summary>
    public int UnmatchedFileCount { get; set; }
    /// <summary>Total parse error count across all validated files.</summary>
    public int ParseErrorCount { get; set; }
    /// <summary>True when matched example execution was requested.</summary>
    public bool ExecutionRequested { get; set; }
    /// <summary>True when the execution phase completed without runner-level failures.</summary>
    public bool ExecutionCompleted { get; set; }
    /// <summary>Executable used to run matched examples, when execution was requested.</summary>
    public string? ExecutionExecutable { get; set; }
    /// <summary>Number of example files that were executed.</summary>
    public int ExecutedFileCount { get; set; }
    /// <summary>Number of executed example files that exited successfully.</summary>
    public int PassedExecutionFileCount { get; set; }
    /// <summary>Number of executed example files that exited with failure.</summary>
    public int FailedExecutionFileCount { get; set; }
    /// <summary>Per-file validation results.</summary>
    public WebApiDocsPowerShellExampleFileValidationResult[] Files { get; set; } = Array.Empty<WebApiDocsPowerShellExampleFileValidationResult>();
    /// <summary>Warnings emitted while resolving or validating example files.</summary>
    public string[] Warnings { get; set; } = Array.Empty<string>();
}

/// <summary>Validation result for a single PowerShell example script.</summary>
public sealed class WebApiDocsPowerShellExampleFileValidationResult
{
    /// <summary>Full path to the example file.</summary>
    public string FilePath { get; set; } = string.Empty;
    /// <summary>True when the script parsed without syntax errors.</summary>
    public bool ValidSyntax { get; set; }
    /// <summary>Number of parse errors returned by the PowerShell parser.</summary>
    public int ParseErrorCount { get; set; }
    /// <summary>Short parse error messages returned by the PowerShell parser.</summary>
    public string[] ParseErrors { get; set; } = Array.Empty<string>();
    /// <summary>Command tokens discovered in the example file.</summary>
    public string[] Commands { get; set; } = Array.Empty<string>();
    /// <summary>Documented command names matched by this example file.</summary>
    public string[] MatchedCommands { get; set; } = Array.Empty<string>();
    /// <summary>True when the execution phase attempted to run this file.</summary>
    public bool ExecutionAttempted { get; set; }
    /// <summary>True when the executed example exited with code 0; null when not executed.</summary>
    public bool? ExecutionSucceeded { get; set; }
    /// <summary>Exit code returned by the example process, when executed.</summary>
    public int? ExecutionExitCode { get; set; }
    /// <summary>Captured standard output from execution, when available.</summary>
    public string? ExecutionStdOut { get; set; }
    /// <summary>Captured standard error from execution, when available.</summary>
    public string? ExecutionStdErr { get; set; }
    /// <summary>Reason this file was skipped during execution, when execution was requested but not attempted.</summary>
    public string? ExecutionSkippedReason { get; set; }
    /// <summary>Path to the written execution transcript artifact, when report writing emitted one.</summary>
    public string? ExecutionArtifactPath { get; set; }
}
