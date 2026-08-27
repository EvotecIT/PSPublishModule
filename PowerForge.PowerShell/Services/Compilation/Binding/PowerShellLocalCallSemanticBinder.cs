using System.Management.Automation.Language;

namespace PowerForge;

internal sealed class PowerShellLocalCallParameter
{
    internal PowerShellLocalCallParameter(PowerShellSymbolId symbol, Type type, PowerShellCompilationParameter contract)
    {
        Symbol = symbol;
        Type = type;
        Contract = contract;
    }

    internal PowerShellSymbolId Symbol { get; }
    internal Type Type { get; }
    internal PowerShellCompilationParameter Contract { get; }
}

internal sealed class PowerShellLocalCallSignature
{
    internal PowerShellLocalCallSignature(
        PowerShellSymbolId symbol,
        PowerShellLocalCallParameter[] parameters,
        bool isAdvanced,
        PowerShellCompilationCommandBinding commandBinding,
        Type? declaredReturnType)
    {
        Symbol = symbol;
        Parameters = parameters;
        IsAdvanced = isAdvanced;
        CommandBinding = commandBinding;
        DeclaredReturnType = declaredReturnType;
    }

    internal PowerShellSymbolId Symbol { get; }
    internal PowerShellLocalCallParameter[] Parameters { get; }
    internal bool IsAdvanced { get; }
    internal PowerShellCompilationCommandBinding CommandBinding { get; }
    internal Type? DeclaredReturnType { get; }
}

/// <summary>Applies deterministic local-function parameter binding before call-graph analysis.</summary>
internal static class PowerShellLocalCallSemanticBinder
{
    internal static PowerShellLocalCallSignature CreateSignature(
        ParsedSourceDocument document,
        FunctionDefinitionAst function,
        PowerShellSymbolId symbol,
        string? targetFramework,
        PowerShellCompilationCapability capabilities)
    {
        var parameters = (function.Body.ParamBlock?.Parameters.ToArray() ?? Array.Empty<ParameterAst>())
            .Select(parameter =>
            {
                var name = parameter.Name.VariablePath.UserPath;
                var span = PowerShellSourceParser.GetSpan(document, parameter.Extent);
                var parameterSymbol = new PowerShellSymbolId(PowerShellSymbolKind.Parameter, document.DocumentId, name, span, function.Name + "/parameter/" + name);
                var type = parameter.StaticType == typeof(System.Management.Automation.SwitchParameter) ? typeof(bool) : parameter.StaticType;
                return new PowerShellLocalCallParameter(parameterSymbol, type, PowerShellParameterContractBinder.Bind(parameter, targetFramework));
            })
            .ToArray();
        PowerShellOutputTypeSemanticPolicy.TryResolve(
            function.Body,
            targetFramework,
            capabilities,
            out var declaredReturnType,
            out _,
            out _);
        declaredReturnType ??= InferReturnType(function, parameters);
        return new PowerShellLocalCallSignature(
            symbol,
            parameters,
            PowerShellAdvancedFunctionPolicy.IsAdvanced(function),
            PowerShellAdvancedFunctionPolicy.GetBinding(function.Body.ParamBlock),
            declaredReturnType);
    }

    private static Type? InferReturnType(FunctionDefinitionAst function, IReadOnlyList<PowerShellLocalCallParameter> parameters)
    {
        var parameterTypes = parameters.ToDictionary(static parameter => parameter.Symbol.Name, static parameter => parameter.Type, StringComparer.OrdinalIgnoreCase);
        var output = function.Body.EndBlock?.Statements
            .SelectMany(static statement => statement.FindAll(
                static node => node is ReturnStatementAst || node is PipelineAst && node.Parent is NamedBlockAst,
                searchNestedScriptBlocks: false))
            .Select(node => node switch
            {
                ReturnStatementAst { Pipeline: not null } returned => InferExpressionType(Unwrap(returned.Pipeline), parameterTypes),
                PipelineAst pipeline => InferExpressionType(Unwrap(pipeline), parameterTypes),
                _ => null
            })
            .Where(static type => type is not null && type != typeof(void))
            .Cast<Type>()
            .Distinct()
            .ToArray() ?? Array.Empty<Type>();
        return output.Length == 1 ? output[0] : null;
    }

