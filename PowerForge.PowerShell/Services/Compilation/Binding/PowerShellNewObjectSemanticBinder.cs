using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Normalizes one closed New-Object CLR-construction shape into the canonical CLR invocation IR.</summary>
internal static class PowerShellNewObjectSemanticBinder
{
    internal static bool IsSupportedShape(CommandAst command)
        => TryGetConstructionShape(command, out _, out _);

    internal static PowerShellBoundExpression? Bind(
        ParsedSourceDocument document,
        CommandAst command,
        PowerShellCompilationCommandProviderContract provider,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression,
        string? targetFramework,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var span = PowerShellSourceParser.GetSpan(document, command.Extent);
        if (provider.Family != PowerShellCompilationCommandFamily.ClrConstruction ||
            !TryGetConstructionShape(command, out var type, out var arguments))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                PowerShellCompilationFeatureIds.ForCommand("New-Object"),
                "Runtime-free New-Object requires one literal TypeName and an optional closed scalar literal or parenthesized literal argument list; Property, ComObject, dynamic type names, variables, splatting, nested arrays, redirection, and wider parameter binding remain hosted.",
                span));
            return null;
        }

        return PowerShellClrMemberSemanticBinder.BindConstructor(
            document,
            command,
            type,
            arguments,
            bindExpression,
            targetFramework,
            diagnostics);
    }

    internal static bool TryGetConstructionShape(
        CommandAst command,
        out Type type,
        out ExpressionAst[] arguments)
    {
        type = null!;
        arguments = Array.Empty<ExpressionAst>();
        if (command.InvocationOperator != TokenKind.Unknown ||
            command.Redirections.Count != 0 ||
            command.Parent is not PipelineAst { PipelineElements.Count: 1 } pipeline ||
            !ReferenceEquals(pipeline.PipelineElements[0], command))
            return false;

        var elements = command.CommandElements.Skip(1).ToArray();
        if (elements.Length == 0)
            return false;

        var index = 0;
        if (elements[index] is CommandParameterAst typeParameter)
        {
            if (typeParameter.Argument is not null ||
                !typeParameter.ParameterName.Equals("TypeName", StringComparison.OrdinalIgnoreCase))
                return false;
            index++;
        }
        if (index >= elements.Length || elements[index] is not StringConstantExpressionAst typeLiteral ||
            string.IsNullOrWhiteSpace(typeLiteral.Value))
            return false;
        index++;

        var resolved = new TypeName(typeLiteral.Extent, typeLiteral.Value).GetReflectionType();
        if (resolved is null)
            return false;
        type = resolved;
        if (index == elements.Length)
            return true;

        if (elements[index] is CommandParameterAst argumentParameter)
        {
            if (argumentParameter.Argument is not null ||
                !(argumentParameter.ParameterName.Equals("ArgumentList", StringComparison.OrdinalIgnoreCase) ||
                  argumentParameter.ParameterName.Equals("Args", StringComparison.OrdinalIgnoreCase)))
                return false;
            index++;
        }
        if (index != elements.Length - 1 || elements[index] is not ExpressionAst argumentList)
            return false;

        return TryUnwrapClosedLiteralArgumentList(argumentList, out arguments);
    }

    private static bool TryUnwrapClosedLiteralArgumentList(
        ExpressionAst expression,
        out ExpressionAst[] arguments)
    {
        arguments = Array.Empty<ExpressionAst>();
        while (expression is ParenExpressionAst parenthesized &&
               parenthesized.Pipeline is PipelineAst pipeline &&
               pipeline.PipelineElements.Count == 1 &&
               pipeline.PipelineElements[0] is CommandExpressionAst commandExpression)
            expression = commandExpression.Expression;

        var candidates = expression is ArrayLiteralAst array
            ? array.Elements.ToArray()
            : new[] { expression };
        if (candidates.Length == 0 || candidates.Any(static candidate => !IsClosedScalarLiteral(candidate)))
            return false;

        arguments = candidates;
        return true;
    }

    private static bool IsClosedScalarLiteral(ExpressionAst expression)
    {
        if (expression is VariableExpressionAst { Splatted: true })
            return false;
        return expression is ConstantExpressionAst or StringConstantExpressionAst;
    }
}
