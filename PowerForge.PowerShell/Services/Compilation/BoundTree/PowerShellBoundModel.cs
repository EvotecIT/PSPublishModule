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
    internal PowerShellSymbolId(PowerShellSymbolKind kind, string documentId, string name, SourceSpan declaration, string? identityPath = null)
    {
        Kind = kind;
        DocumentId = documentId ?? string.Empty;
        Name = name ?? string.Empty;
        Declaration = declaration;
        StableKey = string.Concat(kind.ToString(), ":", DocumentId, ":", (identityPath ?? Name).ToUpperInvariant());
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
    Host = 64,
    TerminatingError = 128
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
    protected PowerShellBoundStatement(
        SourceSpan span,
        PowerShellSemanticEffect effects,
        PowerShellRequiredCapability capabilities = PowerShellRequiredCapability.None,
        PowerShellExecutionDisposition? disposition = null)
        : base(span, new PowerShellTypeFact(typeof(void), PowerShellTypeFactProvenance.Inferred, "Statements do not produce a CLR value."), PowerShellValueState.Known, PowerShellOutputCardinality.None, effects, capabilities, disposition ?? PowerShellExecutionDisposition.Typed)
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
    internal PowerShellBoundVariableExpression(SourceSpan span, PowerShellSymbolId symbol, PowerShellTypeFact type, PowerShellValueState valueState = PowerShellValueState.Unknown)
        : base(span, type, valueState)
    {
        Symbol = symbol;
    }

    internal PowerShellSymbolId Symbol { get; }
}

internal sealed class PowerShellBoundConversionExpression : PowerShellBoundExpression
{
    internal PowerShellBoundConversionExpression(SourceSpan span, PowerShellTypeFact targetType, PowerShellBoundExpression operand)
        : base(span, targetType, operand.ValueState)
    {
        Operand = operand;
    }

    internal PowerShellBoundExpression Operand { get; }
}

internal sealed class PowerShellBoundInvocationExpression : PowerShellBoundExpression
{
    internal PowerShellBoundInvocationExpression(
        SourceSpan span,
        PowerShellSymbolId target,
        PowerShellBoundExpression[] arguments,
        PowerShellTypeFact returnType,
        int[]? authoredEvaluationOrder = null,
        string[]? boundParameterNames = null)
        : base(span, returnType, PowerShellValueState.Unknown)
    {
        Target = target;
        Arguments = arguments ?? Array.Empty<PowerShellBoundExpression>();
        AuthoredEvaluationOrder = authoredEvaluationOrder ?? Enumerable.Range(0, Arguments.Length).ToArray();
        BoundParameterNames = boundParameterNames ?? Array.Empty<string>();
    }

    internal PowerShellSymbolId Target { get; }
    internal PowerShellBoundExpression[] Arguments { get; }
    internal int[] AuthoredEvaluationOrder { get; }
    internal string[] BoundParameterNames { get; }
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

internal sealed class PowerShellBoundAssignmentStatement : PowerShellBoundStatement
{
    internal PowerShellBoundAssignmentStatement(
        SourceSpan span,
        PowerShellSymbolId target,
        PowerShellBoundExpression value,
        PowerShellBoundMutationOperator operation = PowerShellBoundMutationOperator.Assign,
        bool normalizeNullString = false,
        bool checkedIntegral = false)
        : base(span, PowerShellSemanticEffect.Mutation)
    {
        Target = target;
        Value = value;
        Operation = operation;
        NormalizeNullString = normalizeNullString;
        CheckedIntegral = checkedIntegral;
    }

    internal PowerShellSymbolId Target { get; }
    internal PowerShellBoundExpression Value { get; }
    internal PowerShellBoundMutationOperator Operation { get; }
    internal bool NormalizeNullString { get; }
    internal bool CheckedIntegral { get; }
}

internal sealed class PowerShellBoundParameter
{
    internal PowerShellBoundParameter(
        PowerShellSymbolId symbol,
        PowerShellTypeFact type,
        PowerShellCompilationParameter contract)
    {
        Symbol = symbol;
        Type = type;
        Contract = contract;
    }

    internal PowerShellSymbolId Symbol { get; }
    internal PowerShellTypeFact Type { get; }
    internal PowerShellCompilationParameter Contract { get; }
}

internal sealed class PowerShellBoundLocal
{
    internal PowerShellBoundLocal(PowerShellSymbolId symbol, PowerShellTypeFact type)
    {
        Symbol = symbol;
        Type = type;
    }

    internal PowerShellSymbolId Symbol { get; }
    internal PowerShellTypeFact Type { get; }
}

internal sealed class PowerShellLexicalScope
{
    internal PowerShellLexicalScope(PowerShellSymbolId owner, PowerShellSymbolId[] symbols)
    {
        Owner = owner;
        Symbols = symbols ?? Array.Empty<PowerShellSymbolId>();
    }

    internal PowerShellSymbolId Owner { get; }
    internal PowerShellSymbolId[] Symbols { get; }
}

internal sealed class PowerShellBoundHelpMetadata
{
    internal PowerShellBoundHelpMetadata(
        string synopsis,
        string description,
        string notes,
        IReadOnlyDictionary<string, string> parameters,
        string[] examples,
        string[] links,
        string[] inputs,
        string[] outputs)
    {
        Synopsis = synopsis ?? string.Empty;
        Description = description ?? string.Empty;
        Notes = notes ?? string.Empty;
        Parameters = parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Examples = examples ?? Array.Empty<string>();
        Links = links ?? Array.Empty<string>();
        Inputs = inputs ?? Array.Empty<string>();
        Outputs = outputs ?? Array.Empty<string>();
    }

