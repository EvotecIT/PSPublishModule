using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

/// <summary>
/// Selects a conservative terminal suffix from already-bound statements. This owner never inspects
/// generated C# or the final disposition ledger and therefore cannot create a second semantic path.
/// </summary>
internal static partial class PowerShellBoundRegionCandidateSelector
{
    internal static bool TryCreate(
        ParsedSourceDocument document,
        System.Management.Automation.Language.FunctionDefinitionAst syntax,
        PowerShellSymbolId sourceFunction,
        IReadOnlyList<PowerShellBoundParameter> parameters,
        IReadOnlyList<PowerShellBoundLocal> locals,
        IReadOnlyList<System.Management.Automation.Language.StatementAst> authoredStatements,
        IReadOnlyList<PowerShellBoundStatementBinding> bindings,
        int lastFailedStatementIndex,
        out PowerShellBoundRegionCandidate candidate)
    {
        candidate = null!;
        if (HasNamedLifecycle(syntax.Body)) return false;
        var suffix = bindings
            .Where(binding => binding.AuthoredStatementIndex > lastFailedStatementIndex)
            .OrderBy(static binding => binding.AuthoredStatementIndex)
            .ToArray();
        if (suffix.Length == 0 ||
            suffix[0].AuthoredStatementIndex != lastFailedStatementIndex + 1 ||
            suffix[suffix.Length - 1].AuthoredStatementEndIndex != authoredStatements.Count - 1)
            return false;

        var statements = suffix.Select(static binding => binding.Statement).ToArray();
        // Only the final top-level scalar expression is an implicit function return.
        // Earlier output remains in the graph and must pass its ordinary cardinality proof.
        if (statements[statements.Length - 1] is PowerShellBoundExpressionStatement
            { EmitsOutput: true, RequiresOutputContinuation: false } terminal &&
            PowerShellRegionTransferTypePolicy.IsSupported(terminal.Expression.Type.ClrType))
            statements[statements.Length - 1] = new PowerShellBoundReturnStatement(terminal.Span, terminal.Expression);
        if (!AlwaysReturns(statements[statements.Length - 1])) return false;
        var terminalTransferContract = statements[statements.Length - 1] switch
        {
            PowerShellBoundReturnStatement { Expression: null, EmitsValue: false } =>
                PowerShellRegionTransferTypePolicy.DescribeNoValue(),
            PowerShellBoundReturnStatement { Expression.ValueState: PowerShellValueState.Null } =>
                PowerShellRegionTransferTypePolicy.DescribeNullValue(),
            _ => null
        };
        return TryCreateBound(document, syntax, sourceFunction, parameters, locals,
            statements, out candidate, terminalTransferContract: terminalTransferContract);
    }

