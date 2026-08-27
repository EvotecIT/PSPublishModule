namespace PowerForge;

internal enum PowerShellSymbolKind
{
    SourceDocument,
    Function,
    Parameter,
    Local,
    PipelineVariable,
    GeneratedCommand
}

/// <summary>
/// Stable semantic identity derived from canonical declaration facts rather than traversal order.
/// </summary>
internal sealed class PowerShellSymbolId : IEquatable<PowerShellSymbolId>
{
    internal PowerShellSymbolId(PowerShellSymbolKind kind, string documentId, string name, SourceSpan declaration)
    {
        Kind = kind;
        DocumentId = documentId ?? string.Empty;
        Name = name ?? string.Empty;
        Declaration = declaration;
        StableKey = string.Concat(
            kind.ToString(), ":", DocumentId, ":", declaration.StartOffset.ToString(System.Globalization.CultureInfo.InvariantCulture), ":", Name.ToUpperInvariant());
    }

    internal PowerShellSymbolKind Kind { get; }
    internal string DocumentId { get; }
    internal string Name { get; }
    internal SourceSpan Declaration { get; }
    internal string StableKey { get; }

    public bool Equals(PowerShellSymbolId? other)
        => other is not null && string.Equals(StableKey, other.StableKey, StringComparison.Ordinal);

    public override bool Equals(object? obj) => Equals(obj as PowerShellSymbolId);
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(StableKey);
}

internal enum PowerShellTypeFactProvenance
{
    Explicit,
    Literal,
    Inferred,
    CommandContract,
    Widened,
    Unknown
}

internal sealed class PowerShellTypeFact
{
    internal PowerShellTypeFact(Type clrType, PowerShellTypeFactProvenance provenance, string explanation)
    {
        ClrType = clrType ?? throw new ArgumentNullException(nameof(clrType));
        Provenance = provenance;
        Explanation = explanation ?? string.Empty;
    }

    internal Type ClrType { get; }
    internal PowerShellTypeFactProvenance Provenance { get; }
    internal string Explanation { get; }

    internal static PowerShellTypeFact Unknown { get; } = new(typeof(object), PowerShellTypeFactProvenance.Unknown, "No static type fact is available.");
}

internal enum PowerShellValueState
{
    Known,
    Null,
    Missing,
    AutomationNull,
    Unknown
}

internal enum PowerShellOutputCardinality
{
    None,
    Scalar,
    Collection,
    Unknown
}

[Flags]
internal enum PowerShellSemanticEffect
{
    None = 0,
    SuccessOutput = 1,
    NonSuccessStream = 2,
    Mutation = 4,
    FileSystem = 8,
    Network = 16,
    Process = 32,
    Host = 64
}

[Flags]
internal enum PowerShellRequiredCapability
{
    None = 0,
    PowerShellHost = 1,
    CommandRegion = 2,
    FileSystem = 4,
    Network = 8,
    NativeProcess = 16,
    Com = 32,
    Reflection = 64
}

internal enum PowerShellExecutionDispositionKind
{
    Typed,
    Hosted,
    Fallback,
    Rejected
}

internal sealed class PowerShellExecutionDisposition
{
    internal PowerShellExecutionDisposition(PowerShellExecutionDispositionKind kind, string reasonCode, string explanation)
    {
        Kind = kind;
        ReasonCode = reasonCode ?? string.Empty;
        Explanation = explanation ?? string.Empty;
    }

    internal PowerShellExecutionDispositionKind Kind { get; }
    internal string ReasonCode { get; }
    internal string Explanation { get; }

    internal static PowerShellExecutionDisposition Typed { get; } = new(PowerShellExecutionDispositionKind.Typed, string.Empty, string.Empty);
}

internal abstract class PowerShellBoundNode
{
    protected PowerShellBoundNode(
        SourceSpan span,
        PowerShellTypeFact type,
        PowerShellValueState valueState,
        PowerShellOutputCardinality cardinality,
        PowerShellSemanticEffect effects,
        PowerShellRequiredCapability capabilities,
        PowerShellExecutionDisposition disposition)
    {
        Span = span;
        Type = type;
        ValueState = valueState;
        Cardinality = cardinality;
        Effects = effects;
        Capabilities = capabilities;
        Disposition = disposition;
    }

    internal SourceSpan Span { get; }
    internal PowerShellTypeFact Type { get; }
    internal PowerShellValueState ValueState { get; }
    internal PowerShellOutputCardinality Cardinality { get; }
    internal PowerShellSemanticEffect Effects { get; }
    internal PowerShellRequiredCapability Capabilities { get; }
    internal PowerShellExecutionDisposition Disposition { get; }
}

internal abstract class PowerShellBoundStatement : PowerShellBoundNode
{
    protected PowerShellBoundStatement(SourceSpan span, PowerShellSemanticEffect effects)
        : base(span, new PowerShellTypeFact(typeof(void), PowerShellTypeFactProvenance.Inferred, "Statements do not produce a CLR value."), PowerShellValueState.Known, PowerShellOutputCardinality.None, effects, PowerShellRequiredCapability.None, PowerShellExecutionDisposition.Typed)
    {
    }
}

