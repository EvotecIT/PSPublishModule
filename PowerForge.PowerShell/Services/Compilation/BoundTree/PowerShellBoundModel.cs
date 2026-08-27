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
    Reflection = 64,
    PowerShellLanguageOperators = 128,
    RuntimeStateIntrinsics = 256,
    PowerShellHostTypes = 512,
    PowerShellStreams = 1024,
    PowerShellLanguageConversions = 2048
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
    protected PowerShellBoundExpression(
        SourceSpan span,
        PowerShellTypeFact type,
        PowerShellValueState valueState,
        PowerShellSemanticEffect effects = PowerShellSemanticEffect.None,
        PowerShellRequiredCapability capabilities = PowerShellRequiredCapability.None)
        : base(span, type, valueState, PowerShellOutputCardinality.Scalar, effects, capabilities, PowerShellExecutionDisposition.Typed)
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
    internal PowerShellBoundConversionExpression(
        SourceSpan span,
        PowerShellTypeFact targetType,
        PowerShellBoundExpression operand,
        bool usePowerShellLanguageRuntime = false,
        bool usePowerShellTruthiness = false)
        : base(
            span,
            targetType,
            operand.ValueState,
            operand.Effects,
            operand.Capabilities | (usePowerShellLanguageRuntime || usePowerShellTruthiness ? PowerShellRequiredCapability.PowerShellLanguageConversions : PowerShellRequiredCapability.None))
    {
        if (usePowerShellLanguageRuntime && usePowerShellTruthiness)
            throw new ArgumentException("A bound conversion cannot select two PowerShell language conversion operations.");
        Operand = operand;
        UsePowerShellLanguageRuntime = usePowerShellLanguageRuntime;
        UsePowerShellTruthiness = usePowerShellTruthiness;
    }

    internal PowerShellBoundExpression Operand { get; }
    internal bool UsePowerShellLanguageRuntime { get; }
    internal bool UsePowerShellTruthiness { get; }
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
        : base(
            span,
            returnType,
            PowerShellValueState.Unknown,
            arguments.Aggregate(PowerShellSemanticEffect.None, static (effects, argument) => effects | argument.Effects),
            arguments.Aggregate(PowerShellRequiredCapability.None, static (capabilities, argument) => capabilities | argument.Capabilities))
    {
        Target = target;
        Arguments = arguments ?? Array.Empty<PowerShellBoundExpression>();
        AuthoredEvaluationOrder = authoredEvaluationOrder ?? Enumerable.Range(0, Arguments.Length).ToArray();
        BoundParameterNames = boundParameterNames ?? Array.Empty<string>();
    }

    internal PowerShellSymbolId Target { get; }
    internal PowerShellImmutableArray<PowerShellBoundExpression> Arguments { get; }
    internal PowerShellImmutableArray<int> AuthoredEvaluationOrder { get; }
    internal PowerShellImmutableArray<string> BoundParameterNames { get; }
}

internal sealed class PowerShellBoundReturnStatement : PowerShellBoundStatement
{
    internal PowerShellBoundReturnStatement(SourceSpan span, PowerShellBoundExpression? expression, bool emitsValue = true)
        : base(span,
            (expression?.Effects ?? PowerShellSemanticEffect.None) |
            (expression is null || !emitsValue ? PowerShellSemanticEffect.None : PowerShellSemanticEffect.SuccessOutput) |
            (expression is PowerShellBoundMutationExpression ? PowerShellSemanticEffect.Mutation : PowerShellSemanticEffect.None),
            expression?.Capabilities ?? PowerShellRequiredCapability.None)
    {
        Expression = expression;
        EmitsValue = expression is not null && emitsValue;
    }

    internal PowerShellBoundExpression? Expression { get; }
    internal bool EmitsValue { get; }
}

