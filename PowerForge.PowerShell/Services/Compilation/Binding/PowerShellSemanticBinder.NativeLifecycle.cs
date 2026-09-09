using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class PowerShellSemanticBinder
{
    private PowerShellBoundFunction? BindNativeLifecycleFunction(ParsedSourceDocument document,
        FunctionDefinitionAst function, PowerShellSymbolId functionSymbol,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics, string? targetFramework,
        PowerShellCompilationCapability capabilities, Dictionary<string, PowerShellSemanticSymbolBinding> symbols,
        PowerShellBoundParameter[] parameters, PowerShellNativeFunctionBinding nativeBinding,
        Type? outputType, string outputTypeName, int diagnosticStart)
    {
        var locals = DeclareLocals(document, function, symbols, functions, capabilities, _commandResolver);
        var clauses = new List<PowerShellBoundConditionalClause>();
        var blocks = new[] { function.Body.BeginBlock, function.Body.ProcessBlock, function.Body.EndBlock, GetCleanBlock(function.Body) };
        for (var clause = 0; clause < blocks.Length; clause++)
        {
            if (blocks[clause] is not { } block) continue;
            if (block.Traps is { Count: > 0 })
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2946", "Native lifecycle traps require a separate transfer contract.",
                    PowerShellSourceParser.GetSpan(document, block.Extent)));
                return null;
            }
            var statements = new List<PowerShellBoundStatement>();
            foreach (var statement in block.Statements)
            {
                var bound = BindStatement(document, statement, symbols, functions, diagnostics, false,
                    targetFramework, capabilities, allowNonTerminalSuccessOutput: true);
                if (bound is null) return null;
                statements.Add(bound);
            }
            var span = PowerShellSourceParser.GetSpan(document, block.Extent);
            clauses.Add(new PowerShellBoundConditionalClause(new PowerShellBoundNativeLifecycleExpression(span, clause),
                new PowerShellBoundBlock(span, statements.ToArray())));
        }
        if (diagnostics.Count != diagnosticStart) return null;
        var bodySpan = PowerShellSourceParser.GetSpan(document, function.Body.Extent);
        var body = new PowerShellBoundBlock(bodySpan, new PowerShellBoundStatement[]
        {
            new PowerShellBoundIfStatement(bodySpan, clauses.ToArray(), null)
        });
        return new PowerShellBoundFunction(functionSymbol, parameters, locals,
            new PowerShellLexicalScope(functionSymbol, parameters.Select(static parameter => parameter.Symbol)
                .Concat(locals.Select(static local => local.Symbol)).OrderBy(static symbol => symbol.StableKey, StringComparer.Ordinal).ToArray()),
            PowerShellCommentHelpBinder.Bind(function), PowerShellAdvancedFunctionPolicy.GetAliases(function),
            PowerShellAdvancedFunctionPolicy.GetBodyBinding(function.Body), outputType, outputTypeName,
            body, PowerShellTypeFact.Unknown, PowerShellOutputCardinality.Unknown, body.Effects, body.Capabilities,
            PowerShellExecutionDisposition.Typed, nativeBinding);
    }
}
