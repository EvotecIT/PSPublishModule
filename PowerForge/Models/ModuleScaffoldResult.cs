namespace PowerForge;

/// <summary>
/// Result of scaffolding a new module project.
/// </summary>
public sealed class ModuleScaffoldResult
{
    /// <summary>Project root that was checked/created.</summary>
    public string ProjectRoot { get; }

    /// <summary>True when the scaffold was created; false when it already existed.</summary>
    public bool Created { get; }

    /// <summary>Generated module GUID when created.</summary>
    public string? ModuleGuid { get; }

    /// <summary>
    /// Creates a new result instance.
    /// </summary>
    public ModuleScaffoldResult(string projectRoot, bool created, string? moduleGuid)
    {
        ProjectRoot = projectRoot;
        Created = created;
        ModuleGuid = moduleGuid;
    }
}