internal sealed class PowerShellBoundExpressionStatement : PowerShellBoundStatement
{
    internal PowerShellBoundExpressionStatement(SourceSpan span, PowerShellBoundExpression expression, bool emitsOutput)
        : base(span,
            expression.Effects |
            (emitsOutput ? PowerShellSemanticEffect.SuccessOutput : PowerShellSemanticEffect.None) |
            (expression is PowerShellBoundMutationExpression ? PowerShellSemanticEffect.Mutation : PowerShellSemanticEffect.None),
            expression.Capabilities)
    {
        Expression = expression;
        EmitsOutput = emitsOutput;
    }

    internal PowerShellBoundExpression Expression { get; }
    internal bool EmitsOutput { get; }
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
        : base(span, PowerShellSemanticEffect.Mutation | value.Effects, value.Capabilities)
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
    internal PowerShellImmutableArray<PowerShellSymbolId> Symbols { get; }
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
    internal PowerShellImmutableArray<string> Examples { get; }
    internal PowerShellImmutableArray<string> Links { get; }
    internal PowerShellImmutableArray<string> Inputs { get; }
    internal PowerShellImmutableArray<string> Outputs { get; }

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
    internal PowerShellImmutableArray<PowerShellBoundStatement> Statements { get; }
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
        PowerShellOutputCardinality outputCardinality,
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
        OutputCardinality = outputCardinality;
        Effects = effects;
        Capabilities = capabilities;
        Disposition = disposition;
    }

    internal PowerShellSymbolId Symbol { get; }
    internal PowerShellImmutableArray<PowerShellBoundParameter> Parameters { get; }
    internal PowerShellImmutableArray<PowerShellBoundLocal> Locals { get; }
    internal PowerShellLexicalScope Scope { get; }
    internal PowerShellBoundHelpMetadata? Help { get; }
    internal Type? DeclaredOutputType { get; }
    internal PowerShellBoundBlock Body { get; }
    internal PowerShellTypeFact ReturnType { get; }
    internal PowerShellOutputCardinality OutputCardinality { get; }
    internal PowerShellSemanticEffect Effects { get; }
    internal PowerShellRequiredCapability Capabilities { get; }
    internal PowerShellExecutionDisposition Disposition { get; }

    internal PowerShellBoundFunction WithAnalysis(
        PowerShellTypeFact? returnType = null,
        PowerShellOutputCardinality? outputCardinality = null,
        PowerShellSemanticEffect? effects = null,
        PowerShellRequiredCapability? capabilities = null,
        PowerShellExecutionDisposition? disposition = null)
        => new(Symbol, Parameters.ToArray(), Locals.ToArray(), Scope, Help, DeclaredOutputType, Body, returnType ?? ReturnType, outputCardinality ?? OutputCardinality, effects ?? Effects, capabilities ?? Capabilities, disposition ?? Disposition);
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
    internal PowerShellImmutableArray<PowerShellSymbolId> Functions { get; }
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

    internal PowerShellImmutableArray<PowerShellBoundSourceDocument> Documents { get; }
    internal PowerShellImmutableArray<PowerShellBoundFunction> Functions { get; }
    internal PowerShellImmutableArray<PowerShellSemanticDiagnostic> Diagnostics { get; }
    internal PowerShellImmutableArray<PowerShellCallGraphEdge> CallGraph { get; }
    internal PowerShellBoundProgram WithFunctions(PowerShellBoundFunction[] functions) => new(Documents.ToArray(), functions, Diagnostics.ToArray(), CallGraph.ToArray());
    internal PowerShellBoundProgram WithDiagnostics(PowerShellSemanticDiagnostic[] diagnostics) => new(Documents.ToArray(), Functions.ToArray(), diagnostics, CallGraph.ToArray());
    internal PowerShellBoundProgram WithCallGraph(PowerShellCallGraphEdge[] callGraph) => new(Documents.ToArray(), Functions.ToArray(), Diagnostics.ToArray(), callGraph);
    internal PowerShellBoundProgram WithAnalysis(PowerShellBoundFunction[] functions, PowerShellSemanticDiagnostic[] diagnostics)
        => new(Documents.ToArray(), functions, diagnostics, CallGraph.ToArray());
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
