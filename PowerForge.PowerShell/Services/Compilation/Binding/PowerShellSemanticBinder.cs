using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Converts parser-owned PowerShell syntax into the compiler's neutral bound representation.
/// Parser objects are consumed here and never become part of a bound node.
/// </summary>
internal sealed partial class PowerShellSemanticBinder
{
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
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        string? targetFramework,
        PowerShellCompilationCapability capabilities)
    {
        if (!PowerShellOutputTypeSemanticPolicy.TryResolve(
                function.Body,
                targetFramework,
                capabilities,
                out var declaredOutputType,
                out var outputTypeErrorNode,
                out var outputTypeError))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB1201",
                outputTypeError!,
                PowerShellSourceParser.GetSpan(document, outputTypeErrorNode!.Extent)));
            return null;
        }
        var symbols = new Dictionary<string, PowerShellSemanticSymbolBinding>(StringComparer.OrdinalIgnoreCase);
        var parameters = BindParameters(document, function, symbols, diagnostics, targetFramework);
        if (parameters is null) return null;
        if (FindForInitializerScopeLeak(function.Body) is { } scopeLeak)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB1202",
                $"Local '${scopeLeak.VariablePath.UserPath}' is declared in a for initializer and then used outside the loop scope; it must be declared at function scope before typed compilation.",
                PowerShellSourceParser.GetSpan(document, scopeLeak.Extent)));
            return null;
        }
        var authoredStatements = function.Body.EndBlock?.Statements.ToArray() ?? Array.Empty<StatementAst>();
        var localFunctionNames = functions.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var runtimeTailStart = capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStreams)
            ? PowerShellCommandIslandPolicy.FindRuntimeTailStart(authoredStatements, function.Body, localFunctionNames)
            : -1;
        var runtimeTailOffset = runtimeTailStart >= 0 ? authoredStatements[runtimeTailStart].Extent.StartOffset : (int?)null;
        var locals = DeclareLocals(document, function, symbols, capabilities, runtimeTailOffset);
        var parametersByName = parameters.ToDictionary(static parameter => parameter.Symbol.Name, StringComparer.OrdinalIgnoreCase);

        var statements = new List<PowerShellBoundStatement>();
        for (var index = 0; index < authoredStatements.Length; index++)
        {
            var statement = authoredStatements[index];
            if (capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStreams) &&
                PowerShellHostedStatementBinder.TryBind(
                    document,
                    authoredStatements,
                    function.Body,
                    localFunctionNames,
                    symbols,
                    parametersByName,
                    runtimeTailStart,
                    ref index,
                    out var hosted))
            {
                statements.Add(hosted!);
                continue;
            }
            var diagnosticCount = diagnostics.Count;
            var bound = BindStatement(document, statement, symbols, functions, diagnostics, index == authoredStatements.Length - 1, targetFramework, capabilities);
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

        var refinedTypes = symbols.Values.ToDictionary(static binding => binding.Symbol.StableKey, static binding => binding.Type, StringComparer.Ordinal);
        locals = locals.Select(local => new PowerShellBoundLocal(local.Symbol, refinedTypes[local.Symbol.StableKey])).ToArray();

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
            PowerShellCommentHelpBinder.Bind(function),
            declaredOutputType,
            body,
            PowerShellTypeFact.Unknown,
            PowerShellOutputCardinality.Unknown,
            PowerShellSemanticEffect.None,
            PowerShellRequiredCapability.None,
            PowerShellExecutionDisposition.Typed);
    }

    private static PowerShellBoundParameter[]? BindParameters(
        ParsedSourceDocument document,
        FunctionDefinitionAst function,
        IDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        string? targetFramework)
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

            var contract = PowerShellParameterContractBinder.Bind(parameter, targetFramework);
            var clrType = parameter.StaticType == typeof(System.Management.Automation.SwitchParameter)
                ? typeof(bool)
                : parameter.StaticType;
            var hasAuthoredType = parameter.Attributes.OfType<TypeConstraintAst>().Any();
            var type = clrType == typeof(object) && !hasAuthoredType
                ? PowerShellTypeFact.Unknown
                : new PowerShellTypeFact(
                    clrType,
                    PowerShellTypeFactProvenance.Explicit,
                    parameter.StaticType == typeof(System.Management.Automation.SwitchParameter)
                        ? $"Parameter '${name}' has an authored SwitchParameter contract represented as Boolean only when its object identity is not observed."
                        : $"Parameter '${name}' has an authored type constraint.");
            var symbol = new PowerShellSymbolId(PowerShellSymbolKind.Parameter, document.DocumentId, name, span, function.Name + "/parameter/" + name);
            var bound = new PowerShellBoundParameter(symbol, type, contract);
            symbols.Add(name, new PowerShellSemanticSymbolBinding(symbol, type));
            parameters.Add(bound);
        }
        return parameters.ToArray();
    }

    private static PowerShellBoundLocal[] DeclareLocals(
        ParsedSourceDocument document,
        FunctionDefinitionAst function,
        IDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        PowerShellCompilationCapability capabilities,
        int? excludedTailOffset = null)
    {
        var locals = new List<PowerShellBoundLocal>();
        var assignments = (function.Body.EndBlock?.Statements.ToArray() ?? Array.Empty<StatementAst>())
            .SelectMany(static statement => statement.FindAll(static node => node is AssignmentStatementAst, searchNestedScriptBlocks: false))
            .Cast<AssignmentStatementAst>()
            .OrderBy(static assignment => assignment.Extent.StartOffset);
        foreach (var assignment in assignments)
        {
            if (excludedTailOffset.HasValue && assignment.Extent.StartOffset >= excludedTailOffset.Value) continue;
            var variable = PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left);
            if (variable is null) continue;
            var name = variable.VariablePath.UserPath;
            if (name.Equals("null", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("false", StringComparison.OrdinalIgnoreCase) ||
                name.Equals(PowerShellBoundParametersPolicy.VariableName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (symbols.ContainsKey(name)) continue;
            var span = PowerShellSourceParser.GetSpan(document, variable.Extent);
            var type = ResolveAssignmentType(assignment, capabilities);
            var symbol = new PowerShellSymbolId(PowerShellSymbolKind.Local, document.DocumentId, name, span, function.Name + "/local/" + name);
            var local = new PowerShellBoundLocal(symbol, type);
            symbols.Add(name, new PowerShellSemanticSymbolBinding(symbol, type));
            locals.Add(local);
        }
        var loops = (function.Body.EndBlock?.Statements.ToArray() ?? Array.Empty<StatementAst>())
            .SelectMany(static statement => statement.FindAll(static node => node is ForEachStatementAst, searchNestedScriptBlocks: false))
            .Cast<ForEachStatementAst>()
            .OrderBy(static loop => loop.Extent.StartOffset);
        foreach (var loop in loops)
        {
            var name = loop.Variable.VariablePath.UserPath;
            if (symbols.ContainsKey(name)) continue;
            var condition = UnwrapExpression(loop.Condition);
            Type? collectionType = condition switch
            {
                VariableExpressionAst variable when symbols.TryGetValue(variable.VariablePath.UserPath, out var binding) => binding.Type.ClrType,
                ExpressionAst expression when expression.StaticType != typeof(object) => expression.StaticType,
                _ => null
            };
            var elementType = collectionType is { IsArray: true } && collectionType.GetArrayRank() == 1
                ? collectionType.GetElementType()
                : collectionType == typeof(string) ? typeof(string) : null;
            if (elementType is null) continue;
            var span = PowerShellSourceParser.GetSpan(document, loop.Variable.Extent);
            var type = new PowerShellTypeFact(elementType, PowerShellTypeFactProvenance.Inferred, "The foreach collection provides one stable CLR element type.");
            var symbol = new PowerShellSymbolId(PowerShellSymbolKind.Local, document.DocumentId, name, span, function.Name + "/foreach/" + loop.Extent.StartOffset.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/" + name);
            symbols.Add(name, new PowerShellSemanticSymbolBinding(symbol, type));
            locals.Add(new PowerShellBoundLocal(symbol, type));
        }
        return locals.ToArray();
    }

    private static PowerShellTypeFact ResolveAssignmentType(
        AssignmentStatementAst assignment,
        PowerShellCompilationCapability capabilities)
    {
        var expression = UnwrapExpression(assignment.Right);
        var hostedObjects = capabilities.HasFlag(PowerShellCompilationCapability.PowerShellObjects);
        if (assignment.Left is ConvertExpressionAst typedDictionary &&
            expression is HashtableAst &&
            (typedDictionary.StaticType == typeof(System.Collections.Hashtable) ||
             typedDictionary.StaticType == typeof(System.Collections.IDictionary)))
            return new PowerShellTypeFact(
                hostedObjects ? typeof(System.Collections.Hashtable) : typeof(Dictionary<string, string>),
                PowerShellTypeFactProvenance.Explicit,
                hostedObjects
                    ? "A hosted typed hashtable literal preserves heterogeneous object values."
                    : "A typed hashtable literal selects the compiler's homogeneous String dictionary representation.");
        if (assignment.Left is ConvertExpressionAst typedLeft)
            return new PowerShellTypeFact(typedLeft.StaticType, PowerShellTypeFactProvenance.Explicit, "The assignment target has an authored type constraint.");
        if (expression is HashtableAst)
        {
            var hashtable = (HashtableAst)expression;
            var objectValues = PowerShellDictionarySemanticBinder.UsesHostedObjectRepresentation(hashtable, contextualType: null, capabilities);
            return new PowerShellTypeFact(
                objectValues ? typeof(System.Collections.Hashtable) : typeof(Dictionary<string, string>),
                PowerShellTypeFactProvenance.Inferred,
                objectValues
                    ? "A hosted hashtable literal preserves heterogeneous object values."
                    : "A homogeneous hashtable literal selects the compiler's string dictionary representation.");
        }
        if (expression is ConvertExpressionAst ordered && PowerShellDictionarySemanticBinder.IsOrderedHashtableConversion(ordered))
            return new PowerShellTypeFact(typeof(System.Collections.Specialized.OrderedDictionary), PowerShellTypeFactProvenance.Explicit, "An [ordered] literal selects OrderedDictionary representation.");
        if (expression is ConvertExpressionAst conversion && conversion.StaticType != typeof(object))
            return new PowerShellTypeFact(conversion.StaticType, PowerShellTypeFactProvenance.Explicit, "The assignment value has an authored conversion.");
        if (expression is ExpressionAst typedExpression && typedExpression.StaticType != typeof(object))
            return new PowerShellTypeFact(typedExpression.StaticType, PowerShellTypeFactProvenance.Inferred, "The first assignment provides a static CLR type.");
        return PowerShellTypeFact.Unknown;
    }

    private static VariableExpressionAst? FindForInitializerScopeLeak(ScriptBlockAst body)
    {
        foreach (var loop in body.FindAll(static node => node is ForStatementAst, searchNestedScriptBlocks: false).OfType<ForStatementAst>())
        {
            if (loop.Initializer is not AssignmentStatementAst initializer ||
                PowerShellAssignmentTargetPolicy.FindDirectVariable(initializer.Left) is not { } declared)
                continue;
            var leak = body.FindAll(node =>
                    node is VariableExpressionAst variable &&
                    variable.Extent.StartOffset > loop.Extent.EndOffset &&
                    variable.VariablePath.UserPath.Equals(declared.VariablePath.UserPath, StringComparison.OrdinalIgnoreCase) &&
                    !PowerShellAssignmentTargetPolicy.IsDirectAssignmentTarget(variable),
                    searchNestedScriptBlocks: false)
                .OfType<VariableExpressionAst>()
                .FirstOrDefault();
            if (leak is not null) return leak;
        }
        return null;
    }

    private static PowerShellBoundStatement? BindStatement(
        ParsedSourceDocument document,
        StatementAst statement,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        bool isTerminal,
        string? targetFramework,
        PowerShellCompilationCapability capabilities)
    {
        if (statement is AssignmentStatementAst assignment)
        {
            if (PowerShellAssignmentTargetPolicy.FindDirectVariable(assignment.Left) is { } discarded &&
                discarded.VariablePath.UserPath.Equals("null", StringComparison.OrdinalIgnoreCase))
            {
                if (assignment.Operator.ToString() != "Equals")
                {
                    diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2405", "The $null discard target supports simple '=' assignment only.", PowerShellSourceParser.GetSpan(document, assignment.Extent)));
                    return null;
                }
                var discardedValue = BindExpression(document, assignment.Right, symbols, functions, diagnostics, targetFramework: targetFramework, capabilities: capabilities);
                return discardedValue is null
                    ? null
                    : new PowerShellBoundExpressionStatement(PowerShellSourceParser.GetSpan(document, assignment.Extent), discardedValue, emitsOutput: false);
            }
            if (assignment.Left is IndexExpressionAst index)
            {
                return PowerShellDictionarySemanticBinder.BindAssignment(
                    document,
                    assignment,
                    index,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    capabilities,
                    diagnostics);
            }
            if (assignment.Left is MemberExpressionAst member)
            {
                return PowerShellClrMemberSemanticBinder.BindAssignment(
                    document,
                    assignment,
                    member,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    targetFramework,
                    capabilities,
                    diagnostics);
            }
            var mutation = PowerShellMutationSemanticBinder.BindAssignment(
                document,
                assignment,
                symbols,
                (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                diagnostics);
            return mutation is null
                ? null
                : new PowerShellBoundAssignmentStatement(
                    mutation.Span,
                    mutation.Target,
                    mutation.Value!,
                    mutation.Operation,
                    mutation.NormalizeNullString,
                    mutation.CheckedIntegral);
        }
        if (statement is ReturnStatementAst returnStatement)
        {
            var expression = returnStatement.Pipeline is null
                ? null
                : BindExpression(document, returnStatement.Pipeline, symbols, functions, diagnostics, targetFramework: targetFramework, capabilities: capabilities);
            return returnStatement.Pipeline is null || expression is not null
                ? new PowerShellBoundReturnStatement(
                    PowerShellSourceParser.GetSpan(document, returnStatement.Extent),
                    expression,
                    expression is not PowerShellBoundMutationExpression && expression?.Type.ClrType != typeof(void))
                : null;
        }
        if (statement is IfStatementAst ifStatement)
        {
            var clauses = new List<PowerShellBoundConditionalClause>();
            foreach (var clause in ifStatement.Clauses)
            {
                var condition = BindExpression(document, clause.Item1, symbols, functions, diagnostics, targetFramework: targetFramework, capabilities: capabilities);
                if (condition is null) return null;
                condition = BindConditionTruthiness(condition, capabilities, diagnostics);
                if (condition is null) return null;
                var body = BindBlock(document, clause.Item2, symbols, functions, diagnostics, targetFramework, capabilities);
                if (body is null) return null;
                clauses.Add(new PowerShellBoundConditionalClause(condition, body));
            }
            var elseBlock = ifStatement.ElseClause is null
                ? null
                : BindBlock(document, ifStatement.ElseClause, symbols, functions, diagnostics, targetFramework, capabilities);
            if (ifStatement.ElseClause is not null && elseBlock is null) return null;
            return new PowerShellBoundIfStatement(PowerShellSourceParser.GetSpan(document, statement.Extent), clauses.ToArray(), elseBlock);
        }
        if (statement is WhileStatementAst whileStatement)
        {
            var condition = BindExpression(document, whileStatement.Condition, symbols, functions, diagnostics, targetFramework: targetFramework, capabilities: capabilities);
            if (condition is null) return null;
            condition = BindConditionTruthiness(condition, capabilities, diagnostics);
            if (condition is null) return null;
            var body = BindBlock(document, whileStatement.Body, symbols, functions, diagnostics, targetFramework, capabilities);
            return body is null ? null : new PowerShellBoundWhileStatement(PowerShellSourceParser.GetSpan(document, statement.Extent), condition, body);
        }
        if (statement is ForStatementAst forStatement)
        {
            var initializer = forStatement.Initializer is null
                ? null
                : BindExpression(document, forStatement.Initializer, symbols, functions, diagnostics, targetFramework: targetFramework, capabilities: capabilities) as PowerShellBoundMutationExpression;
            if (forStatement.Initializer is not null && initializer is null) return null;
            var condition = forStatement.Condition is null
                ? null
                : BindExpression(document, forStatement.Condition, symbols, functions, diagnostics, targetFramework: targetFramework, capabilities: capabilities);
            if (condition is not null) condition = BindConditionTruthiness(condition, capabilities, diagnostics);
            if (forStatement.Condition is not null && condition is null) return null;
            var iterator = forStatement.Iterator is null
                ? null
                : BindExpression(document, forStatement.Iterator, symbols, functions, diagnostics, targetFramework: targetFramework, capabilities: capabilities) as PowerShellBoundMutationExpression;
            if (forStatement.Iterator is not null && iterator is null) return null;
            var body = BindBlock(document, forStatement.Body, symbols, functions, diagnostics, targetFramework, capabilities);
            return body is null ? null : new PowerShellBoundForStatement(PowerShellSourceParser.GetSpan(document, statement.Extent), initializer, condition, iterator, body);
        }
        if (statement is ForEachStatementAst forEachStatement)
        {
            var variableSpan = PowerShellSourceParser.GetSpan(document, forEachStatement.Variable.Extent);
            if (!symbols.TryGetValue(forEachStatement.Variable.VariablePath.UserPath, out var target) ||
                target.Symbol.Declaration.StartOffset != variableSpan.StartOffset)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2302", $"foreach variable '${forEachStatement.Variable.VariablePath.UserPath}' cannot reuse another function-scope variable on the conservative compilation path.", variableSpan));
                return null;
            }
            var collection = BindExpression(document, forEachStatement.Condition, symbols, functions, diagnostics, targetFramework: targetFramework, capabilities: capabilities);
            if (collection is null) return null;
            var collectionType = collection.Type.ClrType;
            var scalarString = collectionType == typeof(string) && collection.Type.Provenance is PowerShellTypeFactProvenance.Explicit or PowerShellTypeFactProvenance.Literal;
            var elementType = collectionType.IsArray && collectionType.GetArrayRank() == 1
                ? collectionType.GetElementType()
                : scalarString ? typeof(string) : null;
            if (elementType is null || elementType != target.Type.ClrType)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2303", "foreach requires a statically typed one-dimensional array or explicitly typed scalar string.", collection.Span));
                return null;
            }
            var body = BindBlock(document, forEachStatement.Body, symbols, functions, diagnostics, targetFramework, capabilities);
            return body is null ? null : new PowerShellBoundForEachStatement(PowerShellSourceParser.GetSpan(document, statement.Extent), target.Symbol, elementType, collection, scalarString, body);
        }
        if (statement is SwitchStatementAst switchStatement)
        {
            if ((switchStatement.Flags & (SwitchFlags.File | SwitchFlags.Regex | SwitchFlags.Wildcard | SwitchFlags.Parallel)) != 0)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2304", $"Switch flags '{switchStatement.Flags}' require PowerShell runtime matching semantics.", PowerShellSourceParser.GetSpan(document, switchStatement.Extent)));
                return null;
            }
            var value = BindExpression(document, switchStatement.Condition, symbols, functions, diagnostics, targetFramework: targetFramework, capabilities: capabilities);
            if (value is null) return null;
            var valueType = value.Type.ClrType;
            if (valueType != typeof(bool) && valueType != typeof(char) && valueType != typeof(string) && !PowerShellClrTypeSemantics.IsNumeric(valueType))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2305", $"Scalar switch requires a Boolean, character, string, or numeric condition; resolved type was '{valueType.FullName}'.", value.Span));
                return null;
            }
            var clauses = new List<PowerShellBoundSwitchClause>();
            foreach (var clause in switchStatement.Clauses)
            {
                var clauseValue = BindExpression(document, clause.Item1, symbols, functions, diagnostics, valueType, targetFramework, capabilities);
                if (clauseValue is null) return null;
                if (clauseValue.Type.ClrType != valueType)
                {
                    diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2306", $"Scalar switch clause type '{clauseValue.Type.ClrType.FullName}' must exactly match condition type '{valueType.FullName}' to avoid PowerShell coercion semantics.", clauseValue.Span));
                    return null;
                }
                var body = BindBlock(document, clause.Item2, symbols, functions, diagnostics, targetFramework, capabilities);
                if (body is null) return null;
                clauses.Add(new PowerShellBoundSwitchClause(clauseValue, body));
            }
            var defaultBlock = switchStatement.Default is null
                ? null
                : BindBlock(document, switchStatement.Default, symbols, functions, diagnostics, targetFramework, capabilities);
            if (switchStatement.Default is not null && defaultBlock is null) return null;
            return new PowerShellBoundSwitchStatement(
                PowerShellSourceParser.GetSpan(document, statement.Extent),
                value,
                clauses.ToArray(),
                defaultBlock,
                (switchStatement.Flags & SwitchFlags.CaseSensitive) != 0);
        }
        if (statement is ThrowStatementAst throwStatement)
        {
            if (throwStatement.IsRethrow)
            {
                if (!PowerShellControlFlowBindingPolicy.HasAncestor<CatchClauseAst>(throwStatement))
                {
                    diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2307", "A bare typed rethrow is valid only inside a catch clause.", PowerShellSourceParser.GetSpan(document, throwStatement.Extent)));
                    return null;
                }
                return new PowerShellBoundThrowStatement(PowerShellSourceParser.GetSpan(document, statement.Extent), null);
            }
            if (throwStatement.Pipeline is null) return null;
            var expression = BindExpression(document, throwStatement.Pipeline, symbols, functions, diagnostics, targetFramework: targetFramework, capabilities: capabilities);
            if (expression is null) return null;
            if (!typeof(Exception).IsAssignableFrom(expression.Type.ClrType))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2308", $"Typed throw requires a CLR exception expression; resolved type was '{expression.Type.ClrType.FullName}'.", expression.Span));
                return null;
            }
            return new PowerShellBoundThrowStatement(PowerShellSourceParser.GetSpan(document, statement.Extent), expression);
        }
        if (statement is TryStatementAst tryStatement)
        {
            if (tryStatement.Finally?.FindAll(static node => node is ReturnStatementAst or BreakStatementAst or ContinueStatementAst, searchNestedScriptBlocks: true).Any() == true)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2309", "Typed finally blocks cannot alter enclosing return, break, or continue control flow.", PowerShellSourceParser.GetSpan(document, tryStatement.Finally.Extent)));
                return null;
            }
            var body = BindBlock(document, tryStatement.Body, symbols, functions, diagnostics, targetFramework, capabilities);
            if (body is null) return null;
            var catches = new List<PowerShellBoundCatchClause>();
            foreach (var clause in tryStatement.CatchClauses)
            {
                var types = new List<Type>();
                foreach (var constraint in clause.CatchTypes)
                {
                    var type = constraint.TypeName.GetReflectionType();
                    var supportedPowerShellRuntimeException = type == typeof(System.Management.Automation.RuntimeException) &&
                                                               capabilities.HasFlag(PowerShellCompilationCapability.PowerShellObjects);
                    if (type is null || !typeof(Exception).IsAssignableFrom(type) ||
                        !supportedPowerShellRuntimeException && !PowerShellGeneratedTypePolicy.IsSupported(type, targetFramework))
                    {
                        diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2310", $"Typed catch '{constraint.TypeName.FullName}' is outside the generated project reference set.", PowerShellSourceParser.GetSpan(document, constraint.Extent)));
                        return null;
                    }
                    types.Add(type);
                }
                var catchBody = BindBlock(document, clause.Body, symbols, functions, diagnostics, targetFramework, capabilities);
                if (catchBody is null) return null;
                catches.Add(new PowerShellBoundCatchClause(types.ToArray(), catchBody));
            }
            var catchAll = catches.FindIndex(static clause => clause.ExceptionTypes.Length == 0);
            if (catchAll >= 0 && catchAll != catches.Count - 1)
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2311", "A catch-all clause must follow all typed catches on the conservative typed path.", PowerShellSourceParser.GetSpan(document, tryStatement.CatchClauses[catchAll].Extent)));
                return null;
            }
            var flattened = catches.SelectMany((clause, clauseIndex) =>
                clause.ExceptionTypes.Select(type => new { ClauseIndex = clauseIndex, Type = type })).ToArray();
            for (var index = 0; index < flattened.Length; index++)
            {
                if (flattened.Take(index).Any(previous => previous.Type.IsAssignableFrom(flattened[index].Type)))
                {
                    diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2312", $"Typed catch '{flattened[index].Type.FullName}' is unreachable after a broader earlier catch.", PowerShellSourceParser.GetSpan(document, tryStatement.CatchClauses[flattened[index].ClauseIndex].Extent)));
                    return null;
                }
            }
            var finallyBlock = tryStatement.Finally is null
                ? null
                : BindBlock(document, tryStatement.Finally, symbols, functions, diagnostics, targetFramework, capabilities);
            if (tryStatement.Finally is not null && finallyBlock is null) return null;
            return new PowerShellBoundTryStatement(PowerShellSourceParser.GetSpan(document, statement.Extent), body, catches.ToArray(), finallyBlock);
        }
        if (statement is BreakStatementAst { Label: null } breakStatement && PowerShellControlFlowBindingPolicy.HasBreakableAncestor(breakStatement))
            return new PowerShellBoundBreakStatement(PowerShellSourceParser.GetSpan(document, statement.Extent));
        if (statement is ContinueStatementAst { Label: null } continueStatement && PowerShellControlFlowBindingPolicy.HasContinuableAncestor(continueStatement))
            return new PowerShellBoundContinueStatement(PowerShellSourceParser.GetSpan(document, statement.Extent));
        if (statement is BreakStatementAst labeledBreak && labeledBreak.Label is not null)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2313",
                "Labeled break is not supported by the typed compiler.",
                PowerShellSourceParser.GetSpan(document, labeledBreak.Extent)));
            return null;
        }
        if (statement is BreakStatementAst invalidBreak)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2314",
                "break must be inside a supported loop or scalar switch.",
                PowerShellSourceParser.GetSpan(document, invalidBreak.Extent)));
            return null;
        }
        if (statement is ContinueStatementAst labeledContinue && labeledContinue.Label is not null)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2315",
                "Labeled continue is not supported by the typed compiler.",
                PowerShellSourceParser.GetSpan(document, labeledContinue.Extent)));
            return null;
        }
        if (statement is ContinueStatementAst invalidContinue)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2316",
                "continue must be inside a supported loop.",
                PowerShellSourceParser.GetSpan(document, invalidContinue.Extent)));
            return null;
        }
        if (statement is PipelineAst { PipelineElements.Count: 1 } streamPipeline &&
            streamPipeline.PipelineElements[0] is CommandAst streamCommand &&
            capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStreams) &&
            PowerShellCommandIslandPolicy.TryGetStreamCommand(streamCommand, out var streamKind, out var messageSyntax))
        {
            var message = BindExpression(document, messageSyntax, symbols, functions, diagnostics, typeof(string), targetFramework, capabilities);
            return message is null
                ? null
                : new PowerShellBoundStreamWriteStatement(PowerShellSourceParser.GetSpan(document, statement.Extent), streamKind, message);
        }
        if (statement is PipelineAst pipeline)
        {
            var expression = BindExpression(document, pipeline, symbols, functions, diagnostics, targetFramework: targetFramework, capabilities: capabilities);
            if (expression is null) return null;
            var emitsOutput = expression is not PowerShellBoundMutationExpression && expression.Type.ClrType != typeof(void);
            if (!isTerminal && emitsOutput && !IsLocalFunctionPipeline(pipeline, functions)) return null;
            if (isTerminal && IsLocalFunctionPipeline(pipeline, functions))
                return new PowerShellBoundReturnStatement(PowerShellSourceParser.GetSpan(document, statement.Extent), expression, emitsOutput);
            return expression is null
                ? null
                : new PowerShellBoundExpressionStatement(PowerShellSourceParser.GetSpan(document, statement.Extent), expression, emitsOutput);
        }
        return null;
    }

    private static PowerShellBoundBlock? BindBlock(
        ParsedSourceDocument document,
        StatementBlockAst syntax,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        string? targetFramework,
        PowerShellCompilationCapability capabilities)
    {
        var statements = new List<PowerShellBoundStatement>();
        foreach (var statement in syntax.Statements)
        {
            var diagnosticCount = diagnostics.Count;
            var bound = BindStatement(document, statement, symbols, functions, diagnostics, isTerminal: false, targetFramework, capabilities);
            if (bound is null)
            {
                if (diagnostics.Count == diagnosticCount)
                    diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2001", $"Statement '{statement.GetType().Name}' is not yet represented by the bound pipeline.", PowerShellSourceParser.GetSpan(document, statement.Extent)));
                return null;
            }
            statements.Add(bound);
        }
        return new PowerShellBoundBlock(PowerShellSourceParser.GetSpan(document, syntax.Extent), statements.ToArray());
    }

    private static PowerShellBoundExpression? BindExpression(
        ParsedSourceDocument document,
        Ast syntax,
        IReadOnlyDictionary<string, PowerShellSemanticSymbolBinding> symbols,
        IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        Type? contextualType = null,
        string? targetFramework = null,
        PowerShellCompilationCapability capabilities = PowerShellCompilationCapability.None)
    {
        syntax = UnwrapExpression(syntax);
        var span = PowerShellSourceParser.GetSpan(document, syntax.Extent);
        var functionBody = FindOwningFunctionBody(syntax);
        if (functionBody is not null &&
            PowerShellRuntimeStateSemanticBinder.TryBind(
                document,
                syntax,
                functionBody,
                targetFramework,
                capabilities,
                (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                diagnostics,
                out var runtimeState))
            return runtimeState;
        switch (syntax)
        {
            case ExpandableStringExpressionAst expandable:
                return PowerShellStringSemanticBinder.BindInterpolated(
                    document,
                    expandable,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    diagnostics);
            case StringConstantExpressionAst text:
                return new PowerShellBoundLiteralExpression(span, text.Value, LiteralType(typeof(string), "String literal syntax determines the CLR representation."), PowerShellValueState.Known);
            case ConstantExpressionAst constant:
                return new PowerShellBoundLiteralExpression(span, constant.Value, LiteralType(constant.Value?.GetType() ?? typeof(object), "Literal syntax determines the CLR representation."), constant.Value is null ? PowerShellValueState.Null : PowerShellValueState.Known);
            case ArrayLiteralAst array:
                return PowerShellArraySemanticBinder.Bind(
                    document,
                    array,
                    array.Elements,
                    PowerShellBoundArrayKind.Literal,
                    contextualType,
                    (item, elementType) => BindExpression(document, item, symbols, functions, diagnostics, elementType, targetFramework, capabilities),
                    diagnostics);
            case ArrayExpressionAst array:
            {
                var elements = new List<ExpressionAst>();
                foreach (var statement in array.SubExpression.Statements)
                {
                    if (statement is not PipelineAst { PipelineElements.Count: 1 } pipeline ||
                        pipeline.PipelineElements[0] is not CommandExpressionAst command)
                    {
                        diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2501", "Typed @() expressions accept only side-effect-free expression statements.", PowerShellSourceParser.GetSpan(document, statement.Extent)));
                        return null;
                    }
                    if (command.Expression is ArrayLiteralAst literal) elements.AddRange(literal.Elements);
                    else elements.Add(command.Expression);
                }
                return PowerShellArraySemanticBinder.Bind(
                    document,
                    array,
                    elements,
                    PowerShellBoundArrayKind.CollectedExpression,
                    contextualType,
                    (item, elementType) => BindExpression(document, item, symbols, functions, diagnostics, elementType, targetFramework, capabilities),
                    diagnostics);
            }
            case HashtableAst hashtable:
                return PowerShellDictionarySemanticBinder.BindLiteral(
                    document,
                    hashtable,
                    ordered: false,
                    contextualType,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    capabilities,
                    diagnostics);
            case VariableExpressionAst variable when variable.VariablePath.UserPath.Equals("true", StringComparison.OrdinalIgnoreCase):
                return new PowerShellBoundLiteralExpression(span, true, LiteralType(typeof(bool), "$true is a Boolean literal."), PowerShellValueState.Known);
            case VariableExpressionAst variable when variable.VariablePath.UserPath.Equals("false", StringComparison.OrdinalIgnoreCase):
                return new PowerShellBoundLiteralExpression(span, false, LiteralType(typeof(bool), "$false is a Boolean literal."), PowerShellValueState.Known);
            case VariableExpressionAst variable when variable.VariablePath.UserPath.Equals("null", StringComparison.OrdinalIgnoreCase):
                return new PowerShellBoundLiteralExpression(span, null, LiteralType(typeof(object), "$null has no narrower CLR representation."), PowerShellValueState.Null);
            case VariableExpressionAst variable when symbols.TryGetValue(variable.VariablePath.UserPath, out var symbol):
                return new PowerShellBoundVariableExpression(span, symbol.Symbol, symbol.Type, symbol.ValueState);
            case ConvertExpressionAst conversion when PowerShellDictionarySemanticBinder.IsOrderedHashtableConversion(conversion):
                return PowerShellDictionarySemanticBinder.BindLiteral(
                    document,
                    (HashtableAst)conversion.Child,
                    ordered: true,
                    contextualType,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    capabilities,
                    diagnostics);
            case ConvertExpressionAst conversion when PowerShellObjectConstructionPolicy.IsLiteral(conversion):
                return PowerShellObjectSemanticBinder.Bind(
                    document,
                    conversion,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    capabilities,
                    diagnostics);
            case ConvertExpressionAst conversion:
                return PowerShellConversionSemanticBinder.Bind(
                    document,
                    conversion,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    targetFramework,
                    capabilities,
                    diagnostics);
            case BinaryExpressionAst binary:
                return PowerShellOperatorSemanticBinder.BindBinary(
                    binary,
                    span,
                    operand => BindExpression(document, operand, symbols, functions, diagnostics, targetFramework: targetFramework, capabilities: capabilities),
                    diagnostics,
                    targetFramework,
                    capabilities);
            case UnaryExpressionAst unary:
                if (PowerShellMutationSemanticBinder.TryBindIncrement(document, unary, symbols, out var mutation, diagnostics)) return mutation;
                return PowerShellOperatorSemanticBinder.BindUnary(
                    unary,
                    span,
                    operand => BindExpression(document, operand, symbols, functions, diagnostics, targetFramework: targetFramework, capabilities: capabilities),
                    diagnostics,
                    capabilities);
            case AssignmentStatementAst assignment:
                return PowerShellMutationSemanticBinder.BindAssignment(
                    document,
                    assignment,
                    symbols,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    diagnostics);
            case IndexExpressionAst index:
                return PowerShellDictionarySemanticBinder.BindIndex(
                    document,
                    index,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    capabilities,
                    diagnostics);
            case InvokeMemberExpressionAst invocation when PowerShellBoundParametersPolicy.TryGetContainsKey(invocation, out var parameterName):
                if (functionBody?.ParamBlock?.Parameters.Any(parameter =>
                        parameter.Name.VariablePath.UserPath.Equals(parameterName, StringComparison.OrdinalIgnoreCase)) != true)
                {
                    diagnostics.Add(new PowerShellSemanticDiagnostic(
                        "PSB2502",
                        $"$PSBoundParameters.ContainsKey requires the literal canonical name of a declared parameter; '{parameterName}' is not declared by this function.",
                        span));
                    return null;
                }
                return new PowerShellBoundParameterPresenceExpression(span, parameterName);
            case InvokeMemberExpressionAst invocation:
                return PowerShellClrMemberSemanticBinder.BindInvocation(
                    document,
                    invocation,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    targetFramework,
                    capabilities,
                    diagnostics);
            case MemberExpressionAst member:
                return PowerShellClrMemberSemanticBinder.BindMember(
                    document,
                    member,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    targetFramework,
                    capabilities,
                    diagnostics);
            case CommandAst command when TryGetLocalFunction(command, functions, out var target):
                return PowerShellLocalCallSemanticBinder.Bind(
                    document,
                    command,
                    target,
                    (item, itemType) => BindExpression(document, item, symbols, functions, diagnostics, itemType, targetFramework, capabilities),
                    targetFramework,
                    capabilities,
                    diagnostics);
            default:
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2101", $"Expression '{syntax.GetType().Name}' is not yet represented by the bound pipeline.", span));
                return null;
        }
    }

    private static ScriptBlockAst? FindOwningFunctionBody(Ast syntax)
    {
        for (var current = syntax; current is not null; current = current.Parent)
        {
            if (current is FunctionDefinitionAst function) return function.Body;
        }
        return null;
    }

    private static bool IsLocalFunctionPipeline(PipelineAst pipeline, IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions)
        => pipeline.PipelineElements.Count == 1 && pipeline.PipelineElements[0] is CommandAst command && TryGetLocalFunction(command, functions, out _);

    private static bool TryGetLocalFunction(CommandAst command, IReadOnlyDictionary<string, PowerShellLocalCallSignature> functions, out PowerShellLocalCallSignature target)
    {
        var name = command.GetCommandName();
        if (!string.IsNullOrWhiteSpace(name) && functions.TryGetValue(name, out target!)) return true;
        target = null!;
        return false;
    }

    private static PowerShellTypeFact LiteralType(Type type, string explanation)
        => new(type, PowerShellTypeFactProvenance.Literal, explanation);

    private static PowerShellBoundExpression? BindConditionTruthiness(
        PowerShellBoundExpression condition,
        PowerShellCompilationCapability capabilities,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        if (condition.Type.ClrType == typeof(bool)) return condition;
        if (!capabilities.HasFlag(PowerShellCompilationCapability.PowerShellLanguageConversions))
        {
            var message = condition is PowerShellBoundMutationExpression { Operation: PowerShellBoundMutationOperator.Assign } mutation
                ? $"Local variable '${mutation.Target.Name}' may remain unassigned because its assignment occurs only while evaluating a dynamic-truthiness condition."
                : "PowerShell truthiness conversion is dynamic; typed conditions must already be Boolean.";
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2301", message, condition.Span));
            return null;
        }
        return new PowerShellBoundConversionExpression(
            condition.Span,
            new PowerShellTypeFact(typeof(bool), PowerShellTypeFactProvenance.Inferred, "PowerShell-hosted condition truthiness selects one Boolean result."),
            condition,
            usePowerShellTruthiness: true);
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

}
