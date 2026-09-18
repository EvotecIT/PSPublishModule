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
        if (syntax.Type.TypeName.FullName.Equals("PSCustomObject", StringComparison.OrdinalIgnoreCase))
        {
            if (!capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding))
            {
                diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2202",
                    "An authored PSCustomObject cast requires the native custom-object conversion contract.", span));
                return null;
            }
            var customObjectOperand = bindExpression(syntax.Child, typeof(object));
            return customObjectOperand is null ? null : new PowerShellBoundConversionExpression(span,
                new PowerShellTypeFact(typeof(object), PowerShellTypeFactProvenance.Explicit,
                    "The authored PSCustomObject alias selects custom-object conversion rather than a PSObject wrapper."),
                customObjectOperand, useNativeConversion: true, useNativeCustomObjectConversion: true);
        }
        var targetType = syntax.Type.TypeName.GetReflectionType();
        if (targetType is null)
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2201",
                $"Conversion target '{syntax.Type.TypeName.FullName}' requires runtime type resolution.", span));
            return null;
        }
        if (targetType == typeof(void))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2201", $"Conversion target '{targetType.FullName}' is not available in the generated target contract.", span));
            return null;
        }
        if (!PowerShellCompilationParameterTypePolicy.CanUseInMethod(targetType, targetFramework, capabilities))
        {
            if (capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding))
            {
                var runtimeOperand = bindExpression(syntax.Child, typeof(object));
                return runtimeOperand is null ? null : new PowerShellBoundConversionExpression(
                    span,
                    new PowerShellTypeFact(typeof(object), PowerShellTypeFactProvenance.Explicit,
                        "A target-incompatible CLR representation stays object-valued while the active PowerShell host resolves and applies the authored conversion."),
                    runtimeOperand,
                    nativeSourcePath: document.Path,
                    nativeSourceText: PowerShellSourceParser.GetSourceLines(document, span),
                    useNativeConversion: true,
                    nativeRuntimeTypeName: syntax.Type.TypeName.FullName);
            }
            diagnostics.Add(new PowerShellSemanticDiagnostic("PSB2201", $"Conversion target '{targetType.FullName}' is not available in the generated target contract.", span));
            return null;
        }

        if (PowerShellCompilationLiteralPolicy.TryResolveValue(syntax, targetType, targetFramework, semanticProfileId, out var value) &&
            PowerShellCompilationLiteralPolicy.CanEmitBoundValue(value, targetType))
            return BindResolvedLiteral(span, targetType, value);

        var operand = bindExpression(syntax.Child, targetType);
        if (operand is null) return null;
        if (capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding))
            return new PowerShellBoundConversionExpression(span,
                new PowerShellTypeFact(targetType, PowerShellTypeFactProvenance.Explicit,
                    "An authored cast uses the native conversion binder in the active invocation."), operand, useNativeConversion: true);
        if (targetType == typeof(string) && BindClosedStringConversion(operand) is { } stringValue)
            return stringValue;
        if (BindClosedNumericConversion(operand, targetType, capabilities) is { } numericValue)
            return numericValue;
        var usePowerShellLanguageRuntime = !PowerShellClrTypeSemantics.CanAssign(targetType, operand.Type.ClrType);
        if (usePowerShellLanguageRuntime && PowerShellStringificationScopePolicy.RequiresCallerScope(targetType, operand))
        {
            diagnostics.Add(new PowerShellSemanticDiagnostic(PowerShellStringificationScopePolicy.DiagnosticCode,
                PowerShellStringificationScopePolicy.DiagnosticMessage, span));
            return null;
        }
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

    /// <summary>Converts a closed numeric union through the runtime-free library's direct CLR exception contract.</summary>
    internal static PowerShellBoundExpression? BindClosedNumericConversion(PowerShellBoundExpression operand,
        Type targetType, PowerShellCompilationCapability capabilities)
    {
        if (targetType != typeof(int) || operand.Type.Provenance != PowerShellTypeFactProvenance.Int32OrDouble ||
            capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStatementErrors)) return null;
        return new PowerShellBoundClrInvocationExpression(operand.Span, typeof(Convert), nameof(Convert.ToInt32),
            PowerShellClrInvocationKind.StaticMethod, null, PowerShellClrReceiverBehavior.None,
            new[] { operand }, new[] { typeof(object) },
            new PowerShellTypeFact(typeof(int), PowerShellTypeFactProvenance.Explicit,
                "Closed Int32/Double conversion preserves integral values and midpoint-to-even rounding; out-of-range values raise the library's CLR conversion exception."));
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