    private static Type? InferExpressionType(Ast syntax, IReadOnlyDictionary<string, Type> parameterTypes)
        => syntax switch
        {
            StringConstantExpressionAst => typeof(string),
            ConstantExpressionAst constant => constant.Value?.GetType() ?? typeof(object),
            VariableExpressionAst variable when parameterTypes.TryGetValue(variable.VariablePath.UserPath, out var type) => type,
            ConvertExpressionAst conversion when conversion.StaticType != typeof(object) => conversion.StaticType,
            ArrayLiteralAst array => InferArrayType(array.Elements, parameterTypes),
            ArrayExpressionAst array => InferArrayType(
                array.SubExpression.Statements
                    .OfType<PipelineAst>()
                    .SelectMany(static pipeline => pipeline.PipelineElements.OfType<CommandExpressionAst>())
                    .Select(static expression => expression.Expression),
                parameterTypes),
            _ => syntax is ExpressionAst expression && expression.StaticType != typeof(object) ? expression.StaticType : null
        };

    private static Type? InferArrayType(IEnumerable<ExpressionAst> elements, IReadOnlyDictionary<string, Type> parameterTypes)
    {
        var types = elements.Select(element => InferExpressionType(Unwrap(element), parameterTypes)).Distinct().ToArray();
        return types.Length == 1 && types[0] is not null ? types[0]!.MakeArrayType() : null;
    }

    private static Ast Unwrap(Ast syntax)
    {
        while (syntax is PipelineAst { PipelineElements.Count: 1 } pipeline)
            syntax = pipeline.PipelineElements[0];
        while (syntax is CommandExpressionAst command) syntax = command.Expression;
        while (syntax is ParenExpressionAst parenthesized) syntax = parenthesized.Pipeline;
        return syntax;
    }

