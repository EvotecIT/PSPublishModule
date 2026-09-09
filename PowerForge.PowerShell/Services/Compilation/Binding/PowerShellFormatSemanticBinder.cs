namespace PowerForge;

/// <summary>Admits host formatting with invocation-owned callbacks or a qualified scalar argument.</summary>
internal static class PowerShellFormatSemanticBinder
{
    internal static PowerShellBoundExpression? Bind(
        SourceSpan span,
        PowerShellBoundExpression format,
        PowerShellBoundExpression value,
        PowerShellCompilationCapability capabilities,
        ICollection<PowerShellSemanticDiagnostic> diagnostics)
    {
        var type = value.Type.ClrType;
        var native = capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding);
        var scalar = PowerShellClrTypeSemantics.IsNumeric(type) || type == typeof(string) ||
            type == typeof(bool) || type == typeof(char) || type == typeof(DateTime) ||
            type == typeof(TimeSpan) || type == typeof(Guid) ||
            value.Type.Provenance == PowerShellTypeFactProvenance.Int32OrDouble;
        if (format.Type.ClrType == typeof(string) && (scalar || native) &&
            capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStatementErrors))
            return new PowerShellBoundBinaryExpression(span, PowerShellBoundBinaryOperator.PowerShellScalarFormat,
                format, value, new PowerShellTypeFact(typeof(string), PowerShellTypeFactProvenance.Inferred,
                    "Formatting uses the loaded PowerShell host's format and error contract."), usesNativeInvocation: native);

        if (PowerShellRuntimeFreeFormatPolicy.IsSafe(format, value.Type))
            return new PowerShellBoundBinaryExpression(span, PowerShellBoundBinaryOperator.RuntimeFreeScalarFormat,
                format, value, new PowerShellTypeFact(typeof(string), PowerShellTypeFactProvenance.Inferred,
                    "The numeric format grammar and every interpolated fragment are statically qualified for CLR formatting."));

        diagnostics.Add(new PowerShellSemanticDiagnostic(PowerShellCompilationFeatureIds.ForOperator("format"),
            "Formatting requires a String template and either a qualified scalar argument or native invocation storage; runtime-independent formatting requires a separately proven safe template.", span));
        return null;
    }
}
