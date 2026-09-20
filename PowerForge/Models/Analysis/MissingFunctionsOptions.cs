namespace PowerForge;

/// <summary>
/// Options controlling missing-function analysis and helper inlining.
/// </summary>
public sealed class MissingFunctionsOptions
{
    /// <summary>Known functions that should be treated as already available.</summary>
    public string[] KnownFunctions { get; }

    /// <summary>
    /// Approved module names that are allowed sources for pulling inline helper function definitions.
    /// </summary>
    public string[] ApprovedModules { get; }

    /// <summary>
    /// Concrete paths selected for approved modules. When present, analysis imports from these paths instead of
    /// resolving the module by name from the current session or <c>PSModulePath</c>.
    /// </summary>
    public ApprovedModuleSource[] ApprovedModuleSources { get; }

    /// <summary>
    /// Concrete paths selected for declared runtime dependencies. Commands exported by these modules are treated as
    /// externally satisfied, but their function bodies are never returned for inlining.
    /// </summary>
    public ApprovedModuleSource[] RuntimeModuleSources { get; }

    /// <summary>
    /// Whether every approved module must have a concrete source binding before it can contribute inline helpers.
    /// </summary>
    public bool RequireApprovedModuleSources { get; }

    /// <summary>Function/command names to ignore when computing the missing set.</summary>
    public string[] IgnoreFunctions { get; }

    /// <summary>Whether helper functions should be inlined recursively.</summary>
    public bool IncludeFunctionsRecursively { get; }

    /// <summary>
    /// Creates a new <see cref="MissingFunctionsOptions"/> instance.
    /// </summary>
    public MissingFunctionsOptions(
        string[]? knownFunctions = null,
        string[]? approvedModules = null,
        string[]? ignoreFunctions = null,
        bool includeFunctionsRecursively = false,
        ApprovedModuleSource[]? approvedModuleSources = null,
        bool requireApprovedModuleSources = false,
        ApprovedModuleSource[]? runtimeModuleSources = null)
    {
        KnownFunctions = knownFunctions ?? System.Array.Empty<string>();
        ApprovedModules = approvedModules ?? System.Array.Empty<string>();
        ApprovedModuleSources = approvedModuleSources ?? System.Array.Empty<ApprovedModuleSource>();
        RuntimeModuleSources = runtimeModuleSources ?? System.Array.Empty<ApprovedModuleSource>();
        RequireApprovedModuleSources = requireApprovedModuleSources;
        IgnoreFunctions = ignoreFunctions ?? System.Array.Empty<string>();
        IncludeFunctionsRecursively = includeFunctionsRecursively;
    }
}
