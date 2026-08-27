using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Converts parser-owned PowerShell syntax into the compiler's neutral bound representation.
/// Parser objects are consumed here and never become part of a bound node.
/// </summary>
internal sealed class PowerShellSemanticBinder
{
    internal PowerShellBoundProgram Bind(IEnumerable<ParsedSourceDocument> documents)
    {
        if (documents is null) throw new ArgumentNullException(nameof(documents));
        var orderedDocuments = documents.OrderBy(static item => item.DocumentId, StringComparer.Ordinal).ToArray();
        var diagnostics = new List<PowerShellSemanticDiagnostic>();
        var declarations = DeclareFunctions(orderedDocuments, diagnostics);
        var functionsByName = declarations
            .GroupBy(static declaration => declaration.Syntax.Name, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() == 1)
            .ToDictionary(static group => group.Key, static group => group.Single().Symbol, StringComparer.OrdinalIgnoreCase);
        var functions = new List<PowerShellBoundFunction>();

        foreach (var declaration in declarations.OrderBy(static item => item.Symbol.StableKey, StringComparer.Ordinal))
        {
            if (declaration.Document.Errors.Length > 0 || !functionsByName.ContainsKey(declaration.Syntax.Name)) continue;
            var bound = BindFunction(declaration.Document, declaration.Syntax, declaration.Symbol, functionsByName, diagnostics);
            if (bound is not null) functions.Add(bound);
        }

        var boundDocuments = orderedDocuments.Select(document => new PowerShellBoundSourceDocument(
            document.DocumentId,
            document.Path,
            PowerShellSourceParser.GetSpan(document, document.SyntaxRoot.Extent),
            declarations.Where(declaration => declaration.Document.DocumentId == document.DocumentId)
                .Select(static declaration => declaration.Symbol)
                .OrderBy(static symbol => symbol.StableKey, StringComparer.Ordinal)
                .ToArray())).ToArray();

        return new PowerShellBoundProgram(
            boundDocuments,
            functions.OrderBy(static function => function.Symbol.StableKey, StringComparer.Ordinal).ToArray(),
            OrderDiagnostics(diagnostics));
    }