internal abstract class PowerShellBoundExpression : PowerShellBoundNode
{
    protected PowerShellBoundExpression(SourceSpan span, PowerShellTypeFact type, PowerShellValueState valueState)
        : base(span, type, valueState, PowerShellOutputCardinality.Scalar, PowerShellSemanticEffect.None, PowerShellRequiredCapability.None, PowerShellExecutionDisposition.Typed)
    {
    }
}

internal sealed class PowerShellBoundLiteralExpression : PowerShellBoundExpression
{
    internal PowerShellBoundLiteralExpression(SourceSpan span, object? value, PowerShellTypeFact type, PowerShellValueState valueState)
        : base(span, type, valueState)
    {
        Value = value;
    }

    internal object? Value { get; }
}

internal sealed class PowerShellBoundVariableExpression : PowerShellBoundExpression
{
    internal PowerShellBoundVariableExpression(SourceSpan span, PowerShellSymbolId symbol, PowerShellTypeFact type)
        : base(span, type, PowerShellValueState.Unknown)
    {
        Symbol = symbol;
    }

    internal PowerShellSymbolId Symbol { get; }
}

internal sealed class PowerShellBoundReturnStatement : PowerShellBoundStatement
{
    internal PowerShellBoundReturnStatement(SourceSpan span, PowerShellBoundExpression? expression)
        : base(span, expression is null ? PowerShellSemanticEffect.None : PowerShellSemanticEffect.SuccessOutput)
    {
        Expression = expression;
    }

    internal PowerShellBoundExpression? Expression { get; }
}

internal sealed class PowerShellBoundExpressionStatement : PowerShellBoundStatement
{
    internal PowerShellBoundExpressionStatement(SourceSpan span, PowerShellBoundExpression expression)
        : base(span, PowerShellSemanticEffect.SuccessOutput)
    {
        Expression = expression;
    }

    internal PowerShellBoundExpression Expression { get; }
}

internal sealed class PowerShellBoundParameter
{
    internal PowerShellBoundParameter(PowerShellSymbolId symbol, PowerShellTypeFact type)
    {
        Symbol = symbol;
        Type = type;
    }

    internal PowerShellSymbolId Symbol { get; }
    internal PowerShellTypeFact Type { get; }
}

internal sealed class PowerShellBoundBlock
{
    internal PowerShellBoundBlock(SourceSpan span, PowerShellBoundStatement[] statements)
    {
        Span = span;
        Statements = statements ?? Array.Empty<PowerShellBoundStatement>();
    }

    internal SourceSpan Span { get; }
    internal PowerShellBoundStatement[] Statements { get; }
}

internal sealed class PowerShellBoundFunction
{
    internal PowerShellBoundFunction(
        PowerShellSymbolId symbol,
        PowerShellBoundParameter[] parameters,
        PowerShellBoundBlock body,
        PowerShellTypeFact returnType,
        PowerShellSemanticEffect effects,
        PowerShellRequiredCapability capabilities,
        PowerShellExecutionDisposition disposition)
    {
        Symbol = symbol;
        Parameters = parameters ?? Array.Empty<PowerShellBoundParameter>();
        Body = body;
        ReturnType = returnType;
        Effects = effects;
        Capabilities = capabilities;
        Disposition = disposition;
    }

    internal PowerShellSymbolId Symbol { get; }
    internal PowerShellBoundParameter[] Parameters { get; }
    internal PowerShellBoundBlock Body { get; }
    internal PowerShellTypeFact ReturnType { get; }
    internal PowerShellSemanticEffect Effects { get; }
    internal PowerShellRequiredCapability Capabilities { get; }
    internal PowerShellExecutionDisposition Disposition { get; }

    internal PowerShellBoundFunction WithAnalysis(
        PowerShellTypeFact? returnType = null,
        PowerShellSemanticEffect? effects = null,
        PowerShellRequiredCapability? capabilities = null,
        PowerShellExecutionDisposition? disposition = null)
        => new(Symbol, Parameters, Body, returnType ?? ReturnType, effects ?? Effects, capabilities ?? Capabilities, disposition ?? Disposition);
}

internal sealed class PowerShellBoundProgram
{
    internal PowerShellBoundProgram(PowerShellBoundFunction[] functions, PowerShellSemanticDiagnostic[] diagnostics)
    {
        Functions = functions ?? Array.Empty<PowerShellBoundFunction>();
        Diagnostics = diagnostics ?? Array.Empty<PowerShellSemanticDiagnostic>();
    }

    internal PowerShellBoundFunction[] Functions { get; }
    internal PowerShellSemanticDiagnostic[] Diagnostics { get; }
    internal PowerShellBoundProgram WithFunctions(PowerShellBoundFunction[] functions) => new(functions, Diagnostics);
}

internal sealed class PowerShellSemanticDiagnostic
{
    internal PowerShellSemanticDiagnostic(string code, string message, SourceSpan span)
    {
        Code = code;
        Message = message;
        Span = span;
    }

    internal string Code { get; }
    internal string Message { get; }
    internal SourceSpan Span { get; }
}