    internal static PowerShellBoundInvocationExpression? Bind(
        ParsedSourceDocument document,
        CommandAst command,
        PowerShellLocalCallSignature signature,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression,
        string? targetFramework,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var span = PowerShellSourceParser.GetSpan(document, command.Extent);
        if (command.Redirections.Count != 0)
            return Reject(diagnostics, "PSB2801", "Typed local function calls do not support stream redirection.", span);
        if (signature.Parameters.SelectMany(static parameter => parameter.Contract.Bindings)
            .Any(static binding => !string.IsNullOrWhiteSpace(binding.ParameterSetName)))
            return Reject(diagnostics, "PSB2802", $"Local function '{signature.Symbol.Name}' uses named parameter sets that require PowerShell binding.", span);

        var bound = new Dictionary<int, PowerShellBoundExpression>();
        var authoredOrder = new List<int>();
        var positionalIndex = 0;
        var elements = command.CommandElements.Skip(1).ToArray();
        for (var elementIndex = 0; elementIndex < elements.Length; elementIndex++)
        {
            int parameterIndex;
            Ast argumentSyntax;
            if (elements[elementIndex] is CommandParameterAst named)
            {
                parameterIndex = ResolveParameter(signature, named.ParameterName, targetFramework, diagnostics, PowerShellSourceParser.GetSpan(document, named.Extent));
                if (parameterIndex < 0) return null;
                if (bound.ContainsKey(parameterIndex))
                    return Reject(diagnostics, "PSB2803", $"Local function parameter '-{signature.Parameters[parameterIndex].Contract.Name}' is bound more than once.", PowerShellSourceParser.GetSpan(document, named.Extent));
                if (signature.Parameters[parameterIndex].Contract.IsSwitch && named.Argument is null)
                {
                    bound[parameterIndex] = Literal(span, true, typeof(bool));
                    authoredOrder.Add(parameterIndex);
                    continue;
                }
                if (named.Argument is not null) argumentSyntax = named.Argument;
                else if (elementIndex + 1 < elements.Length && elements[elementIndex + 1] is ExpressionAst expression)
                {
                    argumentSyntax = expression;
                    elementIndex++;
                }
                else return Reject(diagnostics, "PSB2804", $"Local function parameter '-{signature.Parameters[parameterIndex].Contract.Name}' requires a statically typed argument.", PowerShellSourceParser.GetSpan(document, named.Extent));
            }
            else if (elements[elementIndex] is ExpressionAst positional)
            {
                if (!signature.CommandBinding.PositionalBinding || signature.Parameters.SelectMany(static parameter => parameter.Contract.Bindings).Any(static binding => binding.Position.HasValue))
                    return Reject(diagnostics, "PSB2805", $"Local function '{signature.Symbol.Name}' uses an explicit positional-binding contract that is not represented by source-order calls.", PowerShellSourceParser.GetSpan(document, positional.Extent));
                while (positionalIndex < signature.Parameters.Length && bound.ContainsKey(positionalIndex)) positionalIndex++;
                if (positionalIndex >= signature.Parameters.Length)
                    return Reject(diagnostics, "PSB2806", $"Local function '{signature.Symbol.Name}' received too many positional arguments.", PowerShellSourceParser.GetSpan(document, positional.Extent));
                parameterIndex = positionalIndex++;
                argumentSyntax = positional;
            }
            else return Reject(diagnostics, "PSB2807", "Typed local calls accept scalar named or positional arguments only.", PowerShellSourceParser.GetSpan(document, elements[elementIndex].Extent));

            var parameter = signature.Parameters[parameterIndex];
            var argument = bindExpression(argumentSyntax, parameter.Type);
            if (argument is null) return null;
            if (!PowerShellClrTypeSemantics.CanAssign(parameter.Type, argument.Type.ClrType) && !(argument.ValueState == PowerShellValueState.Null && !parameter.Type.IsValueType))
                return Reject(diagnostics, "PSB2808", $"Argument for '-{parameter.Contract.Name}' has CLR type '{argument.Type.ClrType.FullName}', not assignable to '{parameter.Type.FullName}'.", argument.Span);
            bound[parameterIndex] = argument;
            authoredOrder.Add(parameterIndex);
        }

        var arguments = new PowerShellBoundExpression[signature.Parameters.Length];
        for (var index = 0; index < signature.Parameters.Length; index++)
        {
            if (bound.TryGetValue(index, out var value)) { arguments[index] = value; continue; }
            var parameter = signature.Parameters[index];
            if (parameter.Contract.IsMandatory)
                return Reject(diagnostics, "PSB2809", $"Mandatory local function parameter '-{parameter.Contract.Name}' was not supplied.", span);
            arguments[index] = DefaultValue(span, parameter.Type);
        }
        var returnType = signature.DeclaredReturnType is null
            ? PowerShellTypeFact.Unknown
            : new PowerShellTypeFact(signature.DeclaredReturnType, PowerShellTypeFactProvenance.Explicit, $"Local function '{signature.Symbol.Name}' declares its success-output type.");
        return new PowerShellBoundInvocationExpression(
            span,
            signature.Symbol,
            arguments,
            returnType,
            authoredOrder.ToArray(),
            bound.Keys.Select(index => signature.Parameters[index].Contract.Name).OrderBy(static name => name, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static int ResolveParameter(
        PowerShellLocalCallSignature signature,
        string name,
        string? targetFramework,
        ICollection<PowerShellSemanticDiagnostic> diagnostics,
        SourceSpan span)
    {
        var exact = signature.Parameters.Select((parameter, index) => new { parameter, index }).Where(item =>
            item.parameter.Contract.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
            item.parameter.Contract.Aliases.Any(alias => alias.Equals(name, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (exact.Length == 1) return exact[0].index;
        var abbreviated = signature.Parameters.Select((parameter, index) => new { parameter, index }).Where(item =>
            item.parameter.Contract.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase) ||
            item.parameter.Contract.Aliases.Any(alias => alias.StartsWith(name, StringComparison.OrdinalIgnoreCase))).ToArray();
        var common = PowerShellCommonParameterPolicy.GetStandard(signature.IsAdvanced, targetFramework);
        var commonMatches = common.Count(parameter => parameter.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase) || parameter.Alias.StartsWith(name, StringComparison.OrdinalIgnoreCase));
        if (abbreviated.Length == 1 && commonMatches == 0) return abbreviated[0].index;
        diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2810", abbreviated.Length == 0 && commonMatches == 0
            ? $"Local function '{signature.Symbol.Name}' has no parameter matching '-{name}'."
            : $"Local function parameter abbreviation '-{name}' is ambiguous or names an unsupported common parameter.", span));
        return -1;
    }

    private static PowerShellBoundExpression DefaultValue(SourceSpan span, Type type)
        => Literal(span, type.IsValueType ? Activator.CreateInstance(type) : type == typeof(string) ? string.Empty : null, type);

    private static PowerShellBoundExpression Literal(SourceSpan span, object? value, Type type)
        => new PowerShellBoundLiteralExpression(span, value, new PowerShellTypeFact(type, PowerShellTypeFactProvenance.Literal, "The local-call binder materializes one omitted or switch argument placeholder."), value is null ? PowerShellValueState.Null : PowerShellValueState.Known);

    private static PowerShellBoundInvocationExpression? Reject(ICollection<PowerShellSemanticDiagnostic> diagnostics, string code, string message, SourceSpan span)
    {
        diagnostics.Add(new PowerShellSemanticDiagnostic(code, message, span));
        return null;
    }
}
