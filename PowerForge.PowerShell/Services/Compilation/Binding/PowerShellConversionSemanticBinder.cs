using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Owns compile-time-safe authored PowerShell conversion binding.</summary>
internal static class PowerShellConversionSemanticBinder
{
    internal static PowerShellBoundExpression? Bind(
        ParsedSourceDocument document,
        ConvertExpressionAst syntax,
        Func<Ast, Type?, PowerShellBoundExpression?> bindExpression,
        string? targetFramework,
        PowerShellCompilationCapability capabilities,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var span = PowerShellSourceParser.GetSpan(document, syntax.Extent);
        var targetType = syntax.StaticType;
        if (targetType == typeof(void) || !PowerShellCompilationParameterTypePolicy.CanUseInMethod(targetType, targetFramework, capabilities))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2201", $"Conversion target '{targetType.FullName}' is not available in the generated target contract.", span));
            return null;
        }

        if (PowerShellCompilationLiteralPolicy.TryResolveValue(syntax, targetType, out var value))
            return BindResolvedLiteral(span, targetType, value);

        var operand = bindExpression(syntax.Child, targetType);
        if (operand is null) return null;
        var usePowerShellLanguageRuntime = !PowerShellClrTypeSemantics.CanAssign(targetType, operand.Type.ClrType);
        if (usePowerShellLanguageRuntime && !capabilities.HasFlag(PowerShellCompilationCapability.PowerShellLanguageConversions))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2202",
                $"Conversion from '{operand.Type.ClrType.FullName}' to '{targetType.FullName}' requires the PowerShell language-conversion runtime.",
                span));
            return null;
        }

        return new PowerShellBoundConversionExpression(
            span,
            new PowerShellTypeFact(targetType, PowerShellTypeFactProvenance.Explicit, "An authored conversion selects a CLR-compatible representation."),
            operand,
            usePowerShellLanguageRuntime);
    }

    private static PowerShellBoundExpression BindResolvedLiteral(SourceSpan span, Type targetType, object? value)
    {
        if (targetType.IsArray && value is Array array)
        {
            var elementType = targetType.GetElementType()!;
            var elements = array.Cast<object?>()
                .Select(item => (PowerShellBoundExpression)new PowerShellBoundLiteralExpression(
                    span,
                    item,
                    new PowerShellTypeFact(elementType, PowerShellTypeFactProvenance.Literal, "Compile-time PowerShell conversion resolved this array element."),
                    item is null ? PowerShellValueState.Null : PowerShellValueState.Known))
                .ToArray();
            return new PowerShellBoundArrayExpression(span, targetType, PowerShellBoundArrayKind.Literal, elements);
        }

        return new PowerShellBoundLiteralExpression(
            span,
            value,
            new PowerShellTypeFact(targetType, PowerShellTypeFactProvenance.Literal, "Compile-time PowerShell conversion resolved one target-typed literal."),
            value is null ? PowerShellValueState.Null : PowerShellValueState.Known);
    }
}
