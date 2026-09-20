using System;

namespace PowerForge;

/// <summary>Classifies a public CLR ABI compatibility problem.</summary>
public enum PowerShellCompilationAbiCompatibilityIssueKind
{
    /// <summary>The baseline or candidate manifest is not usable.</summary>
    InvalidManifest,
    /// <summary>The generated CLR namespace changed.</summary>
    NamespaceChanged,
    /// <summary>The generated public CLR type name changed.</summary>
    TypeNameChanged,
    /// <summary>The static or stateful module lifetime contract changed.</summary>
    ModuleLifetimeChanged,
    /// <summary>A previously public CLR method is no longer present.</summary>
    MethodRemoved,
    /// <summary>A previously public CLR method retained its name but changed its contract.</summary>
    MethodChanged
}

/// <summary>One incompatible difference between two generated public CLR ABI manifests.</summary>
public sealed class PowerShellCompilationAbiCompatibilityIssue
{
    /// <summary>Creates an ABI compatibility issue.</summary>
    public PowerShellCompilationAbiCompatibilityIssue(
        PowerShellCompilationAbiCompatibilityIssueKind kind,
        string path,
        string message)
    {
        Kind = kind;
        Path = path ?? string.Empty;
        Message = message ?? string.Empty;
    }

    /// <summary>Stable issue classification.</summary>
    public PowerShellCompilationAbiCompatibilityIssueKind Kind { get; }

    /// <summary>Public ABI member or contract path affected by the issue.</summary>
    public string Path { get; }

    /// <summary>Human-readable explanation of the breaking change.</summary>
    public string Message { get; }
}

/// <summary>Result of comparing an earlier generated public ABI with a candidate ABI.</summary>
public sealed class PowerShellCompilationAbiCompatibilityResult
{
    /// <summary>Creates a compatibility result.</summary>
    public PowerShellCompilationAbiCompatibilityResult(PowerShellCompilationAbiCompatibilityIssue[]? issues)
        => Issues = issues ?? Array.Empty<PowerShellCompilationAbiCompatibilityIssue>();

    /// <summary>Whether the candidate preserves every public contract in the baseline.</summary>
    public bool IsCompatible => Issues.Length == 0;

    /// <summary>Breaking changes discovered during comparison.</summary>
    public PowerShellCompilationAbiCompatibilityIssue[] Issues { get; }
}
