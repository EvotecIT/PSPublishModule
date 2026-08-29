namespace PowerForge;

/// <summary>
/// Configures conversion of an authored PowerShell module into a generated binary module during <c>Build-Module</c>.
/// </summary>
public sealed class PowerShellModuleCompilationConfiguration
{
    /// <summary>Whether module compilation is enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>Whether unsupported units are retained as explicit PowerShell fallback or rejected.</summary>
    public PowerShellCompilationMode Mode { get; set; } = PowerShellCompilationMode.Hybrid;

    /// <summary>Target framework used by the generated binary module.</summary>
    public string TargetFramework { get; set; } = "net8.0";

    /// <summary>Policy used to select non-code payload.</summary>
    public PowerShellCompilationResourceMode ResourceMode { get; set; } = PowerShellCompilationResourceMode.Declared;

    /// <summary>Contained module-root resource paths or glob patterns to include.</summary>
    public string[] IncludeResource { get; set; } = System.Array.Empty<string>();

    /// <summary>Contained module-root resource paths or glob patterns to exclude.</summary>
    public string[] ExcludeResource { get; set; } = System.Array.Empty<string>();

    /// <summary>Whether the content-addressed generated-build cache may be used.</summary>
    public bool UseBuildCache { get; set; } = true;

    /// <summary>Optional machine-local root for generated-build cache entries.</summary>
    public string? BuildCacheDirectory { get; set; }

    /// <summary>Reviewed dependency graph that the build must reproduce exactly.</summary>
    public PowerShellCompilationDependencyGraph? DependencyLock { get; set; }

    /// <summary>Explicitly permits dependency resolution without a separately reviewed lock.</summary>
    public bool AllowUnreviewedDependencies { get; set; }

    /// <summary>Maximum time allowed for restore and compilation.</summary>
    public int TimeoutSeconds { get; set; } = 300;
}
