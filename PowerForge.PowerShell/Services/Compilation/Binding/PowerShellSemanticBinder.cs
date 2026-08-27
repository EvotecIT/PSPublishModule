using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Converts parser-owned PowerShell syntax into the compiler's neutral bound representation.
/// </summary>
internal sealed class PowerShellSemanticBinder
{
    internal PowerShellBoundProgram Bind(IEnumerable<ParsedSourceDocument> documents)
    {
        if (documents is null) throw new ArgumentNullException(nameof(documents));
        var functions = new List<PowerShellBoundFunction>();
        var diagnostics = new List<PowerShellSemanticDiagnostic>();

        foreach (var document in documents.OrderBy(static item => item.DocumentId, StringComparer.Ordinal))
        {
            foreach (var parseError in document.Errors)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    "PSB0001",
                    parseError.Message,
                    PowerShellSourceParser.GetSpan(document, parseError.Extent)));
            }
            if (document.Errors.Length > 0) continue;

            var declarations = document.SyntaxRoot
                .FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
                .Cast<FunctionDefinitionAst>()
                .OrderBy(static function => function.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(static function => function.Extent.StartOffset)
                .ToArray();
            foreach (var function in declarations)
            {
                var bound = BindFunction(document, function, diagnostics);
                if (bound is not null) functions.Add(bound);
            }
        }

        return new PowerShellBoundProgram(
            functions.OrderBy(static function => function.Symbol.StableKey, StringComparer.Ordinal).ToArray(),
            diagnostics.OrderBy(static diagnostic => diagnostic.Span.DocumentId, StringComparer.Ordinal)
                .ThenBy(static diagnostic => diagnostic.Span.StartOffset)
                .ThenBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)
                .ToArray());
    }

    private static PowerShellBoundFunction? BindFunction(
        ParsedSourceDocument document,
        FunctionDefinitionAst function,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var functionSpan = PowerShellSourceParser.GetSpan(document, function.Extent);
        var functionSymbol = new PowerShellSymbolId(PowerShellSymbolKind.Function, document.DocumentId, function.Name, functionSpan);
        var symbols = new Dictionary<string, PowerShellBoundParameter>(StringComparer.OrdinalIgnoreCase);
        var parameters = new List<PowerShellBoundParameter>();

        var authoredParameters = function.Body.ParamBlock?.Parameters.ToArray() ?? Array.Empty<ParameterAst>();
        foreach (var parameter in authoredParameters)
        {
            var name = parameter.Name.VariablePath.UserPath;
            var span = PowerShellSourceParser.GetSpan(document, parameter.Extent);
            if (symbols.ContainsKey(name))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB1001", $"Parameter '${name}' is declared more than once.", span));
                return null;
            }

            var type = parameter.StaticType == typeof(object)
                ? PowerShellTypeFact.Unknown
                : new PowerShellTypeFact(parameter.StaticType, PowerShellTypeFactProvenance.Explicit, $"Parameter '${name}' has an authored type constraint.");
            var symbol = new PowerShellSymbolId(PowerShellSymbolKind.Parameter, document.DocumentId, name, span);
            var bound = new PowerShellBoundParameter(symbol, type);
            symbols.Add(name, bound);
            parameters.Add(bound);
        }

        var statements = new List<PowerShellBoundStatement>();
        var authoredStatements = function.Body.EndBlock?.Statements.ToArray() ?? Array.Empty<StatementAst>();
        for (var index = 0; index < authoredStatements.Length; index++)
        {
            var statement = authoredStatements[index];
            var diagnosticCount = diagnostics.Count;
            var bound = BindStatement(document, statement, symbols, diagnostics, index == authoredStatements.Length - 1);
            if (bound is null)
            {
                if (diagnostics.Count == diagnosticCount)
                {
                    diagnostics.Add(new PowerShellSemanticDiagnostic(
                        "PSB2001",
                        $"Statement '{statement.GetType().Name}' is not yet represented by the bound pipeline.",
                        PowerShellSourceParser.GetSpan(document, statement.Extent)));
                }
                return null;
            }
            statements.Add(bound);
        }

        var body = new PowerShellBoundBlock(PowerShellSourceParser.GetSpan(document, function.Body.Extent), statements.ToArray());
        return new PowerShellBoundFunction(
            functionSymbol,
            parameters.ToArray(),
            body,
            PowerShellTypeFact.Unknown,
            PowerShellSemanticEffect.None,
            PowerShellRequiredCapability.None,
            PowerShellExecutionDisposition.Typed);
    }

    private static PowerShellBoundStatement? BindStatement(
        ParsedSourceDocument document,
        StatementAst statement,
        IReadOnlyDictionary<string, PowerShellBoundParameter> parameters,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        bool isTerminal)
    {
        if (statement is ReturnStatementAst returnStatement)
        {
            var expression = returnStatement.Pipeline is null
                ? null
                : BindExpression(document, returnStatement.Pipeline, parameters, diagnostics);
            return returnStatement.Pipeline is null || expression is not null
                ? new PowerShellBoundReturnStatement(PowerShellSourceParser.GetSpan(document, returnStatement.Extent), expression)
                : null;
        }

        if (isTerminal && statement is PipelineAst pipeline &&
            pipeline.PipelineElements.Count == 1 &&
            pipeline.PipelineElements[0] is CommandExpressionAst)
        {
            var expression = BindExpression(document, pipeline, parameters, diagnostics);
            return expression is null
                ? null
                : new PowerShellBoundExpressionStatement(PowerShellSourceParser.GetSpan(document, statement.Extent), expression);
        }

        return null;
    }

    private static PowerShellBoundExpression? BindExpression(
        ParsedSourceDocument document,
        Ast syntax,
        IReadOnlyDictionary<string, PowerShellBoundParameter> parameters,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        syntax = UnwrapExpression(syntax);
        var span = PowerShellSourceParser.GetSpan(document, syntax.Extent);
        switch (syntax)
        {
            case StringConstantExpressionAst text:
                return new PowerShellBoundLiteralExpression(
                    span,
                    text.Value,
                    new PowerShellTypeFact(typeof(string), PowerShellTypeFactProvenance.Literal, "String literal syntax determines the CLR representation."),
                    PowerShellValueState.Known);
            case ConstantExpressionAst constant:
                return new PowerShellBoundLiteralExpression(
                    span,
                    constant.Value,
                    new PowerShellTypeFact(constant.Value?.GetType() ?? typeof(object), PowerShellTypeFactProvenance.Literal, "Literal syntax determines the CLR representation."),
                    constant.Value is null ? PowerShellValueState.Null : PowerShellValueState.Known);
            case VariableExpressionAst variable when variable.VariablePath.UserPath.Equals("true", StringComparison.OrdinalIgnoreCase):
                return new PowerShellBoundLiteralExpression(span, true, new PowerShellTypeFact(typeof(bool), PowerShellTypeFactProvenance.Literal, "$true is a Boolean literal."), PowerShellValueState.Known);
            case VariableExpressionAst variable when variable.VariablePath.UserPath.Equals("false", StringComparison.OrdinalIgnoreCase):
                return new PowerShellBoundLiteralExpression(span, false, new PowerShellTypeFact(typeof(bool), PowerShellTypeFactProvenance.Literal, "$false is a Boolean literal."), PowerShellValueState.Known);
            case VariableExpressionAst variable when variable.VariablePath.UserPath.Equals("null", StringComparison.OrdinalIgnoreCase):
                return new PowerShellBoundLiteralExpression(span, null, new PowerShellTypeFact(typeof(object), PowerShellTypeFactProvenance.Literal, "$null has no narrower CLR representation."), PowerShellValueState.Null);
            case VariableExpressionAst variable when parameters.TryGetValue(variable.VariablePath.UserPath, out var parameter):
                if (parameter.Type.Provenance == PowerShellTypeFactProvenance.Unknown)
                {
                    diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2102", $"Parameter '${variable.VariablePath.UserPath}' has no static type constraint.", span));
                    return null;
                }
                return new PowerShellBoundVariableExpression(span, parameter.Symbol, parameter.Type);
            default:
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    "PSB2101",
                    $"Expression '{syntax.GetType().Name}' is not yet represented by the bound pipeline.",
                    span));
                return null;
        }
    }

    private static Ast UnwrapExpression(Ast syntax)
    {
        while (true)
        {
            switch (syntax)
            {
                case PipelineAst pipeline when pipeline.PipelineElements.Count == 1 && pipeline.PipelineElements[0] is CommandExpressionAst command:
                    syntax = command.Expression;
                    continue;
                case CommandExpressionAst command:
                    syntax = command.Expression;
                    continue;
                case ParenExpressionAst parenthesized:
                    syntax = parenthesized.Pipeline;
                    continue;
                default:
                    return syntax;
            }
        }
    }
}
