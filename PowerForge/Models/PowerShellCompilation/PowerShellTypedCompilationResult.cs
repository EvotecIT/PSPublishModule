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

    /// <summary>Creates a compiled-method description using the original host-capability contract.</summary>
    public PowerShellCompiledMethod(
        string sourceName,
        string generatedName,
        string returnType,
        PowerShellCompilationParameter[] parameters,
        int sourceLine,
        string? sourcePath,
        bool requiresPowerShellStreams,
        bool requiresPowerShellCommandRegions,
        string[]? aliases,
        bool requiresPowerShellBoundParameters,
        bool isAdvancedFunction)
        : this(
            sourceName,
            generatedName,
            returnType,
            parameters,
            sourceLine,
            sourcePath,
            requiresPowerShellStreams,
            requiresPowerShellCommandRegions,
            aliases,
            requiresPowerShellBoundParameters,
            isAdvancedFunction,
            null,
            false,
            string.Empty)
    {
    }

    /// <summary>Creates a compiled-method description with command binding metadata.</summary>
    public PowerShellCompiledMethod(
        string sourceName,
        string generatedName,
        string returnType,
        PowerShellCompilationParameter[] parameters,
        int sourceLine,
        string? sourcePath,
        bool requiresPowerShellStreams,
        bool requiresPowerShellCommandRegions,
        string[]? aliases,
        bool requiresPowerShellBoundParameters,
        bool isAdvancedFunction,
        PowerShellCompilationCommandBinding? commandBinding)
        : this(sourceName, generatedName, returnType, parameters, sourceLine, sourcePath,
            requiresPowerShellStreams, requiresPowerShellCommandRegions, aliases, requiresPowerShellBoundParameters,
            isAdvancedFunction, commandBinding, false, string.Empty)
    {
    }

    /// <summary>Creates a compiled-method description with runtime-state requirements.</summary>
    public PowerShellCompiledMethod(
        string sourceName,
        string generatedName,
        string returnType,
        PowerShellCompilationParameter[] parameters,
        int sourceLine,
        string? sourcePath,
        bool requiresPowerShellStreams,
        bool requiresPowerShellCommandRegions = false,
        string[]? aliases = null,
        bool requiresPowerShellBoundParameters = false,
        bool isAdvancedFunction = false,
        PowerShellCompilationCommandBinding? commandBinding = null,
        bool requiresPowerShellRuntimeState = false)
        : this(sourceName, generatedName, returnType, parameters, sourceLine, sourcePath,
            requiresPowerShellStreams, requiresPowerShellCommandRegions, aliases, requiresPowerShellBoundParameters,
            isAdvancedFunction, commandBinding, requiresPowerShellRuntimeState, string.Empty)
    {
    }

    /// <summary>Creates a complete compiled-method description.</summary>
    public PowerShellCompiledMethod(
        string sourceName,
        string generatedName,
        string returnType,
        PowerShellCompilationParameter[] parameters,
        int sourceLine,
        string? sourcePath,
        bool requiresPowerShellStreams,
        bool requiresPowerShellCommandRegions,
        string[]? aliases,
        bool requiresPowerShellBoundParameters,
        bool isAdvancedFunction,
        PowerShellCompilationCommandBinding? commandBinding,
        bool requiresPowerShellRuntimeState,
        string? declaredOutputType,
        int sourceColumn = 1,
        int sourceEndLine = 0,
        int sourceEndColumn = 0,
        PowerShellCompilationSourceMapEntry[]? sourceMap = null,
        PowerShellCompilationCommandProviderContract[]? commandProviders = null)
    {
        SourceName = sourceName ?? string.Empty;
        GeneratedName = generatedName ?? string.Empty;
        ReturnType = returnType ?? string.Empty;
        Parameters = parameters ?? Array.Empty<PowerShellCompilationParameter>();
        SourceLine = sourceLine;
        SourceColumn = sourceColumn;
        SourceEndLine = sourceEndLine > 0 ? sourceEndLine : sourceLine;
        SourceEndColumn = sourceEndColumn > 0 ? sourceEndColumn : sourceColumn;
        SourceMap = sourceMap ?? Array.Empty<PowerShellCompilationSourceMapEntry>();
        CommandProviders = commandProviders ?? Array.Empty<PowerShellCompilationCommandProviderContract>();
        SourcePath = sourcePath ?? string.Empty;
        RequiresPowerShellStreams = requiresPowerShellStreams;
        RequiresPowerShellCommandRegions = requiresPowerShellCommandRegions;
        Aliases = aliases ?? Array.Empty<string>();
        RequiresPowerShellBoundParameters = requiresPowerShellBoundParameters;
        IsAdvancedFunction = isAdvancedFunction;
        CommandBinding = commandBinding ?? new PowerShellCompilationCommandBinding(isAdvancedFunction);
        RequiresPowerShellRuntimeState = requiresPowerShellRuntimeState;
        DeclaredOutputType = declaredOutputType ?? string.Empty;
    }

    /// <summary>Original PowerShell function name.</summary>
    public string SourceName { get; }

    /// <summary>Generated C# method name.</summary>
    public string GeneratedName { get; }

    /// <summary>Resolved CLR return type name.</summary>
    public string ReturnType { get; }

    /// <summary>Authored OutputType metadata, or an empty string when none is declared.</summary>
    public string DeclaredOutputType { get; }

    /// <summary>Typed method parameters.</summary>
    public PowerShellCompilationParameter[] Parameters { get; }

    /// <summary>One-based source line of the PowerShell function body.</summary>
    public int SourceLine { get; }

    /// <summary>One-based source column where the PowerShell function begins.</summary>
    public int SourceColumn { get; }

    /// <summary>One-based source line where the PowerShell function ends.</summary>
    public int SourceEndLine { get; }

    /// <summary>One-based source column where the PowerShell function ends.</summary>
    public int SourceEndColumn { get; }

    /// <summary>Statement-level source spans and method-relative generated C# ranges.</summary>
    public PowerShellCompilationSourceMapEntry[] SourceMap { get; }

    /// <summary>Versioned command semantic providers used by the generated method.</summary>
    public PowerShellCompilationCommandProviderContract[] CommandProviders { get; }

    /// <summary>Full path of the authored PowerShell file containing the function.</summary>
    public string SourcePath { get; }

    /// <summary>Whether the generated method expects PSCmdlet stream delegates.</summary>
    public bool RequiresPowerShellStreams { get; }

    /// <summary>Whether adjacent command statements are dispatched as one PowerShell runtime region.</summary>
    public bool RequiresPowerShellCommandRegions { get; }

    /// <summary>Whether the generated method expects bounded PowerShell runtime-state delegates and values.</summary>
    public bool RequiresPowerShellRuntimeState { get; }

    /// <summary>Whether the generated method expects the names of explicitly bound PowerShell parameters.</summary>
    public bool RequiresPowerShellBoundParameters { get; }

    /// <summary>PowerShell command aliases declared on the original function.</summary>
    public string[] Aliases { get; }

    /// <summary>Whether the source function uses advanced-function parameter binding.</summary>
    public bool IsAdvancedFunction { get; }

    /// <summary>Advanced-function and positional binding behavior preserved for the generated command.</summary>
    public PowerShellCompilationCommandBinding CommandBinding { get; }

    /// <summary>Authored comment-based help retained for the compiled command.</summary>
    public PowerShellCompilationHelp? Help { get; internal set; }
}