    private static bool TryCreateBound(
        ParsedSourceDocument document,
        System.Management.Automation.Language.FunctionDefinitionAst syntax,
        PowerShellSymbolId sourceFunction,
        IReadOnlyList<PowerShellBoundParameter> parameters,
        IReadOnlyList<PowerShellBoundLocal> locals,
        PowerShellBoundStatement[] statements,
        out PowerShellBoundRegionCandidate candidate,
        PowerShellCompiledRegionLocal[]? continuationLocals = null,
        PowerShellCompiledRegionLocal[]? inputLocals = null,
        bool allowsPrefixOwnedInputLocals = false,
        PowerShellRegionTransferContract? terminalTransferContract = null,
        PowerShellRegionControlFlowContract? controlFlowContract = null)
    {
        candidate = null!;
        // A standalone region has no native function context argument. Do not detach reads from their invocation owner.
        if (statements.Any(static statement => statement.Capabilities.HasFlag(PowerShellRequiredCapability.NativeFunctionBinding)))
            return false;
        var first = statements[0].Span;
        var last = statements[statements.Length - 1].Span;
        var span = new SourceSpan(
            document.DocumentId,
            first.StartOffset,
            last.EndOffset,
            first.StartLine,
            first.StartColumn,
            last.EndLine,
            last.EndColumn);
        var localCalls = PowerShellSemanticAnalyzer.EnumerateStatements(new PowerShellBoundBlock(first, statements))
            .SelectMany(PowerShellSemanticAnalyzer.EnumerateDirectExpressions)
            .SelectMany(PowerShellSemanticAnalyzer.EnumerateExpressions)
            .OfType<PowerShellBoundInvocationExpression>()
            .ToArray();
        var localCallAssignments = PowerShellSemanticAnalyzer.EnumerateStatements(new PowerShellBoundBlock(first, statements))
            .OfType<PowerShellBoundAssignmentStatement>()
            .ToArray();
        if (localCalls.Any(invocation =>
                !IsClosedCollectionFactoryInvocation(invocation) ||
                !localCallAssignments.Any(assignment => ReferenceEquals(assignment.Value, invocation))))
            return false;
        var usedSymbols = CollectUsedSymbolKeys(statements);
        var selectedParameters = parameters.Where(parameter => usedSymbols.Contains(parameter.Symbol.StableKey)).ToArray();
        if (selectedParameters.Any(static parameter =>
                parameter.Contract.IsSwitch ||
                !IsSimpleVariableName(parameter.Symbol.Name)) ||
            selectedParameters.Any(parameter =>
                controlFlowContract is not null
                    ? !PowerShellRegionTransferTypePolicy.IsSupported(parameter.Type.ClrType)
                    : !(parameter.Symbol.Kind == PowerShellSymbolKind.Local
                        ? PowerShellRegionTransferTypePolicy.IsSupported(parameter.Type.ClrType)
                        : PowerShellStableScalarTypePolicy.IsSupported(parameter.Type.ClrType))))
            return false;
        var selectedLocals = locals.Where(local => usedSymbols.Contains(local.Symbol.StableKey) &&
            !parameters.Any(parameter => parameter.Symbol.StableKey == local.Symbol.StableKey)).ToArray();
        if (selectedLocals.Any(local =>
                local.Type.Provenance == PowerShellTypeFactProvenance.Unknown ||
                continuationLocals is { Length: > 0 } && local.Type.Provenance == PowerShellTypeFactProvenance.Int32OrDouble ||
                !PowerShellRegionTransferTypePolicy.IsSupported(local.Type) && local.Type.Provenance != PowerShellTypeFactProvenance.Int32OrDouble))
            return false;

        var helperName = CreateHelperName(sourceFunction, span);
        var helperSymbol = new PowerShellSymbolId(
            PowerShellSymbolKind.Function,
            document.DocumentId,
            helperName,
            span,
            sourceFunction.Name + "/region/" + span.StartOffset + "/" + span.EndOffset);
        var helperParameters = selectedParameters.Select(parameter => new PowerShellBoundParameter(
            parameter.Symbol,
            parameter.Type,
            new PowerShellCompilationParameter(
                parameter.Symbol.Name,
                parameter.Type.ClrType.FullName ?? parameter.Type.ClrType.Name,
                hasDefaultValue: false))).ToArray();
        var scopeSymbols = helperParameters.Select(static parameter => parameter.Symbol)
            .Concat(selectedLocals.Select(static local => local.Symbol))
            .OrderBy(static symbol => symbol.StableKey, StringComparer.Ordinal)
            .ToArray();
        var body = new PowerShellBoundBlock(span, statements);
        var helper = new PowerShellBoundFunction(
            helperSymbol,
            helperParameters,
            selectedLocals,
            new PowerShellLexicalScope(helperSymbol, scopeSymbols),
            help: null,
            aliases: Array.Empty<string>(),
            commandBinding: new PowerShellCompilationCommandBinding(),
            declaredOutputType: null,
            declaredOutputTypeName: string.Empty,
            body,
            PowerShellTypeFact.Unknown,
            PowerShellOutputCardinality.Unknown,
            body.Effects,
            body.Capabilities,
            PowerShellExecutionDisposition.Typed);
        candidate = new PowerShellBoundRegionCandidate(
            "region:" + document.DocumentId + ":" + span.StartOffset + ":" + span.EndOffset,
            ComputeSha256(document.Text.Substring(span.StartOffset, span.EndOffset - span.StartOffset)),
            ComputeSha256(document.Text),
            document.Path,
            sourceFunction.Name,
            syntax.Body.Extent.StartLineNumber,
            helper,
            helperParameters.Where(static parameter => parameter.Symbol.Kind == PowerShellSymbolKind.Parameter)
                .Select(static parameter => parameter.Contract).ToArray(),
            continuationLocals,
            requiresLocalOwnershipGuard: continuationLocals is { Length: > 0 } && inputLocals is not { Length: > 0 },
            inputLocals,
            allowsPrefixOwnedInputLocals,
            terminalTransferContract,
            controlFlowContract,
            localCalls.Select(static invocation => invocation.ClosedCollectionFactory!)
                .GroupBy(static call => call.SourceName, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())
                .OrderBy(static call => call.SourceName, StringComparer.OrdinalIgnoreCase)
                .ToArray());
        return true;
    }

