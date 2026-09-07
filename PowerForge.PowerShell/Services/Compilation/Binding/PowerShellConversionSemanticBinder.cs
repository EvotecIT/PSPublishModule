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
        string semanticProfileId,
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

        if (PowerShellCompilationLiteralPolicy.TryResolveValue(syntax, targetType, targetFramework, semanticProfileId, out var value) &&
            PowerShellCompilationLiteralPolicy.CanEmitBoundValue(value, targetType))
            return BindResolvedLiteral(span, targetType, value);

        var operand = bindExpression(syntax.Child, targetType);
        if (operand is null) return null;
        if (targetType == typeof(string) && BindClosedStringConversion(operand) is { } stringValue)
            return stringValue;
        var usePowerShellLanguageRuntime = !PowerShellClrTypeSemantics.CanAssign(targetType, operand.Type.ClrType);
        if (usePowerShellLanguageRuntime && !capabilities.HasFlag(PowerShellCompilationCapability.PowerShellLanguageConversions))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(
                "PSB2202",
                $"Conversion from '{operand.Type.ClrType.FullName}' to '{targetType.FullName}' requires the PowerShell language-conversion runtime.",
                span));
            return null;
        }

        var preserveDictionaryShape = !usePowerShellLanguageRuntime &&
            operand.Type.DictionaryValueKind is PowerShellDictionaryValueKind.String or PowerShellDictionaryValueKind.Object;
        return new PowerShellBoundConversionExpression(
            span,
            new PowerShellTypeFact(targetType, PowerShellTypeFactProvenance.Explicit, "An authored conversion selects a CLR-compatible representation.",
                preserveDictionaryShape ? operand.Type.KnownProperties : null,
                preserveDictionaryShape ? operand.Type.DictionaryValueKind : PowerShellDictionaryValueKind.None),
            operand,
            usePowerShellLanguageRuntime);
    }

    /// <summary>Preserves PowerShell's null-to-empty conversion for a closed string value.</summary>
    internal static PowerShellBoundExpression? BindClosedStringConversion(PowerShellBoundExpression operand)
    {
        var type = new PowerShellTypeFact(typeof(string), PowerShellTypeFactProvenance.Explicit,
            "PowerShell string conversion normalizes null to an empty string.");
        if (operand is PowerShellBoundLiteralExpression { Value: null })
            return new PowerShellBoundLiteralExpression(operand.Span, string.Empty, type, PowerShellValueState.Known);
        if (operand.Type.ClrType != typeof(string)) return null;
        if (operand.ValueState == PowerShellValueState.Known) return operand;
        return new PowerShellBoundConversionExpression(operand.Span, type, operand, normalizeNullString: true);
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
