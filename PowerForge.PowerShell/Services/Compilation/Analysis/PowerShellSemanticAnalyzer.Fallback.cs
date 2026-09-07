namespace PowerForge;

internal sealed partial class PowerShellSemanticAnalyzer
{
    private sealed class FallbackPass : IPowerShellSemanticPass
    {
        public string Id => "60-fallback-fixed-point";

        public PowerShellBoundProgram Run(PowerShellBoundProgram program)
        {
            var functions = program.Functions.ToDictionary(static function => function.Symbol.StableKey, StringComparer.Ordinal);
            RunFixedPoint(functions, (function, lookup) =>
            {
                if (function.Disposition.Kind != PowerShellExecutionDispositionKind.Typed) return function;
                var blockingDiagnostic = program.Diagnostics.FirstOrDefault(diagnostic =>
                    diagnostic.Span.DocumentId.Equals(function.Symbol.DocumentId, StringComparison.Ordinal) &&
                    diagnostic.Span.StartOffset >= function.Symbol.Declaration.StartOffset &&
                    diagnostic.Span.StartOffset <= function.Symbol.Declaration.EndOffset);
                if (blockingDiagnostic is not null)
                {
                    return function.WithAnalysis(disposition: new PowerShellExecutionDisposition(
                        PowerShellExecutionDispositionKind.Fallback,
                        blockingDiagnostic.Code,
                        blockingDiagnostic.Message));
                }
                if (EnumerateStatements(function.Body).OfType<PowerShellBoundExpressionStatement>().Any(statement =>
                        statement.RequiresOutputContinuation && ResolveType(statement.Expression, lookup).ClrType != typeof(void)))
                {
                    return function.WithAnalysis(disposition: new PowerShellExecutionDisposition(
                        PowerShellExecutionDispositionKind.Fallback,
                        "control.output.continuation",
                        "Non-terminal success output requires a continuation-preserving output contract; it cannot become an early CLR return."));
                }
                if (EnumerateStatements(function.Body).OfType<PowerShellBoundStreamWriteStatement>().Any(statement =>
                        statement.Provider is null &&
                        ResolveType(statement.Message, lookup).ClrType != typeof(void) &&
                        !PowerShellStableScalarTypePolicy.IsSupported(ResolveType(statement.Message, lookup))))
                    return function.WithAnalysis(disposition: new PowerShellExecutionDisposition(
                        PowerShellExecutionDispositionKind.Fallback,
                        "control.output.enumeration",
                        "Implicit streamed output currently requires stable scalar records; wider enumeration and failure continuation remain on the PowerShell path."));
                if (EnumerateStatements(function.Body).OfType<PowerShellBoundTryStatement>().Any(statement =>
                        statement.FinallyBlock is not null &&
                        BlockHasEffect(statement.FinallyBlock, PowerShellSemanticEffect.NonSuccessStream, lookup) &&
                        (BlockHasEffect(statement.Body, PowerShellSemanticEffect.SuccessOutput, lookup) ||
                         statement.Catches.Any(clause => BlockHasEffect(clause.Body, PowerShellSemanticEffect.SuccessOutput, lookup)))))
                    return function.WithAnalysis(disposition: new PowerShellExecutionDisposition(
                        PowerShellExecutionDispositionKind.Fallback,
                        "control.finally.stream-stop",
                        "Non-success streams from finally during downstream stop require PowerShell preference and variable-capture handling that the generated command host cannot yet preserve."));
                if ((function.ReturnType.ClrType == typeof(Dictionary<string, string>) ||
                     function.ReturnType.ClrType == typeof(System.Collections.Hashtable) ||
                     function.ReturnType.ClrType == typeof(System.Collections.Specialized.OrderedDictionary)) &&
                    ReturnsCompilerDictionary(function))
                {
                    return function.WithAnalysis(disposition: new PowerShellExecutionDisposition(
                        PowerShellExecutionDispositionKind.Fallback,
                        PowerShellCompilationFeatureIds.ForSyntax("VariableExpressionAst"),
                        "Typed dictionaries are lookup-only locals and cannot escape through the current public CLR return contract."));
                }
                if (IsMutuallyRecursive(function.Symbol, program.CallGraph))
                {
                    return function.WithAnalysis(disposition: new PowerShellExecutionDisposition(
                        PowerShellExecutionDispositionKind.Fallback,
                        PowerShellCompilationFeatureIds.FunctionGraph,
                        $"Function '{function.Symbol.Name}' participates in a mutually recursive local-call cycle, which is not supported by the typed ABI."));
                }
                var isRecursive = IsRecursive(function.Symbol, program.CallGraph);
                if (isRecursive && (function.DeclaredOutputType is null || function.DeclaredOutputType == typeof(void)))
                {
                    return function.WithAnalysis(disposition: new PowerShellExecutionDisposition(
                        PowerShellExecutionDispositionKind.Fallback,
                        PowerShellCompilationFeatureIds.FunctionGraph,
                        $"Function '{function.Symbol.Name}' participates in a recursive local-call cycle without a declared return contract; OutputType(void) is advisory metadata, not a value contract."));
                }
                if (function.ReturnType.Provenance == PowerShellTypeFactProvenance.Unknown)
                    return function.WithAnalysis(disposition: isRecursive
                        ? new PowerShellExecutionDisposition(
                            PowerShellExecutionDispositionKind.Fallback,
                            PowerShellCompilationFeatureIds.FunctionGraph,
                            $"Function '{function.Symbol.Name}' participates in a recursive local-call cycle without a declared return contract.")
                        : new PowerShellExecutionDisposition(PowerShellExecutionDispositionKind.Fallback, "type.return.unknown", "The function return type is not statically known."));
                if (function.ReturnType.ClrType != typeof(void) && !BlockReturnsValue(function.Body))
                {
                    return function.WithAnalysis(disposition: new PowerShellExecutionDisposition(
                        PowerShellExecutionDispositionKind.Fallback,
                        "control.return.fallthrough",
                        $"Typed non-void unit '{function.Symbol.Name}' must end with an explicit return statement on every reachable path."));
                }
                var unresolvedCall = EnumerateStatements(function.Body)
                    .SelectMany(EnumerateDirectExpressions)
                    .SelectMany(EnumerateInvocations)
                    .FirstOrDefault(invocation => !lookup.ContainsKey(invocation.Target.StableKey));
                if (unresolvedCall is not null)
                {
                    return function.WithAnalysis(disposition: new PowerShellExecutionDisposition(
                        PowerShellExecutionDispositionKind.Fallback,
                        "call.binding.unavailable",
                        $"Local function '{unresolvedCall.Target.Name}' did not produce a bound function contract."));
                }
                var shouldProcessTarget = GetCallees(function, lookup).FirstOrDefault(ContainsShouldProcess);
                if (shouldProcessTarget is not null)
                {
                    return function.WithAnalysis(disposition: new PowerShellExecutionDisposition(
                        PowerShellExecutionDispositionKind.Fallback,
                        "call.should-process.command-identity",
                        $"Local function '{shouldProcessTarget.Symbol.Name}' uses ShouldProcess and must remain on the PowerShell command path so its command identity and ConfirmImpact are preserved."));
                }
                var validationTarget = GetValidationCallInsideTypeDiscriminatingTry(function, lookup);
                if (validationTarget is not null)
                {
                    return function.WithAnalysis(disposition: new PowerShellExecutionDisposition(
                        PowerShellExecutionDispositionKind.Fallback,
                        "call.validation.binding-exception",
                        $"Local function '{validationTarget.Symbol.Name}' performs parameter validation inside a typed try/catch, whose PowerShell binding-exception identity must remain on the PowerShell command path."));
                }
                var consumedTarget = GetConsumedCollectionOrHostedCall(function, lookup);
                if (consumedTarget is not null)
                {
                    var hosted = consumedTarget.Capabilities.HasFlag(PowerShellRequiredCapability.CommandRegion);
                    var streamed = HasSuccessStreamOutput(consumedTarget, lookup);
                    return function.WithAnalysis(disposition: new PowerShellExecutionDisposition(
                        PowerShellExecutionDispositionKind.Fallback,
                        hosted ? "call.command-region.cardinality" : streamed ? "call.stream.cardinality" : "call.collection.cardinality",
                        hosted
                            ? $"Local function '{consumedTarget.Symbol.Name}' emits PowerShell command-region success output whose pipeline cardinality cannot be preserved when the call result is consumed."
                            : streamed
                            ? $"Local function '{consumedTarget.Symbol.Name}' writes success records through its command host; consuming its CLR return alone would lose or misroute those records."
                            : $"Local function '{consumedTarget.Symbol.Name}' returns an array whose PowerShell pipeline cardinality cannot be preserved when the result is consumed."));
                }
                var blocked = GetCallees(function, lookup).FirstOrDefault(static callee => callee.Disposition.Kind != PowerShellExecutionDispositionKind.Typed);
                return blocked is null
                    ? function
                    : function.WithAnalysis(disposition: new PowerShellExecutionDisposition(PowerShellExecutionDispositionKind.Fallback, "call.fallback", $"Local function '{blocked.Symbol.Name}' requires fallback."));
            }, static (left, right) => left.Disposition.Kind == right.Disposition.Kind && left.Disposition.ReasonCode == right.Disposition.ReasonCode);
            return program.WithFunctions(functions.Values.OrderBy(static function => function.Symbol.StableKey, StringComparer.Ordinal).ToArray());
        }

        private static bool BlockReturnsValue(PowerShellBoundBlock block)
            => block.Statements.LastOrDefault() switch
            {
                PowerShellBoundReturnStatement { EmitsValue: true } => true,
                PowerShellBoundExpressionStatement { EmitsOutput: true } => true,
                PowerShellBoundThrowStatement => true,
                PowerShellBoundIfStatement conditional => conditional.ElseBlock is not null &&
                    conditional.Clauses.All(static clause => BlockReturnsValue(clause.Body)) &&
                    BlockReturnsValue(conditional.ElseBlock),
                PowerShellBoundSwitchStatement switchStatement => switchStatement.DefaultBlock is not null &&
                    switchStatement.Clauses.All(static clause => BlockReturnsValue(clause.Body)) &&
                    BlockReturnsValue(switchStatement.DefaultBlock),
                PowerShellBoundTryStatement tryStatement =>
                    BlockReturnsValue(tryStatement.Body) &&
                    tryStatement.Catches.All(static clause => BlockReturnsValue(clause.Body)),
                _ => false
            };

    }
}
