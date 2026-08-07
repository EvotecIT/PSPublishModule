using System.Text.Json.Serialization;

#pragma warning disable 1591
namespace PowerForge;

public sealed class RepositoryArchitectureSpec
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; set; }
    [JsonRequired]
    public int SchemaVersion { get; set; }
    public string? RepositoryRoot { get; set; }
    public string? WorkspaceValidationConfig { get; set; }
    public string WorkspaceValidationProfile { get; set; } = "architecture";
    public string[] GlobalImpactPaths { get; set; } = Array.Empty<string>();
    public RepositoryArchitectureProjectRule[] ProjectRules { get; set; } = Array.Empty<RepositoryArchitectureProjectRule>();
    public RepositoryArchitectureCapability[] Capabilities { get; set; } = Array.Empty<RepositoryArchitectureCapability>();
}

public sealed class RepositoryArchitectureProjectRule
{
    [JsonRequired]
    public string Id { get; set; } = string.Empty;
    [JsonRequired]
    public string Project { get; set; } = string.Empty;
    public string[]? AllowedProjectReferences { get; set; }
    public string[] RequiredProjectReferences { get; set; } = Array.Empty<string>();
    public string[] ForbiddenProjectReferences { get; set; } = Array.Empty<string>();
    public string[]? AllowedPackageReferences { get; set; }
    public string[] RequiredPackageReferences { get; set; } = Array.Empty<string>();
    public string[] ForbiddenPackageReferences { get; set; } = Array.Empty<string>();
}

public sealed class RepositoryArchitectureCapability
{
    [JsonRequired]
    public string Id { get; set; } = string.Empty;
    public string? Description { get; set; }
    [JsonRequired]
    public string[] OwnerProjects { get; set; } = Array.Empty<string>();
    [JsonRequired]
    public string[] OwnerPaths { get; set; } = Array.Empty<string>();
    [JsonRequired]
    public string[] ConsumerProjects { get; set; } = Array.Empty<string>();
    public string[] IgnoredUsageProjects { get; set; } = Array.Empty<string>();
    public string[] UsagePatterns { get; set; } = Array.Empty<string>();
    public string[] UsagePathIncludes { get; set; } = ["**/*.cs"];
    public string[] UsagePathExcludes { get; set; } = ["**/bin/**", "**/obj/**"];
    public bool UsagePatternCaseSensitive { get; set; } = true;
    public bool RequireObservedConsumers { get; set; } = true;
    public string[] RequiredEvidenceKinds { get; set; } = Array.Empty<string>();
    [JsonRequired]
    public RepositoryArchitectureEvidence[] Evidence { get; set; } = Array.Empty<RepositoryArchitectureEvidence>();
}

public sealed class RepositoryArchitectureEvidence
{
    [JsonRequired]
    public string Id { get; set; } = string.Empty;
    [JsonRequired]
    public string Kind { get; set; } = string.Empty;
    [JsonRequired]
    public string StepId { get; set; } = string.Empty;
    public string? Path { get; set; }
    [JsonRequired]
    public string[] CoversProjects { get; set; } = Array.Empty<string>();
}
#pragma warning restore 1591