    internal string Synopsis { get; }
    internal string Description { get; }
    internal string Notes { get; }
    internal IReadOnlyDictionary<string, string> Parameters { get; }
    internal string[] Examples { get; }
    internal string[] Links { get; }
    internal string[] Inputs { get; }
    internal string[] Outputs { get; }

    internal PowerShellCompilationHelp ToPublicModel()
        => new()
        {
            Synopsis = Synopsis,
            Description = Description,
            Notes = Notes,
            Parameters = Parameters.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            Examples = Examples.ToArray(),
            Links = Links.ToArray(),
            Inputs = Inputs.ToArray(),
            Outputs = Outputs.ToArray()
        };
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
    internal PowerShellSemanticEffect Effects => Statements.Aggregate(PowerShellSemanticEffect.None, static (value, statement) => value | statement.Effects);
    internal PowerShellRequiredCapability Capabilities => Statements.Aggregate(PowerShellRequiredCapability.None, static (value, statement) => value | statement.Capabilities);
}

internal sealed class PowerShellBoundFunction
{
    internal PowerShellBoundFunction(
        PowerShellSymbolId symbol,
        PowerShellBoundParameter[] parameters,
        PowerShellBoundLocal[] locals,
        PowerShellLexicalScope scope,
        PowerShellBoundHelpMetadata? help,
        Type? declaredOutputType,
        PowerShellBoundBlock body,
        PowerShellTypeFact returnType,
        PowerShellSemanticEffect effects,
        PowerShellRequiredCapability capabilities,
        PowerShellExecutionDisposition disposition)
    {
        Symbol = symbol;
        Parameters = parameters ?? Array.Empty<PowerShellBoundParameter>();
        Locals = locals ?? Array.Empty<PowerShellBoundLocal>();
        Scope = scope;
        Help = help;
        DeclaredOutputType = declaredOutputType;
        Body = body;
        ReturnType = returnType;
        Effects = effects;
        Capabilities = capabilities;
        Disposition = disposition;
    }

    internal PowerShellSymbolId Symbol { get; }
    internal PowerShellBoundParameter[] Parameters { get; }
    internal PowerShellBoundLocal[] Locals { get; }
    internal PowerShellLexicalScope Scope { get; }
    internal PowerShellBoundHelpMetadata? Help { get; }
    internal Type? DeclaredOutputType { get; }
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
        => new(Symbol, Parameters, Locals, Scope, Help, DeclaredOutputType, Body, returnType ?? ReturnType, effects ?? Effects, capabilities ?? Capabilities, disposition ?? Disposition);
}

internal sealed class PowerShellBoundSourceDocument
{
    internal PowerShellBoundSourceDocument(string documentId, string path, SourceSpan span, PowerShellSymbolId[] functions)
    {
        DocumentId = documentId;
        Path = path;
        Span = span;
        Functions = functions ?? Array.Empty<PowerShellSymbolId>();
    }

    internal string DocumentId { get; }
    internal string Path { get; }
    internal SourceSpan Span { get; }
    internal PowerShellSymbolId[] Functions { get; }
}

internal sealed class PowerShellBoundProgram
{
    internal PowerShellBoundProgram(
        PowerShellBoundSourceDocument[] documents,
        PowerShellBoundFunction[] functions,
        PowerShellSemanticDiagnostic[] diagnostics,
        PowerShellCallGraphEdge[]? callGraph = null)
    {
        Documents = documents ?? Array.Empty<PowerShellBoundSourceDocument>();
        Functions = functions ?? Array.Empty<PowerShellBoundFunction>();
        Diagnostics = diagnostics ?? Array.Empty<PowerShellSemanticDiagnostic>();
        CallGraph = callGraph ?? Array.Empty<PowerShellCallGraphEdge>();
    }

    internal PowerShellBoundSourceDocument[] Documents { get; }
    internal PowerShellBoundFunction[] Functions { get; }
    internal PowerShellSemanticDiagnostic[] Diagnostics { get; }
    internal PowerShellCallGraphEdge[] CallGraph { get; }
    internal PowerShellBoundProgram WithFunctions(PowerShellBoundFunction[] functions) => new(Documents, functions, Diagnostics, CallGraph);
    internal PowerShellBoundProgram WithDiagnostics(PowerShellSemanticDiagnostic[] diagnostics) => new(Documents, Functions, diagnostics, CallGraph);
    internal PowerShellBoundProgram WithCallGraph(PowerShellCallGraphEdge[] callGraph) => new(Documents, Functions, Diagnostics, callGraph);
    internal PowerShellBoundProgram WithAnalysis(PowerShellBoundFunction[] functions, PowerShellSemanticDiagnostic[] diagnostics)
        => new(Documents, functions, diagnostics, CallGraph);
}

internal sealed class PowerShellCallGraphEdge
{
    internal PowerShellCallGraphEdge(PowerShellSymbolId caller, PowerShellSymbolId callee, SourceSpan invocation)
    {
        Caller = caller;
        Callee = callee;
        Invocation = invocation;
    }

    internal PowerShellSymbolId Caller { get; }
    internal PowerShellSymbolId Callee { get; }
    internal SourceSpan Invocation { get; }
    internal string StableKey => Caller.StableKey + "->" + Callee.StableKey + ":" + Invocation.StartOffset.ToString(System.Globalization.CultureInfo.InvariantCulture);
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
