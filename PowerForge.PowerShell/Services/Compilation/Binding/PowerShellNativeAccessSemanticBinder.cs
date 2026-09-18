using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Binds access syntax while leaving runtime adapters and overload selection with the native invocation.</summary>
internal static class PowerShellNativeAccessSemanticBinder
{
    internal static PowerShellBoundExpression? BindMember(ParsedSourceDocument document, MemberExpressionAst syntax,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression, string? targetFramework,
        PowerShellCompilationCapability capabilities, ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var span = PowerShellSourceParser.GetSpan(document, syntax.Extent);
        if (syntax.GetType().GetProperty("NullConditional")?.GetValue(syntax) is true)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2630",
                "Native null-conditional member reads retain their PowerShell expression boundary.", span));
            return null;
        }
        if (syntax.Member is StringConstantExpressionAst name && !syntax.Static)
        {
            var literalReceiver = bindExpression(syntax.Expression, null);
            return literalReceiver is null ? null : new PowerShellBoundNativeMemberExpression(span, literalReceiver, name.Value);
        }

        Type? literalTargetType = null;
        PowerShellBoundExpression? receiver = null;
        if (syntax.Expression is TypeExpressionAst typeSyntax)
        {
            literalTargetType = typeSyntax.TypeName.GetReflectionType();
            if (literalTargetType is null || !PowerShellCompilationParameterTypePolicy.CanUseInMethod(literalTargetType, targetFramework, capabilities))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2611",
                    $"CLR type '{typeSyntax.TypeName.FullName}' is not available in the generated project reference set for the requested target.", span));
                return null;
            }
        }
        else
        {
            receiver = bindExpression(syntax.Expression, null);
            if (receiver is null) return null;
        }
        var memberName = bindExpression(syntax.Member, null);
        return memberName is null ? null : new PowerShellBoundNativeMemberExpression(
            span, receiver, literalTargetType, memberName, syntax.Static);
    }

    internal static PowerShellBoundExpression? BindInvocation(ParsedSourceDocument document, InvokeMemberExpressionAst syntax,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression, string? targetFramework,
        PowerShellCompilationCapability capabilities, ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var span = PowerShellSourceParser.GetSpan(document, syntax.Extent);
        if (syntax.GetType().GetProperty("NullConditional")?.GetValue(syntax) is true ||
            syntax.GetType().GetProperty("GenericTypeArguments")?.GetValue(syntax) is System.Collections.ICollection { Count: > 0 } ||
            syntax.Member is not StringConstantExpressionAst name)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2632",
                "Native computed method names, explicit generic arguments, and null-conditional calls retain their PowerShell expression boundary.", span));
            return null;
        }
        Type? literalTargetType = null;
        PowerShellBoundExpression? receiver = null;
        if (syntax.Expression is TypeExpressionAst typeSyntax)
        {
            literalTargetType = typeSyntax.TypeName.GetReflectionType();
            if (literalTargetType is null || !PowerShellCompilationParameterTypePolicy.CanUseInMethod(literalTargetType, targetFramework, capabilities))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2611",
                    $"CLR type '{typeSyntax.TypeName.FullName}' is not available in the generated project reference set for the requested target.", span));
                return null;
            }
        }
        else
        {
            receiver = bindExpression(syntax.Expression, null);
            if (receiver is null) return null;
        }
        var argumentSyntax = syntax.Arguments?.ToArray() ?? Array.Empty<ExpressionAst>();
        var arguments = new List<PowerShellBoundExpression>();
        foreach (var argument in argumentSyntax)
        {
            var value = bindExpression(argument, null);
            if (value is null) return null;
            arguments.Add(value);
        }
        return new PowerShellBoundNativeInvocationExpression(span, receiver, literalTargetType, name.Value, syntax.Static,
            arguments.ToArray(), GetConstraint(syntax.Expression), argumentSyntax.Select(GetConstraint).ToArray());
    }

    internal static PowerShellBoundExpression? BindIndex(ParsedSourceDocument document, IndexExpressionAst syntax,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression, ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var span = PowerShellSourceParser.GetSpan(document, syntax.Extent);
        if (syntax.GetType().GetProperty("NullConditional")?.GetValue(syntax) is true)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2631", "Native null-conditional indexing retains its PowerShell expression boundary.", span));
            return null;
        }
        var receiver = bindExpression(syntax.Target, null);
        if (receiver is null) return null;
        var arguments = new List<PowerShellBoundExpression>();
        var indices = syntax.Index is ArrayLiteralAst { Elements.Count: > 1 } array
            ? array.Elements.ToArray() : new[] { syntax.Index };
        foreach (var index in indices)
        {
            var value = bindExpression(index, null);
            if (value is null) return null;
            arguments.Add(value);
        }
        return new PowerShellBoundNativeIndexExpression(span, receiver, arguments.ToArray(),
            GetConstraint(syntax.Target), GetConstraint(syntax.Index));
    }

    // Authored casts constrain overload selection independently of the value's runtime type.
    private static Type? GetConstraint(ExpressionAst? syntax)
    {
        while (syntax is ParenExpressionAst parenthesized)
            syntax = parenthesized.Pipeline is PipelineAst pipeline ? pipeline.GetPureExpression() : null;
        while (syntax is AttributedExpressionAst attributed)
        {
            if (attributed is ConvertExpressionAst conversion && conversion.Type.TypeName.GetReflectionType() is { } type &&
                type != typeof(System.Management.Automation.PSReference)) return type;
            syntax = attributed.Child;
        }
        return null;
    }
}
