namespace PowerForge;

/// <summary>
/// Stable diagnostic codes emitted by the PowerShell compilation planner.
/// </summary>
public enum PowerShellCompilationDiagnosticCode
{
    /// <summary>The input path does not exist or cannot be read.</summary>
    InputError,

    /// <summary>The PowerShell parser rejected the source.</summary>
    ParseError,

    /// <summary>A parameter does not declare a supported static CLR type.</summary>
    UnsupportedParameterType,

    /// <summary>The unit contains a PowerShell command invocation.</summary>
    CommandInvocation,

    /// <summary>The unit contains a dynamically resolved command invocation.</summary>
    DynamicCommandInvocation,

    /// <summary>The unit contains a nested script block or script-block literal.</summary>
    ScriptBlock,

    /// <summary>The unit reads or writes dynamic PowerShell runtime state.</summary>
    RuntimeScope,

    /// <summary>The unit contains an AST construct not implemented by the typed compiler.</summary>
    UnsupportedSyntax,

    /// <summary>The unit uses an operator not implemented by the typed compiler.</summary>
    UnsupportedOperator
}

/// <summary>
/// Describes why a PowerShell source unit can or cannot be compiled.
/// </summary>
public sealed class PowerShellCompilationDiagnostic
{
    /// <summary>Creates a compilation diagnostic.</summary>
    public PowerShellCompilationDiagnostic(
        PowerShellCompilationDiagnosticCode code,
        string message,
        string filePath,
        int line,
        int column,
        string? featureId = null)
    {
        Code = code;
        Message = message ?? string.Empty;
        FilePath = filePath ?? string.Empty;
        Line = line;
        Column = column;
        FeatureId = PowerShellCompilationFeatureIds.Resolve(code, Message, featureId);
    }

    /// <summary>Stable diagnostic code.</summary>
    public PowerShellCompilationDiagnosticCode Code { get; }

    /// <summary>Human-readable explanation.</summary>
    public string Message { get; }

    /// <summary>Full source path.</summary>
    public string FilePath { get; }

    /// <summary>One-based source line, or zero when unavailable.</summary>
    public int Line { get; }

    /// <summary>One-based source column, or zero when unavailable.</summary>
    public int Column { get; }

    /// <summary>Stable compiler-capability identifier used for coverage planning.</summary>
    public string FeatureId { get; }
}