    private static FunctionDeclaration[] DeclareFunctions(
        IEnumerable<ParsedSourceDocument> documents,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var declarations = new List<FunctionDeclaration>();
        foreach (var document in documents)
        {
            foreach (var parseError in document.Errors)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    "PSB0001",
                    parseError.Message,
                    PowerShellSourceParser.GetSpan(document, parseError.Extent)));
            }
            if (document.Errors.Length > 0) continue;

            foreach (var function in document.SyntaxRoot
                         .FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
                         .Cast<FunctionDefinitionAst>())
            {
                var span = PowerShellSourceParser.GetSpan(document, function.Extent);
                declarations.Add(new FunctionDeclaration(
                    document,
                    function,
                    new PowerShellSymbolId(PowerShellSymbolKind.Function, document.DocumentId, function.Name, span)));
            }
        }

        foreach (var duplicate in declarations.GroupBy(static declaration => declaration.Syntax.Name, StringComparer.OrdinalIgnoreCase)
                     .Where(static group => group.Count() > 1))
        {
            foreach (var declaration in duplicate)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic(
                    "PSB1002",
                    $"Function '{declaration.Syntax.Name}' is declared more than once under PowerShell's case-insensitive naming rules.",
                    declaration.Symbol.Declaration));
            }
        }
        return declarations.ToArray();
    }

    private static PowerShellBoundFunction? BindFunction(
        ParsedSourceDocument document,
        FunctionDefinitionAst function,
        PowerShellSymbolId functionSymbol,
        IReadOnlyDictionary<string, PowerShellSymbolId> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var symbols = new Dictionary<string, SymbolBinding>(StringComparer.OrdinalIgnoreCase);
        var parameters = BindParameters(document, function, symbols, diagnostics);
        if (parameters is null) return null;
        var locals = DeclareLocals(document, function, symbols);

        var statements = new List<PowerShellBoundStatement>();
        var authoredStatements = function.Body.EndBlock?.Statements.ToArray() ?? Array.Empty<StatementAst>();
        for (var index = 0; index < authoredStatements.Length; index++)
        {
            var statement = authoredStatements[index];
            var diagnosticCount = diagnostics.Count;
            var bound = BindStatement(document, statement, symbols, functions, diagnostics, index == authoredStatements.Length - 1);
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
        var scopeSymbols = parameters.Select(static parameter => parameter.Symbol)
            .Concat(locals.Select(static local => local.Symbol))
            .OrderBy(static symbol => symbol.StableKey, StringComparer.Ordinal)
            .ToArray();
        return new PowerShellBoundFunction(
            functionSymbol,
            parameters,
            locals,
            new PowerShellLexicalScope(functionSymbol, scopeSymbols),
            body,
            PowerShellTypeFact.Unknown,
            PowerShellSemanticEffect.None,
            PowerShellRequiredCapability.None,
            PowerShellExecutionDisposition.Typed);
    }

    private static PowerShellBoundParameter[]? BindParameters(
        ParsedSourceDocument document,
        FunctionDefinitionAst function,
        IDictionary<string, SymbolBinding> symbols,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var parameters = new List<PowerShellBoundParameter>();
        foreach (var parameter in function.Body.ParamBlock?.Parameters.ToArray() ?? Array.Empty<ParameterAst>())
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
            var symbol = new PowerShellSymbolId(PowerShellSymbolKind.Parameter, document.DocumentId, name, span, function.Name + "/parameter/" + name);
            var bound = new PowerShellBoundParameter(symbol, type);
            symbols.Add(name, new SymbolBinding(symbol, type));
            parameters.Add(bound);
        }
        return parameters.ToArray();
    }

    private static PowerShellBoundLocal[] DeclareLocals(
        ParsedSourceDocument document,
        FunctionDefinitionAst function,
        IDictionary<string, SymbolBinding> symbols)
    {
        var locals = new List<PowerShellBoundLocal>();
        var assignments = (function.Body.EndBlock?.Statements.ToArray() ?? Array.Empty<StatementAst>())
            .SelectMany(static statement => statement.FindAll(static node => node is AssignmentStatementAst, searchNestedScriptBlocks: false))
            .Cast<AssignmentStatementAst>()
            .OrderBy(static assignment => assignment.Extent.StartOffset);
        foreach (var assignment in assignments)
        {
            var variable = PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left);
            if (variable is null) continue;
            var name = variable.VariablePath.UserPath;
            if (symbols.ContainsKey(name)) continue;
            var span = PowerShellSourceParser.GetSpan(document, variable.Extent);
            var type = ResolveAssignmentType(assignment);
            var symbol = new PowerShellSymbolId(PowerShellSymbolKind.Local, document.DocumentId, name, span, function.Name + "/local/" + name);
            var local = new PowerShellBoundLocal(symbol, type);
            symbols.Add(name, new SymbolBinding(symbol, type));
            locals.Add(local);
        }
        return locals.ToArray();
    }

    private static PowerShellTypeFact ResolveAssignmentType(AssignmentStatementAst assignment)
    {
        if (assignment.Left is ConvertExpressionAst typedLeft && typedLeft.StaticType != typeof(object))
            return new PowerShellTypeFact(typedLeft.StaticType, PowerShellTypeFactProvenance.Explicit, "The assignment target has an authored type constraint.");
        var expression = UnwrapExpression(assignment.Right);
        if (expression is ConvertExpressionAst conversion && conversion.StaticType != typeof(object))
            return new PowerShellTypeFact(conversion.StaticType, PowerShellTypeFactProvenance.Explicit, "The assignment value has an authored conversion.");
        if (expression is ExpressionAst typedExpression && typedExpression.StaticType != typeof(object))
            return new PowerShellTypeFact(typedExpression.StaticType, PowerShellTypeFactProvenance.Inferred, "The first assignment provides a static CLR type.");
        return PowerShellTypeFact.Unknown;
    }

    private static PowerShellBoundStatement? BindStatement(
        ParsedSourceDocument document,
        StatementAst statement,
        IReadOnlyDictionary<string, SymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellSymbolId> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        bool isTerminal)
    {
        if (statement is AssignmentStatementAst assignment)
        {
            if (assignment.Operator != TokenKind.Equals) return null;
            var variable = PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left);
            if (variable is null || !symbols.TryGetValue(variable.VariablePath.UserPath, out var target)) return null;
            var value = BindExpression(document, assignment.Right, symbols, functions, diagnostics);
            return value is null
                ? null
                : new PowerShellBoundAssignmentStatement(PowerShellSourceParser.GetSpan(document, statement.Extent), target.Symbol, value);
        }
        if (statement is ReturnStatementAst returnStatement)
        {
            var expression = returnStatement.Pipeline is null
                ? null
                : BindExpression(document, returnStatement.Pipeline, symbols, functions, diagnostics);
            return returnStatement.Pipeline is null || expression is not null
                ? new PowerShellBoundReturnStatement(PowerShellSourceParser.GetSpan(document, returnStatement.Extent), expression)
                : null;
        }
        if (statement is PipelineAst pipeline && (isTerminal || IsLocalFunctionPipeline(pipeline, functions)))
        {
            var expression = BindExpression(document, pipeline, symbols, functions, diagnostics);
            return expression is null
                ? null
                : new PowerShellBoundExpressionStatement(PowerShellSourceParser.GetSpan(document, statement.Extent), expression);
        }
        return null;
    }

    private static PowerShellBoundExpression? BindExpression(
        ParsedSourceDocument document,
        Ast syntax,
        IReadOnlyDictionary<string, SymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellSymbolId> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        syntax = UnwrapExpression(syntax);
        var span = PowerShellSourceParser.GetSpan(document, syntax.Extent);
        switch (syntax)
        {
            case StringConstantExpressionAst text:
                return new PowerShellBoundLiteralExpression(span, text.Value, LiteralType(typeof(string), "String literal syntax determines the CLR representation."), PowerShellValueState.Known);
            case ConstantExpressionAst constant:
                return new PowerShellBoundLiteralExpression(span, constant.Value, LiteralType(constant.Value?.GetType() ?? typeof(object), "Literal syntax determines the CLR representation."), constant.Value is null ? PowerShellValueState.Null : PowerShellValueState.Known);
            case VariableExpressionAst variable when variable.VariablePath.UserPath.Equals("true", StringComparison.OrdinalIgnoreCase):
                return new PowerShellBoundLiteralExpression(span, true, LiteralType(typeof(bool), "$true is a Boolean literal."), PowerShellValueState.Known);
            case VariableExpressionAst variable when variable.VariablePath.UserPath.Equals("false", StringComparison.OrdinalIgnoreCase):
                return new PowerShellBoundLiteralExpression(span, false, LiteralType(typeof(bool), "$false is a Boolean literal."), PowerShellValueState.Known);
            case VariableExpressionAst variable when variable.VariablePath.UserPath.Equals("null", StringComparison.OrdinalIgnoreCase):
                return new PowerShellBoundLiteralExpression(span, null, LiteralType(typeof(object), "$null has no narrower CLR representation."), PowerShellValueState.Null);
            case VariableExpressionAst variable when symbols.TryGetValue(variable.VariablePath.UserPath, out var symbol):
                return new PowerShellBoundVariableExpression(span, symbol.Symbol, symbol.Type);
            case ConvertExpressionAst conversion:
            {
                var operand = BindExpression(document, conversion.Child, symbols, functions, diagnostics);
                return operand is null
                    ? null
                    : new PowerShellBoundConversionExpression(span, new PowerShellTypeFact(conversion.StaticType, PowerShellTypeFactProvenance.Explicit, "An authored conversion selects the CLR representation."), operand);
            }
            case CommandAst command when TryGetLocalFunction(command, functions, out var target):
            {
                var arguments = new List<PowerShellBoundExpression>();
                foreach (var argument in command.CommandElements.Skip(1).OfType<ExpressionAst>())
                {
                    var bound = BindExpression(document, argument, symbols, functions, diagnostics);
                    if (bound is null) return null;
                    arguments.Add(bound);
                }
                return new PowerShellBoundInvocationExpression(span, target, arguments.ToArray(), PowerShellTypeFact.Unknown);
            }
            default:
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2101", $"Expression '{syntax.GetType().Name}' is not yet represented by the bound pipeline.", span));
                return null;
        }
    }

    private static bool IsLocalFunctionPipeline(PipelineAst pipeline, IReadOnlyDictionary<string, PowerShellSymbolId> functions)
        => pipeline.PipelineElements.Count == 1 && pipeline.PipelineElements[0] is CommandAst command && TryGetLocalFunction(command, functions, out _);

    private static bool TryGetLocalFunction(CommandAst command, IReadOnlyDictionary<string, PowerShellSymbolId> functions, out PowerShellSymbolId target)
    {
        var name = command.GetCommandName();
        if (!string.IsNullOrWhiteSpace(name) && functions.TryGetValue(name, out target!)) return true;
        target = null!;
        return false;
    }

    private static PowerShellTypeFact LiteralType(Type type, string explanation)
        => new(type, PowerShellTypeFactProvenance.Literal, explanation);

    private static Ast UnwrapExpression(Ast syntax)
    {
        while (true)
        {
            switch (syntax)
            {
                case PipelineAst pipeline when pipeline.PipelineElements.Count == 1 && pipeline.PipelineElements[0] is CommandExpressionAst command:
                    syntax = command.Expression;
                    continue;
                case PipelineAst pipeline when pipeline.PipelineElements.Count == 1 && pipeline.PipelineElements[0] is CommandAst command:
                    return command;
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

    private static PowerShellSemanticDiagnostic[] OrderDiagnostics(IEnumerable<PowerShellSemanticDiagnostic> diagnostics)
        => diagnostics.OrderBy(static diagnostic => diagnostic.Span.DocumentId, StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.Span.StartOffset)
            .ThenBy(static diagnostic => diagnostic.Code, StringComparer.Ordinal)
            .ToArray();

    private sealed class FunctionDeclaration
    {
        internal FunctionDeclaration(ParsedSourceDocument document, FunctionDefinitionAst syntax, PowerShellSymbolId symbol)
        {
            Document = document;
            Syntax = syntax;
            Symbol = symbol;
        }

        internal ParsedSourceDocument Document { get; }
        internal FunctionDefinitionAst Syntax { get; }
        internal PowerShellSymbolId Symbol { get; }
    }

    private sealed class SymbolBinding
    {
        internal SymbolBinding(PowerShellSymbolId symbol, PowerShellTypeFact type)
        {
            Symbol = symbol;
            Type = type;
        }

        internal PowerShellSymbolId Symbol { get; }
        internal PowerShellTypeFact Type { get; }
    }
}
