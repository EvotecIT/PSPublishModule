namespace PowerForge;

/// <summary>Admits scalar formatting only where the loaded host owns its error semantics.</summary>
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
        var scalar = PowerShellClrTypeSemantics.IsNumeric(type) || type == typeof(string) ||
            type == typeof(bool) || type == typeof(char) || type == typeof(DateTime) ||
            type == typeof(TimeSpan) || type == typeof(Guid) ||
            value.Type.Provenance == PowerShellTypeFactProvenance.Int32OrDouble;
        if (format.Type.ClrType == typeof(string) && scalar &&
            capabilities.HasFlag(PowerShellCompilationCapability.PowerShellStatementErrors))
            return new PowerShellBoundBinaryExpression(span, PowerShellBoundBinaryOperator.PowerShellScalarFormat,
                format, value, new PowerShellTypeFact(typeof(string), PowerShellTypeFactProvenance.Inferred,
                    "Scalar formatting uses the loaded PowerShell host's format and error contract."));

        diagnostics.Add(new PowerShellSemanticDiagnostic(PowerShellCompilationFeatureIds.ForOperator("format"),
            "Formatting requires a String template, a qualified scalar argument, and the PowerShell statement-error host; runtime-independent formatting requires a separately proven safe template.", span));
        return null;
    }
}