    private static bool IsClosedCollectionFactoryInvocation(PowerShellBoundInvocationExpression invocation)
        => invocation.ResultProjection == PowerShellLocalCallResultProjection.ClosedCollectionFactory &&
           invocation.ClosedCollectionFactory is
           {
               ParameterTypes.Count: 0,
               LoweredReturnType: "System.Collections.ArrayList",
               ProjectedReturnType: "System.Collections.ArrayList",
               ResultContract:
               {
                   Shape: PowerShellRegionTransferShape.ListSequence,
                   ElementContract: PowerShellRegionTransferElementContract.OpaqueReference,
                   Direction: PowerShellRegionTransferDirection.LiveOut,
                   Ownership: PowerShellRegionTransferOwnership.CompiledCalleeFresh,
                   OutputBehavior: PowerShellRegionTransferOutputBehavior.NoEnumerate,
                   Mutation: PowerShellRegionTransferMutation.RetainedOnly,
                   Supported: true
               }
           } &&
           invocation.Arguments.Length == 0 &&
           !invocation.CapturesSuccessOutput &&
           !invocation.ReturnsModuleStateDerived;

    private static bool AlwaysReturns(PowerShellBoundStatement statement)
        => statement switch
        {
            PowerShellBoundReturnStatement => true,
            PowerShellBoundIfStatement conditional => conditional.ElseBlock is not null &&
                conditional.Clauses.All(static clause => clause.Body.Statements.LastOrDefault() is { } last && AlwaysReturns(last)) &&
                conditional.ElseBlock.Statements.LastOrDefault() is { } otherwise && AlwaysReturns(otherwise),
            PowerShellBoundSwitchStatement switchStatement => switchStatement.InputKind == PowerShellBoundSwitchInputKind.Scalar && switchStatement.DefaultBlock is not null &&
                switchStatement.Clauses.All(static clause => clause.Body.Statements.LastOrDefault() is { } last && AlwaysReturns(last)) &&
                switchStatement.DefaultBlock.Statements.LastOrDefault() is { } otherwise && AlwaysReturns(otherwise),
            PowerShellBoundTryStatement tryStatement =>
                tryStatement.Body.Statements.LastOrDefault() is { } body && AlwaysReturns(body) &&
                tryStatement.Catches.All(static clause => clause.Body.Statements.LastOrDefault() is { } last && AlwaysReturns(last)),
            _ => false
        };

    private static HashSet<string> CollectUsedSymbolKeys(IEnumerable<PowerShellBoundStatement> statements)
    {
        var block = new PowerShellBoundBlock(
            statements.First().Span,
            statements.ToArray());
        var keys = PowerShellSemanticAnalyzer.EnumerateStatements(block)
            .SelectMany(PowerShellSemanticAnalyzer.EnumerateDirectExpressions)
            .SelectMany(PowerShellSemanticAnalyzer.EnumerateExpressions)
            .SelectMany(static expression => expression switch
            {
                PowerShellBoundVariableExpression variable => new[] { variable.Symbol.StableKey },
                PowerShellBoundMutationExpression mutation => new[] { mutation.Target.StableKey },
                _ => Array.Empty<string>()
            })
            .ToHashSet(StringComparer.Ordinal);
        foreach (var statement in PowerShellSemanticAnalyzer.EnumerateStatements(block))
        {
            if (statement is PowerShellBoundAssignmentStatement assignment) keys.Add(assignment.Target.StableKey);
            if (statement is PowerShellBoundCommandCaptureStatement capture) keys.Add(capture.Target.StableKey);
            if (statement is PowerShellBoundOutputCaptureStatement { Target: not null } outputCapture) keys.Add(outputCapture.Target.StableKey);
            if (statement is PowerShellBoundForEachStatement forEach) keys.Add(forEach.Variable.StableKey);
            if (statement is PowerShellBoundForStatement { Initializer: not null } forLoop) keys.Add(forLoop.Initializer.Target.StableKey);
        }
        return keys;
    }

