#pragma warning disable 1591
namespace PowerForge;

public enum RepositoryArchitectureIssueSeverity
{
    Warning,
    Error
}

public sealed class RepositoryArchitectureIssue
{
    public RepositoryArchitectureIssueSeverity Severity { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Path { get; set; }
    public string? CapabilityId { get; set; }
    public string? ProjectId { get; set; }
}

public sealed class RepositoryArchitectureProject
{
    public string Id { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public bool IsTestProject { get; set; }
    public string[] ProjectReferences { get; set; } = Array.Empty<string>();
    public string[] PackageReferences { get; set; } = Array.Empty<string>();
    public string[] ReverseProjectReferences { get; set; } = Array.Empty<string>();
}

public sealed class RepositoryArchitectureCapabilityResult
{
    public string Id { get; set; } = string.Empty;
    public bool Impacted { get; set; }
    public string[] OwnerProjects { get; set; } = Array.Empty<string>();
    public string[] DeclaredConsumerProjects { get; set; } = Array.Empty<string>();
    public string[] ObservedConsumerProjects { get; set; } = Array.Empty<string>();
    public string[] RequiredEvidenceIds { get; set; } = Array.Empty<string>();
    public string[] RequiredValidationStepIds { get; set; } = Array.Empty<string>();
}

public sealed class RepositoryArchitectureReport
{
    public bool Succeeded { get; set; }
    public string RepositoryRoot { get; set; } = string.Empty;
    public string? ConfigPath { get; set; }
    public string[] ChangedFiles { get; set; } = Array.Empty<string>();
    public RepositoryArchitectureProject[] Projects { get; set; } = Array.Empty<RepositoryArchitectureProject>();
    public RepositoryArchitectureCapabilityResult[] Capabilities { get; set; } = Array.Empty<RepositoryArchitectureCapabilityResult>();
    public string[] RequiredValidationStepIds { get; set; } = Array.Empty<string>();
    public RepositoryArchitectureIssue[] Issues { get; set; } = Array.Empty<RepositoryArchitectureIssue>();
}

public sealed class RepositoryArchitectureExecutionResult
{
    public RepositoryArchitectureReport Architecture { get; set; } = new();
    public WorkspaceValidationPlan? ValidationPlan { get; set; }
    public WorkspaceValidationResult? Validation { get; set; }
    public bool Succeeded => Architecture.Succeeded && (Validation?.Succeeded ?? true);
}
#pragma warning restore 1591
