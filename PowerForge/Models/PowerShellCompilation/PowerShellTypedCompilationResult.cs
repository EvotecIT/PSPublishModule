using System;

namespace PowerForge;

/// <summary>
/// Describes one PowerShell function translated to a typed CLR method.
/// </summary>
public sealed class PowerShellCompiledMethod
{
    /// <summary>Creates a compiled-method description.</summary>
    public PowerShellCompiledMethod(string sourceName, string generatedName, string returnType, PowerShellCompilationParameter[] parameters, int sourceLine)
        : this(sourceName, generatedName, returnType, parameters, sourceLine, null)
    {
    }

    /// <summary>Creates a compiled-method description with authored file identity.</summary>
    public PowerShellCompiledMethod(string sourceName, string generatedName, string returnType, PowerShellCompilationParameter[] parameters, int sourceLine, string? sourcePath)
        : this(sourceName, generatedName, returnType, parameters, sourceLine, sourcePath, false)
    {
    }

    /// <summary>Creates a compiled-method description with authored file identity and host capability requirements.</summary>
    public PowerShellCompiledMethod(
        string sourceName,
        string generatedName,
        string returnType,
        PowerShellCompilationParameter[] parameters,
        int sourceLine,
        string? sourcePath,
        bool requiresPowerShellStreams,
        bool requiresPowerShellCommandRegions = false)
    {
        SourceName = sourceName ?? string.Empty;
        GeneratedName = generatedName ?? string.Empty;
        ReturnType = returnType ?? string.Empty;
        Parameters = parameters ?? Array.Empty<PowerShellCompilationParameter>();
        SourceLine = sourceLine;
        SourcePath = sourcePath ?? string.Empty;
        RequiresPowerShellStreams = requiresPowerShellStreams;
        RequiresPowerShellCommandRegions = requiresPowerShellCommandRegions;
    }

    /// <summary>Original PowerShell function name.</summary>
    public string SourceName { get; }

    /// <summary>Generated C# method name.</summary>
    public string GeneratedName { get; }

    /// <summary>Resolved CLR return type name.</summary>
    public string ReturnType { get; }

    /// <summary>Typed method parameters.</summary>
    public PowerShellCompilationParameter[] Parameters { get; }

    /// <summary>One-based source line of the PowerShell function.</summary>
    public int SourceLine { get; }

    /// <summary>Full path of the authored PowerShell file containing the function.</summary>
    public string SourcePath { get; }

    /// <summary>Whether the generated method expects PSCmdlet stream delegates.</summary>
    public bool RequiresPowerShellStreams { get; }

    /// <summary>Whether adjacent command statements are dispatched as one PowerShell runtime region.</summary>
    public bool RequiresPowerShellCommandRegions { get; }
}

/// <summary>
/// Generated C# source and evidence produced by typed PowerShell transpilation.
/// </summary>
public sealed class PowerShellTypedCompilationResult
{
    /// <summary>Creates a typed-compilation result.</summary>
    public PowerShellTypedCompilationResult(
        string sourcePath,
        string namespaceName,
        string typeName,
        string sourceCode,
        PowerShellCompiledMethod[] methods,
        PowerShellCompilationDiagnostic[] diagnostics)
        : this(sourcePath, namespaceName, typeName, sourceCode, methods, diagnostics, null)
    {
    }

    /// <summary>Creates a typed-compilation result with every authored source file.</summary>
    public PowerShellTypedCompilationResult(
        string sourcePath,
        string namespaceName,
        string typeName,
        string sourceCode,
        PowerShellCompiledMethod[] methods,
        PowerShellCompilationDiagnostic[] diagnostics,
        string[]? sourcePaths)
    {
        SourcePath = sourcePath ?? string.Empty;
        NamespaceName = namespaceName ?? string.Empty;
        TypeName = typeName ?? string.Empty;
        SourceCode = sourceCode ?? string.Empty;
        Methods = methods ?? Array.Empty<PowerShellCompiledMethod>();
        Diagnostics = diagnostics ?? Array.Empty<PowerShellCompilationDiagnostic>();
        SourcePaths = sourcePaths ?? (string.IsNullOrWhiteSpace(SourcePath) ? Array.Empty<string>() : new[] { SourcePath });
    }

    /// <summary>Full PowerShell source path.</summary>
    public string SourcePath { get; }

    /// <summary>Generated C# namespace.</summary>
    public string NamespaceName { get; }

    /// <summary>Generated static CLR type name.</summary>
    public string TypeName { get; }

    /// <summary>Complete generated C# source.</summary>
    public string SourceCode { get; }

    /// <summary>Successfully translated methods.</summary>
    public PowerShellCompiledMethod[] Methods { get; }

    /// <summary>Structural or semantic translation blockers.</summary>
    public PowerShellCompilationDiagnostic[] Diagnostics { get; }

    /// <summary>All authored PowerShell files contributing to this generated CLR source.</summary>
    public string[] SourcePaths { get; }

    /// <summary>Whether at least one method was translated and no blockers remain.</summary>
    public bool Success => Methods.Length > 0 && Diagnostics.Length == 0;
}