    private static string CreateHelperName(PowerShellSymbolId function, SourceSpan span)
    {
        using var sha = SHA256.Create();
        var identity = function.StableKey + "\0" + span.StartOffset + "\0" + span.EndOffset;
        var suffix = string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(identity))
            .Take(12)
            .Select(static value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
        return "__PowerForgeRegion_" + suffix;
    }

    private static bool IsSimpleVariableName(string value)
        => !string.IsNullOrWhiteSpace(value) &&
           (char.IsLetter(value[0]) || value[0] == '_') &&
           value.Skip(1).All(static character => char.IsLetterOrDigit(character) || character == '_');

    private static bool HasNamedLifecycle(System.Management.Automation.Language.ScriptBlockAst body)
        => body.DynamicParamBlock is not null ||
           body.BeginBlock is not null ||
           body.ProcessBlock is not null ||
           body.EndBlock is { Unnamed: false } ||
           body.GetType().GetProperty("CleanBlock")?.GetValue(body) is not null;

    private static string ComputeSha256(string value)
    {
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value))
            .Select(static item => item.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
    }
}

internal sealed class PowerShellBoundStatementBinding
{
    internal PowerShellBoundStatementBinding(
        int authoredStatementIndex,
        int authoredStatementEndIndex,
        PowerShellBoundStatement statement)
    {
        AuthoredStatementIndex = authoredStatementIndex;
        AuthoredStatementEndIndex = Math.Max(authoredStatementIndex, authoredStatementEndIndex);
        Statement = statement;
    }

    internal int AuthoredStatementIndex { get; }
    internal int AuthoredStatementEndIndex { get; }
    internal PowerShellBoundStatement Statement { get; }
}

internal sealed class PowerShellBoundRegionCandidate
{
    internal PowerShellBoundRegionCandidate(
        string regionId,
        string sourceSha256,
        string sourceDocumentSha256,
        string sourcePath,
        string sourceName,
        int sourceLine,
        PowerShellBoundFunction regionFunction,
        PowerShellCompilationParameter[] inputParameters,
        PowerShellCompiledRegionLocal[]? continuationLocals = null,
        bool requiresLocalOwnershipGuard = false,
        PowerShellCompiledRegionLocal[]? inputLocals = null,
        bool allowsPrefixOwnedInputLocals = false,
        PowerShellRegionTransferContract? terminalTransferContract = null,
        PowerShellRegionControlFlowContract? controlFlowContract = null,
        PowerShellCompiledRegionLocalCall[]? localCalls = null)
    {
        RegionId = regionId;
        SourceSha256 = sourceSha256;
        SourceDocumentSha256 = sourceDocumentSha256;
        SourcePath = sourcePath;
        SourceName = sourceName;
        SourceLine = sourceLine;
        RegionFunction = regionFunction;
        InputParameters = inputParameters ?? Array.Empty<PowerShellCompilationParameter>();
        ContinuationLocals = continuationLocals ?? Array.Empty<PowerShellCompiledRegionLocal>();
        InputLocals = inputLocals ?? Array.Empty<PowerShellCompiledRegionLocal>();
        RequiresLocalOwnershipGuard = requiresLocalOwnershipGuard;
        AllowsPrefixOwnedInputLocals = allowsPrefixOwnedInputLocals;
        TerminalTransferContract = terminalTransferContract;
        ControlFlowContract = controlFlowContract;
        LocalCalls = localCalls ?? Array.Empty<PowerShellCompiledRegionLocalCall>();
    }

    internal string RegionId { get; }
    internal string SourceSha256 { get; }
    internal string SourceDocumentSha256 { get; }
    internal string SourcePath { get; }
    internal string SourceName { get; }
    internal int SourceLine { get; }
    internal PowerShellBoundFunction RegionFunction { get; }
    internal PowerShellImmutableArray<PowerShellCompilationParameter> InputParameters { get; }
    internal PowerShellImmutableArray<PowerShellCompiledRegionLocal> ContinuationLocals { get; }
    internal PowerShellImmutableArray<PowerShellCompiledRegionLocal> InputLocals { get; }
    internal bool RequiresLocalOwnershipGuard { get; }
    internal bool AllowsPrefixOwnedInputLocals { get; }
    internal PowerShellRegionTransferContract? TerminalTransferContract { get; }
    internal PowerShellRegionControlFlowContract? ControlFlowContract { get; }
    internal PowerShellImmutableArray<PowerShellCompiledRegionLocalCall> LocalCalls { get; }
}