/// <summary>Maps one lowered PowerShell statement to a generated C# range.</summary>
public sealed class PowerShellCompilationSourceMapEntry
{
    /// <summary>Creates one statement-level source-map entry.</summary>
    public PowerShellCompilationSourceMapEntry(
        int sourceStartLine,
        int sourceStartColumn,
        int sourceEndLine,
        int sourceEndColumn,
        int generatedStartLine,
        int generatedStartColumn,
        int generatedEndLine,
        int generatedEndColumn)
    {
        SourceStartLine = sourceStartLine;
        SourceStartColumn = sourceStartColumn;
        SourceEndLine = sourceEndLine;
        SourceEndColumn = sourceEndColumn;
        GeneratedStartLine = generatedStartLine;
        GeneratedStartColumn = generatedStartColumn;
        GeneratedEndLine = generatedEndLine;
        GeneratedEndColumn = generatedEndColumn;
    }

    /// <summary>One-based authored start line.</summary>
    public int SourceStartLine { get; }

    /// <summary>One-based authored start column.</summary>
    public int SourceStartColumn { get; }

    /// <summary>One-based authored end line.</summary>
    public int SourceEndLine { get; }

    /// <summary>One-based authored end column.</summary>
    public int SourceEndColumn { get; }

    /// <summary>One-based generated start line relative to the generated method.</summary>
    public int GeneratedStartLine { get; }

    /// <summary>One-based generated start column.</summary>
    public int GeneratedStartColumn { get; }

    /// <summary>One-based generated end line relative to the generated method.</summary>
    public int GeneratedEndLine { get; }

    /// <summary>One-based generated end column.</summary>
    public int GeneratedEndColumn { get; }
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
