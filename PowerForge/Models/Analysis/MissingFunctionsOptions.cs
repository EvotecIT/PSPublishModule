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
        bool includeFunctionsRecursively = false)
    {
        KnownFunctions = knownFunctions ?? System.Array.Empty<string>();
        ApprovedModules = approvedModules ?? System.Array.Empty<string>();
        IgnoreFunctions = ignoreFunctions ?? System.Array.Empty<string>();
        IncludeFunctionsRecursively = includeFunctionsRecursively;
    }
}

