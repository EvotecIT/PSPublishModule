namespace PowerForge;

/// <summary>Checks final packages and runs product smoke probes without publishing artifacts.</summary>
public sealed class ReleaseValidationSpec
{
    /// <summary>Optional JSON schema location for authoring tools.</summary>
    [System.Text.Json.Serialization.JsonPropertyName("$schema")]
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string? Schema { get; set; }
    /// <summary>Configuration schema version.</summary>
    public int SchemaVersion { get; set; } = 1;
    /// <summary>Root for relative input paths, relative to the configuration file.</summary>
    public string ProjectRoot { get; set; } = ".";
    /// <summary>Optional NuGet package set and its product-owned expectations.</summary>
    public PackageSetValidation? Packages { get; set; }
    /// <summary>Packaged PowerShell modules to inspect and exercise.</summary>
    public ModuleArtifactValidation[] Modules { get; set; } = Array.Empty<ModuleArtifactValidation>();
    /// <summary>Optional CLI publish manifest and expected target matrix.</summary>
    public CliArtifactValidation? CliArtifacts { get; set; }
    /// <summary>Installed .NET tool probes.</summary>
    public DotNetToolValidation[] Tools { get; set; } = Array.Empty<DotNetToolValidation>();
    /// <summary>Isolated projects that consume the staged packages.</summary>
    public PackageConsumerValidation[] Consumers { get; set; } = Array.Empty<PackageConsumerValidation>();
    /// <summary>Additional product commands to run through the shared process runner.</summary>
    public ReleaseCommandValidation[] Commands { get; set; } = Array.Empty<ReleaseCommandValidation>();
}

/// <summary>Caller-provided locations and version for a validation run.</summary>
public sealed class ReleaseValidationRequest
{
    /// <summary>Optional project-root override.</summary>
    public string? ProjectRoot { get; set; }
    /// <summary>Optional expected release version; otherwise obtained from the artifacts unless Packages.SameVersion is false.</summary>
    public string? Version { get; set; }
    /// <summary>Named path/value overrides used by configuration tokens.</summary>
    public Dictionary<string, string> Variables { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Optional authoritative staged asset list to compare with a unified CLI manifest.</summary>
    public string[]? StagedAssets { get; set; }

    /// <summary>Release-owned effective publish plan, including execution-time matrix selection.</summary>
    internal DotNetPublishPlan? PublishPlan { get; set; }
}

/// <summary>Artifact and runtime validation evidence.</summary>
public sealed class ReleaseValidationReport
{
    /// <summary>Whether every selected check passed.</summary>
    public bool Success => Errors.Count == 0;
    /// <summary>Release-wide artifact version; empty for a mixed-version run without a caller-supplied version.</summary>
    public string Version { get; set; } = string.Empty;
    /// <summary>Completed check descriptions.</summary>
    public List<string> Checks { get; } = new();
    /// <summary>Actionable validation failures.</summary>
    public List<string> Errors { get; } = new();
}
