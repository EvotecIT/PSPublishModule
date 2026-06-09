#pragma warning disable 1591
namespace PowerForge;

public enum WorkspaceValidationStepKind
{
    DotNet,
    Command
}

public sealed class WorkspaceValidationSpec
{
    public int SchemaVersion { get; set; } = 1;
    public string? ProjectRoot { get; set; }
    public string[] DefaultFeatures { get; set; } = Array.Empty<string>();
    public Dictionary<string, string?> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public WorkspaceValidationProfile[] Profiles { get; set; } = Array.Empty<WorkspaceValidationProfile>();
    public WorkspaceValidationStep[] Steps { get; set; } = Array.Empty<WorkspaceValidationStep>();
}

public sealed class WorkspaceValidationProfile
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string[] Features { get; set; } = Array.Empty<string>();
    public Dictionary<string, string?> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class WorkspaceValidationStep
{
    public string Id { get; set; } = string.Empty;
    public string? Name { get; set; }
    public WorkspaceValidationStepKind Kind { get; set; } = WorkspaceValidationStepKind.DotNet;
    public string? Executable { get; set; }
    public string[] Arguments { get; set; } = Array.Empty<string>();
    public string? WorkingDirectory { get; set; }
    public string[] Profiles { get; set; } = Array.Empty<string>();
    public string[] RequiredFeatures { get; set; } = Array.Empty<string>();
    public string[] Items { get; set; } = Array.Empty<string>();
    public string[] Frameworks { get; set; } = Array.Empty<string>();
    public string? RequiredPath { get; set; }
    public bool ContinueOnMissingRequiredPath { get; set; }
    public Dictionary<string, string?> EnvironmentVariables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string? FailureContext { get; set; }
    public string? FailureHint { get; set; }
}

public sealed class WorkspaceValidationRequest
{
    public string ProfileName { get; set; } = "default";
    public string Configuration { get; set; } = "Release";
    public string? TestimoXRoot { get; set; }
    public string[] EnabledFeatures { get; set; } = Array.Empty<string>();
    public string[] DisabledFeatures { get; set; } = Array.Empty<string>();
    public Dictionary<string, string?> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool CaptureOutput { get; set; }
    public bool CaptureError { get; set; }
}

public sealed class WorkspaceValidationProfileSummary
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string[] Features { get; set; } = Array.Empty<string>();
}

public sealed class WorkspaceValidationPreparedStep
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public WorkspaceValidationStepKind Kind { get; set; }
    public string Executable { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public string[] Arguments { get; set; } = Array.Empty<string>();
    public string DisplayCommand { get; set; } = string.Empty;
    public string? FailureContext { get; set; }
    public string? FailureHint { get; set; }
    public string? RequiredPath { get; set; }
    public bool ContinueOnMissingRequiredPath { get; set; }
    public Dictionary<string, string?> EnvironmentVariables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class WorkspaceValidationPlan
{
    public string ProjectRoot { get; set; } = string.Empty;
    public string ProfileName { get; set; } = string.Empty;
    public string Configuration { get; set; } = string.Empty;
    public string[] ActiveFeatures { get; set; } = Array.Empty<string>();
    public WorkspaceValidationPreparedStep[] Steps { get; set; } = Array.Empty<WorkspaceValidationPreparedStep>();
}

public sealed class WorkspaceValidationStepResult
{
    public WorkspaceValidationPreparedStep Step { get; set; } = new();
    public bool Succeeded { get; set; }
    public bool Skipped { get; set; }
    public int ExitCode { get; set; }
    public string? SkipReason { get; set; }
    public string? StdOut { get; set; }
    public string? StdErr { get; set; }
}

public sealed class WorkspaceValidationResult
{
    public WorkspaceValidationPlan Plan { get; set; } = new();
    public bool Succeeded { get; set; }
    public string? ErrorMessage { get; set; }
    public WorkspaceValidationStepResult[] Steps { get; set; } = Array.Empty<WorkspaceValidationStepResult>();
}
#pragma warning restore 1591
